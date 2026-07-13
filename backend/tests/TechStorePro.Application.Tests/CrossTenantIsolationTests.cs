using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Identity;
using TechStorePro.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// <b>This is the test that keeps the product alive.</b>
///
/// TechStorePro is one database shared by every company (database-design.md §1). The only thing
/// standing between company A and company B's stock, customers and invoices is the global query
/// filter in <see cref="ApplicationDbContext"/>. If it ever silently stops applying — an
/// <c>IgnoreQueryFilters</c> added for convenience, a raw SQL query, a new entity that forgets
/// <c>ITenantScoped</c> — the product leaks one customer's business to another, and no amount of
/// later care undoes that.
///
/// So the filter is not trusted; it is proven, against a real PostgreSQL, on every build.
/// Development-plan.md makes this a gate on P2: no business module ships until it passes.
/// </summary>
public class CrossTenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("techstorepro_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private Guid _companyA;
    private Guid _companyB;
    private Guid _branchOfA;
    private Guid _branchOfB;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Seed both companies with no tenant in scope, which is the one context allowed to see
        // across companies (migrations and platform admin).
        await using var seed = CreateContext(companyId: null);
        await seed.Database.MigrateAsync();

        var a = new Company { Name = "Gulf Computers", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        var b = new Company { Name = "Sharjah IT", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        seed.Companies.AddRange(a, b);

        var branchA = new Branch { CompanyId = a.Id, Name = "A Main", Code = "AMAIN", IsDefault = true };
        var branchB = new Branch { CompanyId = b.Id, Name = "B Main", Code = "BMAIN", IsDefault = true };
        seed.Branches.AddRange(branchA, branchB);

        await seed.SaveChangesAsync();

        _companyA = a.Id;
        _companyB = b.Id;
        _branchOfA = branchA.Id;
        _branchOfB = branchB.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_company_sees_only_its_own_rows()
    {
        await using var asA = CreateContext(_companyA);

        var branches = await asA.Branches.ToListAsync();

        branches.Should().ContainSingle();
        branches[0].Id.Should().Be(_branchOfA);
    }

    [Fact]
    public async Task A_company_cannot_fetch_another_companys_row_even_with_its_exact_id()
    {
        await using var asA = CreateContext(_companyA);

        // The attack this defends against is not guessing an id — ids leak, through URLs, exports,
        // screenshots. It is that knowing an id must not be enough to read the row behind it.
        var stolen = await asA.Branches.FirstOrDefaultAsync(b => b.Id == _branchOfB);

        stolen.Should().BeNull("company A must not be able to read company B's branch by id");
    }

    [Fact]
    public async Task A_company_cannot_update_another_companys_row()
    {
        await using var asA = CreateContext(_companyA);

        var target = await asA.Branches.FirstOrDefaultAsync(b => b.Id == _branchOfB);

        // It is invisible, so it cannot even be loaded to be modified. That is the mechanism: the
        // write is not blocked by a check that someone might forget — the row simply is not there.
        target.Should().BeNull();

        await using var asB = CreateContext(_companyB);
        var stillIntact = await asB.Branches.FirstAsync(b => b.Id == _branchOfB);
        stillIntact.Name.Should().Be("B Main");
    }

    [Fact]
    public async Task A_new_row_is_stamped_with_the_current_company_not_the_one_supplied()
    {
        await using var asA = CreateContext(_companyA);

        // A hostile or buggy caller sets someone else's company id on an insert.
        asA.Branches.Add(new Branch { Name = "Planted", Code = "PLANT", CompanyId = Guid.Empty });
        await asA.SaveChangesAsync();

        var planted = await asA.Branches.FirstAsync(b => b.Code == "PLANT");
        planted.CompanyId.Should().Be(_companyA);

        await using var asB = CreateContext(_companyB);
        (await asB.Branches.AnyAsync(b => b.Code == "PLANT")).Should().BeFalse(
            "a row created by company A must never appear in company B");
    }

    [Fact]
    public async Task Soft_deleted_rows_are_invisible_to_normal_queries()
    {
        await using var asA = CreateContext(_companyA);

        var branch = new Branch { Name = "Temp", Code = "TEMP", CompanyId = _companyA };
        asA.Branches.Add(branch);
        await asA.SaveChangesAsync();

        branch.DeletedReason = "closed";
        asA.Branches.Remove(branch);
        await asA.SaveChangesAsync();

        (await asA.Branches.AnyAsync(b => b.Code == "TEMP")).Should().BeFalse();

        // Retired, not removed: the row and its reason are still there for the auditor.
        var retired = await asA.Branches.IgnoreQueryFilters().FirstAsync(b => b.Code == "TEMP");
        retired.IsDeleted.Should().BeTrue();
        retired.DeletedReason.Should().Be("closed");
        retired.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Master_data_is_isolated_per_company_too()
    {
        // P2 added products, customers and suppliers. Every one of them is a table company A must
        // never see into. This test grows with the schema on purpose: an entity that forgets
        // ITenantScoped would pass every other test in the suite and leak in production.
        await using var asA = CreateContext(_companyA);

        asA.Products.Add(new Product
        {
            CompanyId = _companyA,
            ItemCode = "A-LAPTOP",
            Sku = "A-LAPTOP",
            Name = "A's laptop",
            Unit = "each"
        });

        asA.Customers.Add(new Customer { CompanyId = _companyA, Code = "A-CUST", Name = "A's customer" });
        asA.Suppliers.Add(new Supplier { CompanyId = _companyA, Code = "A-SUPP", Name = "A's supplier" });

        await asA.SaveChangesAsync();

        await using var asB = CreateContext(_companyB);

        (await asB.Products.AnyAsync()).Should().BeFalse("company B must not see company A's products");
        (await asB.Customers.AnyAsync()).Should().BeFalse("company B must not see company A's customers");
        (await asB.Suppliers.AnyAsync()).Should().BeFalse("company B must not see company A's suppliers");

        // And the same SKU is free for B to use — the uniqueness is per company, not global. A shop
        // must not be told "that SKU is taken" because an unrelated company already used it.
        asB.Products.Add(new Product
        {
            CompanyId = _companyB,
            ItemCode = "A-LAPTOP",
            Sku = "A-LAPTOP",
            Name = "B's laptop",
            Unit = "each"
        });

        var act = async () => await asB.SaveChangesAsync();

        await act.Should().NotThrowAsync("SKU uniqueness is scoped to the company");
    }

    [Fact]
    public async Task A_retired_product_frees_its_sku_for_reuse()
    {
        await using var asA = CreateContext(_companyA);

        var mistyped = new Product
        {
            CompanyId = _companyA,
            ItemCode = "SKU-1",
            Sku = "SKU-1",
            Name = "Typo",
            Unit = "each"
        };

        asA.Products.Add(mistyped);
        await asA.SaveChangesAsync();

        mistyped.DeletedReason = "Entered by mistake";
        asA.Products.Remove(mistyped);
        await asA.SaveChangesAsync();

        // The unique index is filtered on is_deleted. Without that filter, a shop that mistypes a SKU
        // could never use the correct one — the retired row would hold the code hostage forever.
        asA.Products.Add(new Product
        {
            CompanyId = _companyA,
            ItemCode = "SKU-1",
            Sku = "SKU-1",
            Name = "Correct product",
            Unit = "each"
        });

        var act = async () => await asA.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_audit_log_records_who_changed_what_but_never_the_password_hash()
    {
        var userId = Guid.NewGuid();

        await using var asA = CreateContext(_companyA, userId, "maryam@gulfcomputers.ae");

        asA.Users.Add(new User
        {
            Email = "samir@gulfcomputers.ae",
            FullName = "Samir Khan",
            PasswordHash = "1.210000.SALTSALTSALTSALT==.HASHHASHHASHHASHHASHHASHHASHHASH="
        });

        await asA.SaveChangesAsync();

        var entry = await asA.AuditLogs.FirstAsync(a => a.EntityType == nameof(User));

        entry.UserEmail.Should().Be("maryam@gulfcomputers.ae");
        entry.NewValues.Should().Contain("samir@gulfcomputers.ae");

        // An audit trail that faithfully records every password hash a user has ever had is a
        // credential history, not an audit log.
        entry.NewValues.Should().NotContain("PasswordHash");
        entry.NewValues.Should().NotContain("1.210000.");
    }

    private ApplicationDbContext CreateContext(
        Guid? companyId,
        Guid? userId = null,
        string? email = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro"))
            .Options;

        return new ApplicationDbContext(
            options,
            new StubTenant(companyId),
            new StubUser(userId, email),
            new StubClock());
    }

    private sealed class StubTenant(Guid? companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool HasTenant => CompanyId.HasValue;
    }

    private sealed class StubUser(Guid? userId, string? email) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? Email { get; } = email;
        public bool IsAuthenticated => UserId.HasValue;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "tests";
    }

    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
    }
}
