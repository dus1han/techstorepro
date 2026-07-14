using System.Text;
using TechStorePro.API.Controllers;
using TechStorePro.API.Middleware;
using TechStorePro.API.Services;
using TechStorePro.Application;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Infrastructure;
using TechStorePro.Infrastructure.Identity;
using TechStorePro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Per-request identity and tenant, both read from the caller's JWT.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// One TenantContext per scope, exposed under both interfaces — they must be the same instance, or a
// background job would pin the company on one object and the DbContext would read the tenant off
// another and still see null.
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantSetter>(sp => sp.GetRequiredService<TenantContext>());

var jwt = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"] ?? throw new InvalidOperationException(
                    "Jwt:Key is not configured. Set it via user-secrets or the environment."))),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// Authorisation is by (feature, action), evaluated in the MediatR pipeline against the database —
// see PermissionBehaviour. Nothing is policy- or role-based here, because requirements §7 forbids
// fixed roles: [Authorize] on the controller means "authenticated", and nothing more.
// Two kinds of caller, told apart by a claim each — never by the absence of the other's.
//
// A platform admin's token has no company_id, and a null tenant switches the DbContext query filters
// off. So "Tenant" demands the company claim positively: an authenticated token that merely lacks a
// company is refused rather than falling through into a request that would read every company at once.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.Tenant,
        policy => policy.RequireAuthenticatedUser().RequireClaim(TokenService.CompanyIdClaim));

    options.AddPolicy(
        AuthorizationPolicies.Platform,
        policy => policy.RequireAuthenticatedUser().RequireClaim(TokenService.PlatformAdminClaim, "true"));
});

// The Idempotency-Key filter runs on every state-changing request that carries the header
// (api-design.md §5). It is registered globally rather than per-controller on purpose: a payment
// endpoint added later without remembering to opt in is exactly the endpoint that will be
// double-clicked at a till.
builder.Services.AddControllers(options => options.Filters.Add<IdempotencyFilter>());
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

const string FrontendCors = "frontend";
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCors, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:3000"])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCors);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

// Migrate and seed on start-up.
//
// Applying migrations from the app is right for local development and wrong for production, where
// two instances starting at once would race — development-plan.md has migrations running as a
// separate deploy step. Seeding reference data (features, setting definitions) is safe either way:
// it is idempotent and additive.
await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    if (app.Environment.IsDevelopment())
    {
        await context.Database.MigrateAsync();
    }

    var seeder = scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>();
    await seeder.SeedAsync();
}

app.Run();

/// <summary>Exposed so the integration tests can drive the real pipeline through WebApplicationFactory.</summary>
public partial class Program;
