using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Commands.Login;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Application.Platform;
using TechStorePro.Domain.Identity;
using TechStorePro.Infrastructure.Identity;
using TechStorePro.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// Authentication, against a real PostgreSQL.
///
/// <b>None of this had a single test before.</b> Login, token issuance, the permission service, the
/// owner-holds-everything rule — the entire surface that decides who may see what — was covered by
/// nothing at all, which is exactly why rewriting it was worth being frightened of. These tests exist
/// so the next person to touch it is not.
///
/// The thing they mostly defend is the tenancy boundary. A username is unique only *within* a company,
/// so the same name means different people in different shops, and a login that resolved the wrong one
/// would hand a stranger somebody else's business.
/// </summary>
public class AuthenticationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("techstorepro_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private const string Password = "Str0ng!Passw0rd";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext(null);
        await db.Database.MigrateAsync();

        // Settings are read per company by the real provider; these tests use a stub, so the only
        // thing the database needs is the schema.
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // --- Onboarding: the flow that replaced self-registration -----------------------------------

    [Fact]
    public async Task The_platform_onboards_a_company_and_its_first_user_together()
    {
        await using var db = CreateContext(null);

        var created = await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        created.CompanyCode.Should().Be("GULF01");
        created.OwnerLogin.Should().Be("admin@GULF01", "this is the string the operator reads out to the customer");

        // A company that cannot transact is not onboarded, it is half-created. Every later module
        // assumes a branch and a warehouse exist.
        var company = await db.IgnoringTenantFilter<Company>().FirstAsync(c => c.Id == created.CompanyId);
        company.IsActive.Should().BeTrue();

        (await db.IgnoringTenantFilter<Branch>().CountAsync(b => b.CompanyId == created.CompanyId))
            .Should().Be(1);

        var owner = await db.IgnoringTenantFilter<User>().FirstAsync(u => u.Id == created.OwnerUserId);
        owner.IsOwner.Should().BeTrue("without an owner the company would contain nobody able to grant anything");
        owner.MustChangePassword.Should().BeTrue("the platform operator chose this password and knows it");
    }

    [Fact]
    public async Task Two_companies_cannot_share_a_company_code()
    {
        // The code is half of every login its staff type. Two companies sharing one would make
        // 'ahmed@GULF01' ambiguous, which is the one thing the scheme cannot survive.
        await using var db = CreateContext(null);

        await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        var act = async () => await OnboardAsync(db, "Gulf Trading", "GULF01", "admin");

        await act.Should().ThrowAsync<Exception>();
    }

    // --- Signing in -----------------------------------------------------------------------------

    [Fact]
    public async Task A_user_signs_in_with_username_at_company()
    {
        await using var db = CreateContext(null);
        var created = await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        var result = await LoginAsync(db, "admin@GULF01", Password);

        result.User.UserId.Should().Be(created.OwnerUserId);
        result.User.Username.Should().Be("admin");
        result.User.CompanyId.Should().Be(created.CompanyId);
        result.User.CompanyCode.Should().Be("GULF01", "so the UI can remind them what to type next time");
        result.User.IsOwner.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_same_username_in_two_companies_signs_in_two_different_people()
    {
        // The whole point of scoping the username. Both shops called their manager "admin"; neither
        // knows the other exists; and the two logins must never cross.
        await using var db = CreateContext(null);

        var gulf = await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");
        var sharjah = await OnboardAsync(db, "Sharjah IT", "SHJ01", "admin");

        var a = await LoginAsync(db, "admin@GULF01", Password);
        var b = await LoginAsync(db, "admin@SHJ01", Password);

        a.User.UserId.Should().Be(gulf.OwnerUserId);
        b.User.UserId.Should().Be(sharjah.OwnerUserId);

        a.User.CompanyId.Should().Be(gulf.CompanyId);
        b.User.CompanyId.Should().Be(sharjah.CompanyId);
        (a.User.CompanyId == b.User.CompanyId).Should().BeFalse("they are two different companies");
    }

    [Fact]
    public async Task The_right_username_with_the_wrong_company_is_refused()
    {
        // 'admin' exists, and 'SHJ01' exists — but not together. A caller must not be able to reach
        // one company's user by naming another company's code.
        await using var db = CreateContext(null);

        await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");
        await OnboardAsync(db, "Sharjah IT", "SHJ01", "someoneelse");

        var act = async () => await LoginAsync(db, "admin@SHJ01", Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("admin@NOSUCHCO", Password)]   // the company does not exist
    [InlineData("nobody@GULF01", Password)]    // the user does not exist
    [InlineData("admin@GULF01", "wrong")]      // the password is wrong
    [InlineData("admin", Password)]            // not even the right shape
    public async Task Every_credential_failure_gives_the_same_answer(string login, string password)
    {
        // Told apart, these are a map of the platform: which companies exist, and who works at them.
        // One message, one code path — see LoginCommandHandler.
        await using var db = CreateContext(null);
        await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        var act = async () => await LoginAsync(db, login, password);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("Username or password is incorrect.");
    }

    [Fact]
    public async Task A_failed_login_is_recorded_verbatim_so_probing_is_visible()
    {
        await using var db = CreateContext(null);
        await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        var act = async () => await LoginAsync(db, "root@ADMIN", "hunter2");
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var attempt = await db.LoginHistory.OrderByDescending(h => h.At).FirstAsync();

        // Unparsed and unnormalised: on a failed attempt the whole value of the row is seeing exactly
        // what somebody was probing with.
        attempt.Login.Should().Be("root@ADMIN");
        attempt.Result.Should().Be(LoginResult.BadCredentials);
        attempt.UserId.Should().BeNull();
    }

    [Fact]
    public async Task Nobody_in_a_suspended_company_can_sign_in_not_even_its_owner()
    {
        // This is the enforcement point for a tenant that has stopped paying, and it has to hold for
        // the owner too — otherwise suspension is a suggestion.
        await using var db = CreateContext(null);
        var created = await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        var company = await db.IgnoringTenantFilter<Company>().FirstAsync(c => c.Id == created.CompanyId);
        company.IsActive = false;
        await db.SaveChangesAsync();

        var act = async () => await LoginAsync(db, "admin@GULF01", Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*not active*");
    }

    // --- The platform operator ------------------------------------------------------------------

    [Fact]
    public async Task A_platform_admin_signs_in_with_a_bare_username()
    {
        await using var db = CreateContext(null);

        db.PlatformAdmins.Add(new PlatformAdmin
        {
            Username = "platformadmin",
            FullName = "Platform Administrator",
            PasswordHash = new PasswordHasher().Hash(Password),
            IsActive = true
        });

        await db.SaveChangesAsync();

        var result = await PlatformLoginAsync(db, "platformadmin", Password);

        result.Admin.Username.Should().Be("platformadmin");
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_company_user_cannot_sign_in_at_the_platform_door()
    {
        // The two are different tables and different credentials. A shop's owner is not a diminished
        // platform operator; they are not one at all.
        await using var db = CreateContext(null);
        await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        var act = async () => await PlatformLoginAsync(db, "admin", Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task A_platform_admin_cannot_sign_in_at_a_company_door()
    {
        await using var db = CreateContext(null);
        await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        db.PlatformAdmins.Add(new PlatformAdmin
        {
            Username = "platformadmin",
            FullName = "Platform Administrator",
            PasswordHash = new PasswordHasher().Hash(Password),
            IsActive = true
        });

        await db.SaveChangesAsync();

        var act = async () => await LoginAsync(db, "platformadmin@GULF01", Password);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "a platform admin is not a user of any company, so there is nothing for that login to find");
    }

    [Fact]
    public async Task The_platform_token_carries_no_company_and_the_tenant_token_does()
    {
        // The single most dangerous fact in this design: a null tenant switches the DbContext query
        // filters OFF. So a platform token must never satisfy a tenant endpoint, and the only thing
        // standing there is the company_id claim — checked positively, by the Tenant policy.
        await using var db = CreateContext(null);
        await OnboardAsync(db, "Gulf Computers", "GULF01", "admin");

        db.PlatformAdmins.Add(new PlatformAdmin
        {
            Username = "platformadmin",
            FullName = "Platform Administrator",
            PasswordHash = new PasswordHasher().Hash(Password),
            IsActive = true
        });

        await db.SaveChangesAsync();

        var tenant = await LoginAsync(db, "admin@GULF01", Password);
        var platform = await PlatformLoginAsync(db, "platformadmin", Password);

        ClaimsOf(tenant.AccessToken).Should().ContainKey(TokenService.CompanyIdClaim);
        ClaimsOf(tenant.AccessToken).Should().NotContainKey(TokenService.PlatformAdminClaim);

        ClaimsOf(platform.AccessToken).Should().NotContainKey(
            TokenService.CompanyIdClaim,
            "a platform admin belongs to no company — and the Tenant policy refuses a token without one");

        ClaimsOf(platform.AccessToken).Should().ContainKey(TokenService.PlatformAdminClaim);
    }

    // --- Fixture --------------------------------------------------------------------------------

    private static Dictionary<string, string> ClaimsOf(string jwt) =>
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(jwt)
            .Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value);

    private async Task<CompanyCreatedDto> OnboardAsync(
        ApplicationDbContext db,
        string name,
        string code,
        string ownerUsername)
    {
        var handler = new CreateCompanyCommandHandler(db, new PasswordHasher(), new StubClock());

        return await handler.Handle(
            new CreateCompanyCommand(
                Name: name,
                Code: code,
                OwnerFullName: "The Owner",
                OwnerUsername: ownerUsername,
                OwnerPassword: Password),
            CancellationToken.None);
    }

    private async Task<AuthResult> LoginAsync(ApplicationDbContext db, string login, string password)
    {
        var handler = new LoginCommandHandler(
            db,
            new PasswordHasher(),
            new StubClock(),
            new StubUser(),
            Sessions(db));

        return await handler.Handle(new LoginCommand(login, password), CancellationToken.None);
    }

    private async Task<PlatformAuthResult> PlatformLoginAsync(
        ApplicationDbContext db,
        string username,
        string password)
    {
        var handler = new PlatformLoginCommandHandler(
            db,
            new PasswordHasher(),
            new StubClock(),
            new StubUser(),
            Sessions(db));

        return await handler.Handle(new PlatformLoginCommand(username, password), CancellationToken.None);
    }

    private static AuthSessionFactory Sessions(ApplicationDbContext db) =>
        new(db, Tokens(), new StubSettings(), new StubClock(), new StubUser());

    private static TokenService Tokens()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "tests-only-signing-key-long-enough-for-hmac-sha256",
                ["Jwt:Issuer"] = "TechStorePro",
                ["Jwt:Audience"] = "TechStorePro.Client"
            })
            .Build();

        return new TokenService(configuration, new StubClock());
    }

    private ApplicationDbContext CreateContext(Guid? companyId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro"))
            .Options;

        return new ApplicationDbContext(options, new StubTenant(companyId), new StubUser(), new StubClock());
    }

    private sealed class StubTenant(Guid? companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool HasTenant => CompanyId.HasValue;
    }

    private sealed class StubUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Username => "tests";
        public bool IsAuthenticated => false;
        public bool IsPlatformAdmin => false;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "tests";
    }

    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The real provider resolves per company; a fresh login has none yet, so these are the defaults.</summary>
    private sealed class StubSettings : ISettingsProvider
    {
        public Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Default<T>(key));

        public Task<T> GetAsOfAsync<T>(string key, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(Default<T>(key));

        public Task<T> GetForBranchAsync<T>(
            string key,
            Guid branchId,
            DateTimeOffset? asOf = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Default<T>(key));

        public Task SetAsync(
            string key,
            string value,
            Guid? branchId = null,
            DateTimeOffset? validFrom = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static T Default<T>(string key) =>
            (T)Convert.ChangeType(
                key switch
                {
                    "security.token.access_minutes" => 30,
                    "security.token.refresh_days" => 14,
                    _ => 0
                },
                typeof(T));
    }
}
