using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Adjustments;
using TechStorePro.Application.Inventory.Queries;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Inventory;
using TechStorePro.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// The stock ledger against a real PostgreSQL.
///
/// <b>These cannot be unit tests and pretending otherwise would be the whole bug.</b> Everything the
/// ledger promises — the movement and the balance in one transaction, the balance row locked before it
/// is read, the average recomputed inside that lock, the upsert that makes <c>FOR UPDATE</c> have a row
/// to take hold of — is a promise about what the *database* does. An in-memory provider has no
/// <c>FOR UPDATE</c>, no <c>ON CONFLICT</c> and no transactions worth the name, so a suite built on one
/// would pass while the real thing oversold the last laptop in the shop.
///
/// The domain arithmetic is tested separately and cheaply in <c>Domain.Tests/Inventory</c>. What is
/// tested here is only what needs a database to be true.
/// </summary>
public class StockLedgerTests : IAsyncLifetime
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

    /// <summary>Branch-owned by A's branch, so A may both receive into and issue from it.</summary>
    private Guid _warehouseOfA;

    /// <summary>Company-shared, with no branch granted access. Nothing may move through it.</summary>
    private Guid _lockedWarehouseOfA;

    private Guid _warehouseOfB;

    private Guid _laptop;   // serial-tracked
    private Guid _cable;    // not tracked
    private Guid _labour;   // a service: has no stock at all
    private Guid _productOfB;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var seed = CreateContext(companyId: null);
        await seed.Database.MigrateAsync();

        var a = new Company { Name = "Gulf Computers", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        var b = new Company { Name = "Sharjah IT", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        seed.Companies.AddRange(a, b);

        var branchA = new Branch { CompanyId = a.Id, Name = "A Main", Code = "AMAIN", IsDefault = true };
        var branchB = new Branch { CompanyId = b.Id, Name = "B Main", Code = "BMAIN", IsDefault = true };
        seed.Branches.AddRange(branchA, branchB);

        var warehouseA = new Warehouse
        {
            CompanyId = a.Id,
            BranchId = branchA.Id,
            Name = "A Store",
            Code = "A-STORE"
        };

        var lockedA = new Warehouse
        {
            CompanyId = a.Id,
            BranchId = null,          // shared…
            Name = "A Bonded",
            Code = "A-BONDED"         // …but with an empty access list, so no branch may use it.
        };

        var warehouseB = new Warehouse
        {
            CompanyId = b.Id,
            BranchId = branchB.Id,
            Name = "B Store",
            Code = "B-STORE"
        };

        seed.Warehouses.AddRange(warehouseA, lockedA, warehouseB);

        var laptop = Product(a.Id, "LAPTOP", "Laptop", TrackingMode.Serial);
        var cable = Product(a.Id, "CABLE", "HDMI cable", TrackingMode.None);
        var labour = Product(a.Id, "LABOUR", "Bench labour", TrackingMode.None, ProductKind.Service);
        var productB = Product(b.Id, "B-LAPTOP", "B's laptop", TrackingMode.None);

        seed.Products.AddRange(laptop, cable, labour, productB);

        await seed.SaveChangesAsync();

        _companyA = a.Id;
        _companyB = b.Id;
        _branchOfA = branchA.Id;
        _branchOfB = branchB.Id;
        _warehouseOfA = warehouseA.Id;
        _lockedWarehouseOfA = lockedA.Id;
        _warehouseOfB = warehouseB.Id;
        _laptop = laptop.Id;
        _cable = cable.Id;
        _labour = labour.Id;
        _productOfB = productB.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // --- The movement and the balance are one write ---------------------------------------------

    [Fact]
    public async Task A_receipt_appends_a_movement_and_raises_the_balance_in_one_transaction()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, quantity: 10, unitCost: 100m));

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.Quantity.Should().Be(10);
        balance.AverageCost.Should().Be(100m);

        var movement = await db.StockMovements.SingleAsync(m => m.ProductId == _cable);
        movement.Quantity.Should().Be(10, "an inbound movement is stored with a positive sign");
        movement.BalanceAfter.Should().Be(10);
        movement.AverageCostAfter.Should().Be(100m);
    }

    [Fact]
    public async Task A_movement_outside_a_transaction_is_refused()
    {
        // Without an ambient transaction the FOR UPDATE lock would be released the moment the SELECT
        // finished, and two concurrent sales of the last unit would both pass their availability check.
        // The ledger refuses rather than overselling and finding out from the customer.
        await using var db = CreateContext(_companyA);

        var ledger = Ledger(db);

        var act = async () => await ledger.PostAsync(Receipt(_cable, 1, 100m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*must be posted inside a transaction*");
    }

    [Fact]
    public async Task A_rolled_back_transaction_leaves_no_stock_and_no_movement()
    {
        // The ledger is never the only thing a business operation does. If the document that moved the
        // stock rolls back, the stock must roll back with it.
        await using var db = CreateContext(_companyA);

        await using (var transaction = await db.BeginTransactionAsync())
        {
            await Ledger(db).PostAsync(Receipt(_cable, 10, 100m));
            await transaction.RollbackAsync();
        }

        await using var fresh = CreateContext(_companyA);

        (await fresh.StockMovements.AnyAsync(m => m.ProductId == _cable)).Should().BeFalse();
        (await fresh.StockBalances.AnyAsync(b => b.ProductId == _cable && b.Quantity != 0)).Should().BeFalse();
    }

    // --- The money ------------------------------------------------------------------------------

    [Fact]
    public async Task Two_receipts_at_different_prices_produce_a_weighted_average()
    {
        // 10 @ 100 then 10 @ 200 → 3,000 over 20 units → 150. The number a shopkeeper computes on paper,
        // recomputed inside the row lock so a concurrent sale cannot interleave into a cost that never
        // existed.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 10, 100m));
        await PostAsync(db, Receipt(_cable, 10, 200m));

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);

        balance.Quantity.Should().Be(20);
        balance.AverageCost.Should().Be(150m);
        balance.TotalValue.Should().Be(3_000m);
    }

    [Fact]
    public async Task A_sale_books_cogs_at_the_average_and_leaves_the_average_alone()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 10, 100m));
        await PostAsync(db, Receipt(_cable, 10, 200m));

        var result = await PostAsync(db, Sale(_cable, quantity: 5));

        // What the sale booked as cost of goods sold. P5 snapshots exactly this onto the invoice line;
        // recomputing it later would use an average that has since moved.
        result.UnitCost.Should().Be(150m);

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.Quantity.Should().Be(15);
        balance.AverageCost.Should().Be(150m, "issuing stock at the average leaves the average where it was");

        var sale = await db.StockMovements.SingleAsync(m => m.Type == MovementType.Sale);
        sale.Quantity.Should().Be(-5, "an outbound movement is stored with a negative sign");
    }

    // --- Overselling ----------------------------------------------------------------------------

    [Fact]
    public async Task Selling_more_than_is_on_the_shelf_is_refused()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 3, 100m));

        var act = async () => await PostAsync(db, Sale(_cable, quantity: 4));

        (await act.Should().ThrowAsync<InsufficientStockException>())
            .Which.Available.Should().Be(3);
    }

    [Fact]
    public async Task Reserved_stock_cannot_be_sold_to_anybody_else()
    {
        // Requirements §20, and the reason the module exists: five on the shelf, four promised, and the
        // fifth is the only one still for sale.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 5, 100m));
        await ReserveAsync(db, _cable, quantity: 4);

        (await Ledger(db).AvailableAsync(_warehouseOfA, _cable)).Should().Be(1);

        var act = async () => await PostAsync(db, Sale(_cable, quantity: 2));

        (await act.Should().ThrowAsync<InsufficientStockException>())
            .Which.Available.Should().Be(1);
    }

    [Fact]
    public async Task The_last_unit_cannot_be_promised_twice()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 1, 100m));
        await ReserveAsync(db, _cable, quantity: 1);

        var act = async () => await ReserveAsync(db, _cable, quantity: 1);

        await act.Should().ThrowAsync<InsufficientStockException>();
    }

    [Fact]
    public async Task A_delivery_consumes_the_reservation_that_promised_it()
    {
        // Without this, delivering the four units you reserved would fail its own availability check:
        // the reservation would be competing with the sale that made it.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 5, 100m));
        var reservation = await ReserveAsync(db, _cable, quantity: 4);

        await PostAsync(db, Sale(_cable, quantity: 4) with { ReservationId = reservation.Id });

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);

        balance.Quantity.Should().Be(1);
        balance.ReservedQuantity.Should().Be(0,
            "the units it was guarding have physically left; still reserving them would hold stock that is gone");

        var consumed = await db.StockReservations.SingleAsync(r => r.Id == reservation.Id);
        consumed.Status.Should().Be(ReservationStatus.Fulfilled);
    }

    [Fact]
    public async Task Releasing_a_reservation_puts_the_stock_back_on_sale()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 5, 100m));
        var reservation = await ReserveAsync(db, _cable, quantity: 5);

        (await Ledger(db).AvailableAsync(_warehouseOfA, _cable)).Should().Be(0);

        await using (var transaction = await db.BeginTransactionAsync())
        {
            await Ledger(db).ReleaseAsync(reservation.Id);
            await transaction.CommitAsync();
        }

        (await Ledger(db).AvailableAsync(_warehouseOfA, _cable)).Should().Be(5);
    }

    // --- Serials --------------------------------------------------------------------------------

    [Fact]
    public async Task Receiving_a_serial_creates_the_unit_and_puts_it_in_stock()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_laptop, 1, 3_000m) with { SerialNumbers = ["SN-001"] });

        var serial = await db.Serials.SingleAsync();

        serial.SerialNumber.Should().Be("SN-001");
        serial.Status.Should().Be(SerialStatus.InStock);
        serial.WarehouseId.Should().Be(_warehouseOfA);
        serial.PurchaseCost.Should().Be(3_000m);

        var movement = await db.StockMovements.SingleAsync();
        movement.SerialId.Should().Be(serial.Id, "a serial-tracked unit moves by exactly one movement");
    }

    [Fact]
    public async Task The_same_laptop_cannot_be_sold_twice()
    {
        // The single most important guarantee in the module. The quantity check alone would not catch
        // this — two sales of one unit would each decrement a balance that still looked plausible.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_laptop, 1, 3_000m) with { SerialNumbers = ["SN-001"] });
        await PostAsync(db, Sale(_laptop, 1) with { SerialNumbers = ["SN-001"] });

        var act = async () => await PostAsync(db, Sale(_laptop, 1) with { SerialNumbers = ["SN-001"] });

        await act.Should().ThrowAsync<DomainException>();

        var serial = await db.Serials.SingleAsync();
        serial.Status.Should().Be(SerialStatus.Sold);
        serial.WarehouseId.Should().BeNull("a sold unit is in a customer's hands, not in a warehouse");
    }

    [Fact]
    public async Task A_serial_tracked_product_needs_one_serial_per_unit()
    {
        await using var db = CreateContext(_companyA);

        var act = async () => await PostAsync(
            db, Receipt(_laptop, quantity: 2, unitCost: 3_000m) with { SerialNumbers = ["SN-001"] });

        await act.Should().ThrowAsync<DomainException>().WithMessage("*2 units need 2 serial numbers*");
    }

    [Fact]
    public async Task A_serial_cannot_be_issued_from_a_warehouse_it_is_not_in()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_laptop, 1, 3_000m) with { SerialNumbers = ["SN-001"] });

        // Ship it out, so it belongs to no warehouse at all, then try to sell it from where it was.
        await PostAsync(db, Posting(_laptop, MovementType.TransferOut, 1) with { SerialNumbers = ["SN-001"] });

        var act = async () => await PostAsync(db, Sale(_laptop, 1) with { SerialNumbers = ["SN-001"] });

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Every_serial_movement_leaves_a_history_entry()
    {
        // This is what makes a warranty claim answerable two years later.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_laptop, 1, 3_000m) with { SerialNumbers = ["SN-001"] });
        await PostAsync(db, Sale(_laptop, 1) with { SerialNumbers = ["SN-001"] });

        var events = await db.SerialEvents.OrderBy(e => e.At).ToListAsync();

        events.Select(e => e.Type).Should().Equal(SerialEventType.Received, SerialEventType.Sold);
    }

    // --- What may not move at all ---------------------------------------------------------------

    [Fact]
    public async Task A_service_has_no_stock()
    {
        await using var db = CreateContext(_companyA);

        var act = async () => await PostAsync(db, Receipt(_labour, 1, 100m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*is a service and has no stock*");
    }

    [Fact]
    public async Task A_branch_may_not_issue_from_a_warehouse_it_has_no_access_to()
    {
        // "Shared" must never silently mean "any branch may drain it" (requirements §45 D2).
        await using var db = CreateContext(_companyA);

        var act = async () => await PostAsync(
            db, Receipt(_cable, 1, 100m) with { WarehouseId = _lockedWarehouseOfA });

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*may not receive into*");
    }

    // --- The cache must be able to prove itself -------------------------------------------------

    [Fact]
    public async Task The_balances_agree_with_the_ledger_after_a_day_of_trading()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 10, 100m));
        await PostAsync(db, Receipt(_cable, 10, 200m));
        await PostAsync(db, Sale(_cable, 5));
        await PostAsync(db, Posting(_cable, MovementType.AdjustmentOut, 2));
        await PostAsync(db, Receipt(_laptop, 1, 3_000m) with { SerialNumbers = ["SN-001"] });

        var audit = await new BalanceAuditor(db).AuditAsync();

        audit.Agrees.Should().BeTrue("stock_balances is a cache of stock_movements and must equal it");
        audit.Discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task The_audit_catches_a_balance_that_was_written_behind_the_ledgers_back()
    {
        // The audit is worthless unless it fails when it should. This simulates the exact disaster it
        // exists to catch: something updated stock_balances without appending a movement — a raw SQL
        // fix, a well-meaning script, a handler that bypassed IStockLedger.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 10, 100m));

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.Quantity = 999;
        await db.SaveChangesAsync();

        var audit = await new BalanceAuditor(db).AuditAsync();

        audit.Agrees.Should().BeFalse();

        var discrepancy = audit.Discrepancies.Should().ContainSingle().Which;
        discrepancy.Kind.Should().Be(DiscrepancyKind.Quantity);
        discrepancy.CachedQuantity.Should().Be(999);
        discrepancy.LedgerQuantity.Should().Be(10);
        discrepancy.QuantityDifference.Should().Be(989);
    }

    [Fact]
    public async Task The_audit_catches_a_corrupted_average_cost_even_when_the_quantity_is_right()
    {
        // The nastier failure, and the one a quantity-only audit misses entirely: the units are all
        // present and correct, so nothing looks broken — while every sale from this warehouse quietly
        // books the wrong COGS into the P&L. "Agree to the cent" is the requirement, not "agree to the
        // unit".
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 10, 100m));

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.AverageCost = 250m;
        await db.SaveChangesAsync();

        var audit = await new BalanceAuditor(db).AuditAsync();

        audit.Agrees.Should().BeFalse("the quantity agrees but the money does not");

        var discrepancy = audit.Discrepancies.Should().ContainSingle().Which;
        discrepancy.Kind.Should().Be(DiscrepancyKind.Cost);
        discrepancy.CachedAverageCost.Should().Be(250m);
        discrepancy.LedgerAverageCost.Should().Be(100m);
        discrepancy.ValueDifference.Should().Be(1_500m, "10 units overvalued by 150 each");
    }

    [Fact]
    public async Task The_audit_still_catches_a_corrupted_balance_that_has_since_been_traded_on()
    {
        // The subtle one, and the reason the audit recomputes value from the movements rather than
        // comparing the balance to StockMovement.AverageCostAfter.
        //
        // AverageCostAfter is written *from* the balance. So if something corrupts the balance and a
        // perfectly ordinary movement is then posted, that movement faithfully records the corrupted
        // average as though it were correct — and an audit that trusted it would compare the cache
        // against a copy of itself and report all clear. The corruption would be laundered by the very
        // next sale.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, Receipt(_cable, 10, 100m));

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.AverageCost = 250m;
        await db.SaveChangesAsync();

        // Trade on the corrupted average: this sale is issued at 250 and stamps 250 into the ledger.
        await PostAsync(db, Sale(_cable, 1));

        var audit = await new BalanceAuditor(db).AuditAsync();

        audit.Agrees.Should().BeFalse(
            "a corrupted average that has been traded on must not be laundered by the trade");

        audit.Discrepancies.Should().ContainSingle().Which.Kind.Should().Be(DiscrepancyKind.Cost);
    }

    // --- The document, not just the ledger ------------------------------------------------------

    [Fact]
    public async Task An_adjustment_posts_the_ledger_and_persists_its_own_lines()
    {
        // This runs the real CreateAdjustmentCommandHandler, not a hand-built document, and that is the
        // entire point of it.
        //
        // Every other test in this file builds its documents with DbSet.Add(parent), which cascades the
        // Added state to the children. The handler cannot: IStockLedger.PostAsync saves inside the
        // handler's transaction (it must — the movement and the balance are one write), so by the time
        // the adjustment's lines are attached, the adjustment itself is already Unchanged. A child found
        // on an Unchanged parent with an id already set — BaseEntity assigns Guid.NewGuid() — is taken by
        // EF for an existing row, and it emits an UPDATE against a line that was never inserted. Zero
        // rows match and the whole adjustment dies with DbUpdateConcurrencyException.
        //
        // Which is to say: the endpoint was a 500 on every call, and a suite that only ever used
        // DbSet.Add could never have seen it.
        await using var db = CreateContext(_companyA);

        var handler = new CreateAdjustmentCommandHandler(
            db,
            Ledger(db),
            new DocumentNumberGenerator(new ApplicationDbContextAccessor(db), db, new StubTenant(_companyA), new StubClock()),
            new StubClock());

        var adjustmentId = await handler.Handle(
            new CreateAdjustmentCommand(
                WarehouseId: _warehouseOfA,
                BranchId: _branchOfA,
                Reason: AdjustmentReason.OpeningStock,
                Explanation: "Opening stock",
                Lines: [new CreateAdjustmentLine(ProductId: _cable, Quantity: 10, UnitCost: 100m)]),
            CancellationToken.None);

        await using var fresh = CreateContext(_companyA);

        var adjustment = await fresh.StockAdjustments
            .Include(a => a.Lines)
            .FirstAsync(a => a.Id == adjustmentId);

        adjustment.Number.Should().NotBeNullOrWhiteSpace("the document takes a gapless number");
        adjustment.Lines.Should().ContainSingle("the line must actually be inserted, not UPDATEd into thin air");
        adjustment.Lines.Single().Quantity.Should().Be(10);

        // And the ledger moved with it, in the same transaction.
        var balance = await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.Quantity.Should().Be(10);
        balance.AverageCost.Should().Be(100m);

        (await new BalanceAuditor(fresh).AuditAsync()).Agrees.Should().BeTrue();
    }

    // --- Tenancy --------------------------------------------------------------------------------

    [Fact]
    public async Task One_companys_stock_is_invisible_to_another()
    {
        await using var asA = CreateContext(_companyA);
        await PostAsync(asA, Receipt(_cable, 10, 100m));

        await using var asB = CreateContext(_companyB);

        (await asB.StockBalances.AnyAsync()).Should().BeFalse("company B must not see company A's stock");
        (await asB.StockMovements.AnyAsync()).Should().BeFalse("nor its ledger");

        // And B's own audit must not be polluted by A's rows: a reconciliation that swept in another
        // tenant's balances would report discrepancies that are not B's to fix.
        var audit = await new BalanceAuditor(asB).AuditAsync();
        audit.BalancesChecked.Should().Be(0);
        audit.Agrees.Should().BeTrue();
    }

    [Fact]
    public async Task A_company_cannot_move_stock_it_can_see_the_id_of()
    {
        // Ids leak — through URLs, exports, screenshots. Knowing B's product id must not be enough to
        // post a movement against it.
        await using var asA = CreateContext(_companyA);

        var act = async () => await PostAsync(asA, Receipt(_productOfB, 5, 100m));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task A_company_cannot_move_stock_into_another_companys_warehouse()
    {
        await using var asA = CreateContext(_companyA);

        var act = async () => await PostAsync(
            asA, Receipt(_cable, 5, 100m) with { WarehouseId = _warehouseOfB });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // --- Fixture --------------------------------------------------------------------------------

    private static Product Product(
        Guid companyId,
        string code,
        string name,
        TrackingMode tracking,
        ProductKind kind = ProductKind.Product) =>
        new()
        {
            CompanyId = companyId,
            ItemCode = code,
            Sku = code,
            Name = name,
            Unit = "each",
            Kind = kind,
            TrackingMode = tracking
        };

    private StockPosting Posting(Guid productId, MovementType type, decimal quantity, decimal? unitCost = null) =>
        new(
            WarehouseId: _warehouseOfA,
            BranchId: _branchOfA,
            ProductId: productId,
            Type: type,
            Quantity: quantity,
            ReferenceType: StockReferenceType.StockAdjustment,
            UnitCost: unitCost);

    private StockPosting Receipt(Guid productId, decimal quantity, decimal unitCost) =>
        Posting(productId, MovementType.Receipt, quantity, unitCost) with
        {
            ReferenceType = StockReferenceType.GoodsReceipt
        };

    private StockPosting Sale(Guid productId, decimal quantity) =>
        Posting(productId, MovementType.Sale, quantity) with
        {
            ReferenceType = StockReferenceType.Invoice
        };

    /// <summary>Posts inside a transaction, which is the only way the ledger will run at all.</summary>
    private async Task<StockPostingResult> PostAsync(ApplicationDbContext db, StockPosting posting)
    {
        await using var transaction = await db.BeginTransactionAsync();

        var result = await Ledger(db).PostAsync(posting);

        await transaction.CommitAsync();

        return result;
    }

    private async Task<StockReservation> ReserveAsync(ApplicationDbContext db, Guid productId, decimal quantity)
    {
        await using var transaction = await db.BeginTransactionAsync();

        var reservation = await Ledger(db).ReserveAsync(
            _warehouseOfA,
            productId,
            quantity,
            StockReferenceType.Invoice,
            referenceId: null,
            referenceNumber: "QT-2026-00001",
            expiresAt: null);

        await transaction.CommitAsync();

        return reservation;
    }

    /// <summary>
    /// The ledger reads its company from <see cref="ITenantContext"/>, exactly as it does in production
    /// — so the tenant handed to it here is the same one the DbContext is filtering on. Every posting in
    /// this suite is company A's; the tenancy tests below prove what happens when it tries to reach
    /// beyond that.
    /// </summary>
    private StockLedger Ledger(ApplicationDbContext db) =>
        new(
            new ApplicationDbContextAccessor(db),
            db,
            new StubTenant(_companyA),
            new StubClock(),
            new WeightedAverageCosting());

    private ApplicationDbContext CreateContext(Guid? companyId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro"))
            .Options;

        return new ApplicationDbContext(
            options,
            new StubTenant(companyId),
            new StubUser(),
            new StubClock());
    }

    private sealed class StubTenant(Guid? companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool HasTenant => CompanyId.HasValue;
    }

    private sealed class StubUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Email => "tests@techstorepro.ae";
        public bool IsAuthenticated => true;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "tests";
    }

    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
    }
}
