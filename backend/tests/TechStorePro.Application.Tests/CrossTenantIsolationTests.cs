using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;
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

        var a = new Company { Name = "Gulf Computers", Code = "GULF01", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        var b = new Company { Name = "Sharjah IT", Code = "SHJ01", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
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
    public async Task Inventory_is_isolated_per_company_too()
    {
        // P3 added the stock ledger, and with it the tables that hold what a shop is actually worth.
        // Every one of them is tenant-scoped, and an entity that forgot ITenantScoped would pass every
        // other test in this suite and leak one company's inventory into another's stock report.
        await using var asA = CreateContext(_companyA);

        var warehouse = new Warehouse
        {
            CompanyId = _companyA,
            BranchId = _branchOfA,
            Name = "A Store",
            Code = "A-STORE"
        };

        var product = new Product
        {
            CompanyId = _companyA,
            ItemCode = "A-INV",
            Sku = "A-INV",
            Name = "A's stock item",
            Unit = "each",
            TrackingMode = TrackingMode.Serial
        };

        asA.Warehouses.Add(warehouse);
        asA.Products.Add(product);
        await asA.SaveChangesAsync();

        asA.StockBalances.Add(new StockBalance
        {
            CompanyId = _companyA,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            Quantity = 10,
            AverageCost = 100m
        });

        asA.StockMovements.Add(new StockMovement
        {
            CompanyId = _companyA,
            WarehouseId = warehouse.Id,
            BranchId = _branchOfA,
            ProductId = product.Id,
            Type = MovementType.Receipt,
            Quantity = 10,
            UnitCost = 100m,
            AverageCostAfter = 100m,
            BalanceAfter = 10,
            ReferenceType = StockReferenceType.GoodsReceipt,
            OccurredAt = DateTimeOffset.UnixEpoch
        });

        var serial = new Serial
        {
            CompanyId = _companyA,
            ProductId = product.Id,
            SerialNumber = "SN-A-001",
            Status = SerialStatus.InStock,
            WarehouseId = warehouse.Id
        };

        asA.Serials.Add(serial);
        await asA.SaveChangesAsync();

        asA.SerialEvents.Add(new SerialEvent
        {
            CompanyId = _companyA,
            SerialId = serial.Id,
            Type = SerialEventType.Received,
            Status = SerialStatus.InStock,
            WarehouseId = warehouse.Id,
            At = DateTimeOffset.UnixEpoch
        });

        asA.StockReservations.Add(new StockReservation
        {
            CompanyId = _companyA,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            Quantity = 2,
            Status = ReservationStatus.Active,
            ReferenceType = StockReferenceType.Invoice,
            ReservedAt = DateTimeOffset.UnixEpoch
        });

        asA.StockAdjustments.Add(new StockAdjustment
        {
            CompanyId = _companyA,
            Number = "ADJ-2026-00001",
            WarehouseId = warehouse.Id,
            BranchId = _branchOfA,
            Reason = AdjustmentReason.Damaged,
            Explanation = "Water damage",
            AdjustedAt = DateTimeOffset.UnixEpoch,
            Lines = [new StockAdjustmentLine { CompanyId = _companyA, ProductId = product.Id, Quantity = -1, UnitCost = 100m }]
        });

        asA.StockCounts.Add(new StockCount
        {
            CompanyId = _companyA,
            Number = "CNT-2026-00001",
            WarehouseId = warehouse.Id,
            BranchId = _branchOfA,
            Status = StockCountStatus.Counting,
            StartedAt = DateTimeOffset.UnixEpoch,
            Lines =
            [
                new StockCountLine
                {
                    CompanyId = _companyA,
                    ProductId = product.Id,
                    SystemQuantity = 10,
                    CountedQuantity = 9,
                    UnitCost = 100m
                }
            ]
        });

        asA.BarcodePrintJobs.Add(new BarcodePrintJob
        {
            CompanyId = _companyA,
            SourceType = BarcodeSource.Product,
            SourceId = product.Id,
            LabelCount = 10,
            PrintedAt = DateTimeOffset.UnixEpoch
        });

        await asA.SaveChangesAsync();

        await using var asB = CreateContext(_companyB);

        (await asB.StockBalances.AnyAsync()).Should().BeFalse("B must not see what A has on its shelves");
        (await asB.StockMovements.AnyAsync()).Should().BeFalse("nor A's ledger — it is A's entire cost base");
        (await asB.Serials.AnyAsync()).Should().BeFalse("nor which machines A holds");
        (await asB.SerialEvents.AnyAsync()).Should().BeFalse("nor their history");
        (await asB.StockReservations.AnyAsync()).Should().BeFalse("nor what A has promised its customers");
        (await asB.StockAdjustments.AnyAsync()).Should().BeFalse("nor what A has written off");
        (await asB.StockAdjustmentLines.AnyAsync()).Should().BeFalse("the lines are scoped too, not just the header");
        (await asB.StockCounts.AnyAsync()).Should().BeFalse("nor A's counts");
        (await asB.StockCountLines.AnyAsync()).Should().BeFalse("nor their lines");
        (await asB.BarcodePrintJobs.AnyAsync()).Should().BeFalse("nor what A has labelled");

        // Knowing the id is not enough. Ids leak through URLs, exports and screenshots; the row behind
        // one must still be unreachable.
        (await asB.StockBalances.FirstOrDefaultAsync(b => b.ProductId == product.Id)).Should().BeNull();
        (await asB.Serials.FirstOrDefaultAsync(s => s.Id == serial.Id)).Should().BeNull();

        // And B may reuse the serial number: a serial identifies one machine within one company's
        // books, and B must not be told "that serial is taken" because an unrelated company used it.
        var warehouseB = new Warehouse
        {
            CompanyId = _companyB,
            BranchId = _branchOfB,
            Name = "B Store",
            Code = "B-STORE"
        };

        var productB = new Product
        {
            CompanyId = _companyB,
            ItemCode = "B-INV",
            Sku = "B-INV",
            Name = "B's stock item",
            Unit = "each",
            TrackingMode = TrackingMode.Serial
        };

        asB.Warehouses.Add(warehouseB);
        asB.Products.Add(productB);
        await asB.SaveChangesAsync();

        asB.Serials.Add(new Serial
        {
            CompanyId = _companyB,
            ProductId = productB.Id,
            SerialNumber = "SN-A-001",
            Status = SerialStatus.InStock,
            WarehouseId = warehouseB.Id
        });

        var act = async () => await asB.SaveChangesAsync();

        await act.Should().NotThrowAsync("serial uniqueness is scoped to the company, not global");
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

        await using var asA = CreateContext(_companyA, userId, "maryam");

        asA.Users.Add(new User
        {
            CompanyId = _companyA,
            Username = "samir",
            FullName = "Samir Khan",
            PasswordHash = "1.210000.SALTSALTSALTSALT==.HASHHASHHASHHASHHASHHASHHASHHASH="
        });

        await asA.SaveChangesAsync();

        var entry = await asA.AuditLogs.FirstAsync(a => a.EntityType == nameof(User));

        // By username, not by email: email is optional and non-unique now, so it could not answer
        // "who did this" — it might be blank, or the same address for two people.
        entry.Username.Should().Be("maryam");
        entry.NewValues.Should().Contain("samir");

        // An audit trail that faithfully records every password hash a user has ever had is a
        // credential history, not an audit log.
        entry.NewValues.Should().NotContain("PasswordHash");
        entry.NewValues.Should().NotContain("1.210000.");
    }

    [Fact]
    public async Task Two_companies_may_each_have_a_user_called_admin()
    {
        // The whole reason a username is scoped to its company. A shop names its manager "admin"
        // without being told that an invisible stranger already took the name — being told would be
        // both a terrible experience and a way to enumerate the platform's tenants.
        await using var asA = CreateContext(_companyA);
        await using var asB = CreateContext(_companyB);

        asA.Users.Add(new User
        {
            CompanyId = _companyA,
            Username = "admin",
            FullName = "A's admin",
            PasswordHash = "x"
        });

        await asA.SaveChangesAsync();

        asB.Users.Add(new User
        {
            CompanyId = _companyB,
            Username = "admin",
            FullName = "B's admin",
            PasswordHash = "x"
        });

        var act = async () => await asB.SaveChangesAsync();

        await act.Should().NotThrowAsync("a username is unique within a company, not across the platform");

        // And they are two different people, each invisible to the other.
        (await asA.Users.CountAsync(u => u.Username == "admin")).Should().Be(1);
        (await asB.Users.CountAsync(u => u.Username == "admin")).Should().Be(1);
    }

    [Fact]
    public async Task The_same_username_twice_in_one_company_is_refused()
    {
        await using var asA = CreateContext(_companyA);

        asA.Users.Add(new User
        {
            CompanyId = _companyA,
            Username = "ahmed",
            FullName = "Ahmed One",
            PasswordHash = "x"
        });

        await asA.SaveChangesAsync();

        asA.Users.Add(new User
        {
            CompanyId = _companyA,
            Username = "ahmed",
            FullName = "Ahmed Two",
            PasswordHash = "x"
        });

        var act = async () => await asA.SaveChangesAsync();

        // Otherwise 'ahmed@GULF01' would identify two people and the login could not resolve either.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Sales_are_isolated_per_company_too()
    {
        // P5 added nine tables, and they hold the most sensitive rows in the system: what a company sells,
        // to whom, at what price, and at what margin. A sales entity that forgot ITenantScoped would pass
        // every other test in this suite and quietly show one shop its competitor's order book.
        await using var asA = CreateContext(_companyA);

        var customer = new Customer { CompanyId = _companyA, Code = "A-CUST", Name = "A's customer" };

        var product = new Product
        {
            CompanyId = _companyA,
            ItemCode = "A-SELL",
            Sku = "A-SELL",
            Name = "A's product",
            Unit = "each",
            TrackingMode = TrackingMode.None,
            SellingPrice = 100m
        };

        var warehouse = new Warehouse
        {
            CompanyId = _companyA,
            BranchId = _branchOfA,
            Name = "A Sales Store",
            Code = "A-SALES"
        };

        asA.Customers.Add(customer);
        asA.Products.Add(product);
        asA.Warehouses.Add(warehouse);
        await asA.SaveChangesAsync();

        var quotation = new Quotation
        {
            CompanyId = _companyA,
            Number = "QT-2026-00001",
            CustomerId = customer.Id,
            BranchId = _branchOfA,
            QuotedAt = DateTimeOffset.UnixEpoch
        };

        var order = new SalesOrder
        {
            CompanyId = _companyA,
            Number = "SO-2026-00001",
            CustomerId = customer.Id,
            BranchId = _branchOfA,
            WarehouseId = warehouse.Id,
            OrderedAt = DateTimeOffset.UnixEpoch
        };

        var delivery = new Delivery
        {
            CompanyId = _companyA,
            Number = "DLV-2026-00001",
            CustomerId = customer.Id,
            BranchId = _branchOfA,
            WarehouseId = warehouse.Id,
            DeliveredAt = DateTimeOffset.UnixEpoch
        };

        var invoice = new SalesInvoice
        {
            CompanyId = _companyA,
            Number = "INV-2026-00001",
            CustomerId = customer.Id,
            BranchId = _branchOfA,
            InvoicedAt = DateTimeOffset.UnixEpoch
        };

        asA.Quotations.Add(quotation);
        asA.SalesOrders.Add(order);
        asA.Deliveries.Add(delivery);
        asA.SalesInvoices.Add(invoice);
        await asA.SaveChangesAsync();

        asA.QuotationLines.Add(new QuotationLine
        {
            CompanyId = _companyA,
            QuotationId = quotation.Id,
            ProductId = product.Id,
            Description = "A's product",
            Quantity = 1,
            UnitPrice = 100m,
            TaxPercent = 5m
        });

        asA.SalesOrderLines.Add(new SalesOrderLine
        {
            CompanyId = _companyA,
            SalesOrderId = order.Id,
            ProductId = product.Id,
            Description = "A's product",
            Quantity = 1,
            UnitPrice = 100m,
            TaxPercent = 5m
        });

        var deliveryLine = new DeliveryLine
        {
            CompanyId = _companyA,
            DeliveryId = delivery.Id,
            ProductId = product.Id,
            Quantity = 1,
            UnitCost = 60m
        };

        asA.DeliveryLines.Add(deliveryLine);
        await asA.SaveChangesAsync();

        asA.SalesInvoiceLines.Add(new SalesInvoiceLine
        {
            CompanyId = _companyA,
            SalesInvoiceId = invoice.Id,
            DeliveryLineId = deliveryLine.Id,
            ProductId = product.Id,
            Description = "A's product",
            Quantity = 1,
            UnitPrice = 100m,
            TaxPercent = 5m,
            UnitCost = 60m
        });

        await asA.SaveChangesAsync();

        // B holds every one of A's ids, and still sees nothing.
        await using var asB = CreateContext(_companyB);

        (await asB.Quotations.CountAsync()).Should().Be(0);
        (await asB.QuotationLines.CountAsync()).Should().Be(0);
        (await asB.SalesOrders.CountAsync()).Should().Be(0);
        (await asB.SalesOrderLines.CountAsync()).Should().Be(0);
        (await asB.Deliveries.CountAsync()).Should().Be(0);
        (await asB.DeliveryLines.CountAsync()).Should().Be(0);
        (await asB.DeliverySerials.CountAsync()).Should().Be(0);
        (await asB.SalesInvoices.CountAsync()).Should().Be(0);
        (await asB.SalesInvoiceLines.CountAsync()).Should().Be(0);

        // Not even by asking for the exact row — the margin on A's sale is A's business alone.
        (await asB.SalesInvoices.FirstOrDefaultAsync(i => i.Id == invoice.Id)).Should().BeNull();
        (await asB.SalesOrders.FirstOrDefaultAsync(o => o.Id == order.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Two_companies_may_each_raise_invoice_number_one()
    {
        // Document numbers are per (company, branch, type, year). Global uniqueness would mean one shop's
        // sales volume dictated another's invoice numbers — and an auditor asking why INV-2026-00002 does
        // not exist would get no answer anyone could give.
        await using var asA = CreateContext(_companyA);
        await using var asB = CreateContext(_companyB);

        var customerA = new Customer { CompanyId = _companyA, Code = "A-C", Name = "A's customer" };
        var customerB = new Customer { CompanyId = _companyB, Code = "B-C", Name = "B's customer" };

        asA.Customers.Add(customerA);
        asB.Customers.Add(customerB);
        await asA.SaveChangesAsync();
        await asB.SaveChangesAsync();

        asA.SalesInvoices.Add(new SalesInvoice
        {
            CompanyId = _companyA,
            Number = "INV-2026-00001",
            CustomerId = customerA.Id,
            BranchId = _branchOfA,
            InvoicedAt = DateTimeOffset.UnixEpoch
        });

        await asA.SaveChangesAsync();

        asB.SalesInvoices.Add(new SalesInvoice
        {
            CompanyId = _companyB,
            Number = "INV-2026-00001",
            CustomerId = customerB.Id,
            BranchId = _branchOfB,
            InvoicedAt = DateTimeOffset.UnixEpoch
        });

        var act = async () => await asB.SaveChangesAsync();

        await act.Should().NotThrowAsync("an invoice number is unique within a company, not across the platform");
    }

    private ApplicationDbContext CreateContext(
        Guid? companyId,
        Guid? userId = null,
        string? username = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro"))
            .Options;

        return new ApplicationDbContext(
            options,
            new StubTenant(companyId),
            new StubUser(userId, username),
            new StubClock());
    }

    private sealed class StubTenant(Guid? companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool HasTenant => CompanyId.HasValue;
    }

    private sealed class StubUser(Guid? userId, string? username) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? Username { get; } = username;
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsPlatformAdmin => false;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "tests";
    }

    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
    }
}
