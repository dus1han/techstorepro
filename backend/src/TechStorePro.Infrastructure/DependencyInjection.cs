using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Infrastructure.Catalog;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Identity;
using TechStorePro.Infrastructure.Persistence;
using TechStorePro.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TechStorePro.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro");

                // EnableRetryOnFailure is deliberately NOT set. A retrying execution strategy
                // refuses user-initiated transactions — and this system depends on them: document
                // numbering holds a SELECT … FOR UPDATE across statements, and the stock ledger will
                // do the same. Retrying a transaction the strategy cannot see would also replay half
                // a stock movement, which is worse than a transient failure surfacing to the caller.
                //
                // If transient-fault retry is wanted later, it must be reintroduced by wrapping each
                // operation in an IExecutionStrategy.ExecuteAsync delegate, not by flipping this on.
            }));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ApplicationDbContextAccessor>();
        services.AddSingleton<IDateTime, SystemDateTime>();

        // Permission grants are cached per (user, company) for a couple of minutes; see
        // PermissionService for why they are not in the token.
        services.AddMemoryCache();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthSessionFactory, AuthSessionFactory>();

        services.AddScoped<ISettingsProvider, SettingsProvider>();
        services.AddScoped<IDocumentNumberGenerator, DocumentNumberGenerator>();

        // P2. Sales (P5) will call this and snapshot the result onto the invoice line.
        services.AddScoped<IPriceResolver, PriceResolver>();

        services.AddScoped<ReferenceDataSeeder>();

        return services;
    }
}
