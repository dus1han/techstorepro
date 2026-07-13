using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Purchasing.GoodsReceipts;
using TechStorePro.Application.Purchasing.Imports;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Purchasing;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Inventory;
using TechStorePro.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// Purchasing and imports, against a real PostgreSQL.
///
/// <b>The landed-cost path is the reason this file exists.</b> Costing is weighted average (§45 D1), so
/// the cost this module hands the ledger does not merely price one container — it feeds the moving
/// average and spreads to every existing unit of the product, where it never washes out. The arithmetic
/// is unit-tested in <c>Domain.Tests/Purchasing</c>; what is tested here is the part that needs a
/// database to be true: that the money actually reaches the balance, in the same transaction, and that
/// the ledger can still prove itself afterwards.
/// </summary>
public class PurchasingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("techstorepro_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private Guid _companyA;
    private Guid _branchOfA;
    private Guid _warehouseOfA;
    private Guid _supplier;

    private Guid _laptop;   // serial-tracked, 1,000 a unit
    private Guid _cable;    // untracked, 50 a unit

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var seed = CreateContext(null);
        await seed.Database.MigrateAsync();

        var company = new Company { Name = "Gulf Computers", Code = "GULF01", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        seed.Companies.Add(company);

        var branch = new Branch { CompanyId = company.Id, Name = "Main", Code = "MAIN", IsDefault = true };
        seed.Branches.Add(branch);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Name = "Main Warehouse",
            Code = "MAIN"
        };
        seed.Warehouses.Add(warehouse);

        var supplier = new Supplier { CompanyId = company.Id, Code = "SUP-1", Name = "Shenzhen Electronics" };
        seed.Suppliers.Add(supplier);

        var laptop = new Product
        {
            CompanyId = company.Id,
            ItemCode = "LAPTOP",
            Sku = "LAPTOP",
            Name = "Laptop",
            Unit = "each",
            TrackingMode = TrackingMode.Serial
        };

        var cable = new Product
        {
            CompanyId = company.Id,
            ItemCode = "CABLE",
            Sku = "CABLE",
            Name = "HDMI cable",
            Unit = "each",
            TrackingMode = TrackingMode.None
        };

        seed.Products.AddRange(laptop, cable);

        // Every document takes a number, and the sequences must exist before the first one is raised.
        foreach (var (type, prefix) in new[]
                 {
                     (DocumentType.GoodsReceipt, "GRN"),
                     (DocumentType.ImportShipment, "IMP"),
                     (DocumentType.PurchaseOrder, "PO")
                 })
        {
            seed.DocumentNumberSequences.Add(new DocumentNumberSequence
            {
                CompanyId = company.Id,
                BranchId = branch.Id,
                DocumentType = type,
                Prefix = prefix,
                Year = 2026,
                NextNumber = 1,
                Padding = 5,
                ResetsAnnually = true
            });
        }

        await seed.SaveChangesAsync();

        _companyA = company.Id;
        _branchOfA = branch.Id;
        _warehouseOfA = warehouse.Id;
        _supplier = supplier.Id;
        _laptop = laptop.Id;
        _cable = cable.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // --- Receiving ------------------------------------------------------------------------------

    [Fact]
    public async Task A_direct_purchase_needs_no_order_and_still_reaches_the_shelf()
    {
        // Requirements §25's direct flow. The shop drove to the wholesaler and came back with a box.
        await using var db = CreateContext(_companyA);

        var receiptId = await ReceiveAsync(db, new ReceiveLine(_cable, Quantity: 100, UnitPrice: 50m));

        var receipt = await db.GoodsReceipts.Include(r => r.Lines).FirstAsync(r => r.Id == receiptId);

        receipt.PurchaseOrderId.Should().BeNull("a PO is optional — §25 says so outright");
        receipt.Number.Should().StartWith("GRN-");

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.Quantity.Should().Be(100);
        balance.AverageCost.Should().Be(50m);
    }

    [Fact]
    public async Task Serials_are_captured_at_the_door()
    {
        // Not at the sale. This is what makes a warranty claim answerable two years later: the serial
        // ties the laptop on the counter back to the box it arrived in.
        await using var db = CreateContext(_companyA);

        var receiptId = await ReceiveAsync(
            db,
            new ReceiveLine(_laptop, Quantity: 2, UnitPrice: 1_000m, SerialNumbers: ["SN-A", "SN-B"]));

        var captured = await db.GoodsReceiptSerials
            .Where(s => s.GoodsReceiptLine.GoodsReceiptId == receiptId)
            .Select(s => s.SerialNumber)
            .ToListAsync();

        captured.Should().BeEquivalentTo(["SN-A", "SN-B"]);

        // And the ledger owns them, in stock, in this warehouse.
        var serials = await db.Serials.ToListAsync();
        serials.Should().HaveCount(2);
        serials.Should().OnlyContain(s => s.Status == SerialStatus.InStock && s.WarehouseId == _warehouseOfA);
    }

    [Fact]
    public async Task A_foreign_currency_receipt_books_stock_in_the_companys_own_money()
    {
        // The supplier billed USD 1,000 a unit; the shop's books are in AED at 3.67.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(
            db,
            new ReceiveLine(_cable, Quantity: 10, UnitPrice: 1_000m),
            currencyCode: "USD",
            exchangeRate: 3.67m);

        var balance = await db.StockBalances.SingleAsync(b => b.ProductId == _cable);

        balance.AverageCost.Should().Be(3_670m, "stock is valued in base currency, always");
    }

    [Fact]
    public async Task A_failed_line_rolls_the_whole_receipt_back()
    {
        // A half-received delivery is worse than a rejected one: the stock and the paperwork would
        // disagree, and nobody would know which was right.
        await using var db = CreateContext(_companyA);

        // Two serials promised, one supplied — the ledger refuses.
        var act = async () => await ReceiveAsync(
            db,
            new ReceiveLine(_laptop, Quantity: 2, UnitPrice: 1_000m, SerialNumbers: ["ONLY-ONE"]));

        await act.Should().ThrowAsync<DomainException>();

        await using var fresh = CreateContext(_companyA);

        (await fresh.GoodsReceipts.AnyAsync()).Should().BeFalse();
        (await fresh.StockMovements.AnyAsync()).Should().BeFalse();
        (await fresh.Serials.AnyAsync()).Should().BeFalse();
    }

    // --- Landed cost: the whole point of the phase ------------------------------------------------

    [Fact]
    public async Task The_agreed_worked_example_lands_on_the_shelf()
    {
        // Decision D6, end to end and against a real database. Ten laptops at 1,000 and a hundred
        // cables at 50, in a container carrying AED 3,000 of freight, duty and clearing.
        //
        //   laptops: 3000 × 10000/15000 = 2,000 → +200 a unit → 1,200
        //   cables:  3000 ×  5000/15000 = 1,000 → + 10 a unit →    60
        //
        // This is the number that feeds the moving average, so it is the number that matters most in
        // the entire purchasing module.
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);

        await ReceiveAsync(
            db,
            [
                new ReceiveLine(_laptop, 10, 1_000m, SerialNumbers: Serials(10)),
                new ReceiveLine(_cable, 100, 50m)
            ],
            shipmentId: shipmentId);

        // Before the charges land, stock is worth the goods price and nothing more.
        (await db.StockBalances.SingleAsync(b => b.ProductId == _laptop)).AverageCost.Should().Be(1_000m);

        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 2_000m);
        await AddChargeAsync(db, shipmentId, ImportChargeType.Customs, 1_000m);

        var result = await ApportionAsync(db, shipmentId);

        result.TotalCharges.Should().Be(3_000m);
        result.Absorbed.Should().Be(3_000m);
        result.Unabsorbed.Should().Be(0m, "every unit is still on the shelf to carry its share");

        await using var fresh = CreateContext(_companyA);

        var laptops = await fresh.StockBalances.SingleAsync(b => b.ProductId == _laptop);
        var cables = await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable);

        laptops.AverageCost.Should().Be(1_200m);
        laptops.Quantity.Should().Be(10, "freight does not conjure laptops");

        cables.AverageCost.Should().Be(60m);
        cables.Quantity.Should().Be(100);
    }

    [Fact]
    public async Task The_ledger_can_still_prove_itself_after_a_revaluation()
    {
        // The invariant P3 built and P4 could easily have broken. The balance audit recomputes value as
        // SUM(quantity × unit_cost + value_adjustment) — leave that second term out and every import
        // the shop ever landed would show up as a permanent discrepancy nobody could clear.
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);

        await ReceiveAsync(db, [new ReceiveLine(_cable, 100, 50m)], shipmentId: shipmentId);
        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 1_000m);
        await ApportionAsync(db, shipmentId);

        var audit = await new BalanceAuditor(db).AuditAsync();

        audit.Agrees.Should().BeTrue("the cache must still equal the ledger once landed cost is folded in");
        audit.Discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task A_revaluation_moves_money_and_not_a_single_unit()
    {
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);
        await ReceiveAsync(db, [new ReceiveLine(_cable, 100, 50m)], shipmentId: shipmentId);
        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 1_000m);
        await ApportionAsync(db, shipmentId);

        var revaluation = await db.StockMovements.SingleAsync(m => m.Type == MovementType.Revaluation);

        revaluation.Quantity.Should().Be(0m);
        revaluation.ValueAdjustment.Should().Be(1_000m);
        revaluation.AverageCostAfter.Should().Be(60m);
        revaluation.BalanceAfter.Should().Be(100m, "the quantity is untouched");
    }

    [Fact]
    public async Task A_container_cannot_be_costed_twice()
    {
        // The worst thing this module could do: fold the freight into the moving average a second time,
        // doubling it on every unit. Because the average is moving, it would never wash back out.
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);
        await ReceiveAsync(db, [new ReceiveLine(_cable, 100, 50m)], shipmentId: shipmentId);
        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 1_000m);

        await ApportionAsync(db, shipmentId);

        var act = async () => await ApportionAsync(db, shipmentId);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already been costed*");

        // And the average did not move a second time.
        await using var fresh = CreateContext(_companyA);
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).AverageCost.Should().Be(60m);
    }

    [Fact]
    public async Task Cost_that_no_stock_was_left_to_absorb_is_reported_rather_than_hidden()
    {
        // The container sold out before the clearing agent invoiced. That money is real, and it has
        // nowhere in inventory to live. Dropping it would overstate margin; smearing it over whatever
        // else is on the shelf would charge one container's freight to another's goods.
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);
        await ReceiveAsync(db, [new ReceiveLine(_cable, 100, 50m)], shipmentId: shipmentId);

        // Half the container walks out of the shop before the freight invoice arrives.
        await SellAsync(db, _cable, 50);

        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 1_000m);

        var result = await ApportionAsync(db, shipmentId);

        result.Absorbed.Should().Be(500m, "only the fifty units still on the shelf can carry the freight");
        result.Unabsorbed.Should().Be(500m, "the rest is a real expense with nowhere in inventory to go");

        await using var fresh = CreateContext(_companyA);

        var shipment = await fresh.ImportShipments.FirstAsync(s => s.Id == shipmentId);
        shipment.UnabsorbedCost.Should().Be(500m, "recorded on the document, visible and attributable");

        // The survivors carry their own share and no more: 50 units, 500 of freight → +10 each.
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).AverageCost.Should().Be(60m);
    }

    [Fact]
    public async Task Charges_cannot_be_added_after_the_container_is_costed()
    {
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);
        await ReceiveAsync(db, [new ReceiveLine(_cable, 100, 50m)], shipmentId: shipmentId);
        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 1_000m);
        await ApportionAsync(db, shipmentId);

        var act = async () => await AddChargeAsync(db, shipmentId, ImportChargeType.Clearing, 200m);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task A_shipment_with_no_goods_cannot_be_costed()
    {
        // There is nothing for the charges to attach to. The revaluation would have no stock to raise.
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);
        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 1_000m);

        var act = async () => await ApportionAsync(db, shipmentId);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*nothing for its charges to attach to*");
    }

    [Fact]
    public async Task Charges_billed_in_two_currencies_are_apportioned_in_the_companys_own()
    {
        // Freight from the shipping line in USD; duty from the customs authority in AED. A container's
        // charges cannot be added up in any currency but the company's.
        await using var db = CreateContext(_companyA);

        var shipmentId = await CreateShipmentAsync(db);
        await ReceiveAsync(db, [new ReceiveLine(_cable, 100, 50m)], shipmentId: shipmentId);

        await AddChargeAsync(db, shipmentId, ImportChargeType.Freight, 100m, "USD", 3.67m);   // 367 AED
        await AddChargeAsync(db, shipmentId, ImportChargeType.Customs, 133m);                 // 133 AED

        var result = await ApportionAsync(db, shipmentId);

        result.TotalCharges.Should().Be(500m);
        result.Absorbed.Should().Be(500m);

        await using var fresh = CreateContext(_companyA);
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).AverageCost.Should().Be(55m);
    }

    // --- Fixture --------------------------------------------------------------------------------

    private static string[] Serials(int count) =>
        Enumerable.Range(1, count).Select(i => $"SN-{i:D3}").ToArray();

    private Task<Guid> ReceiveAsync(
        ApplicationDbContext db,
        ReceiveLine line,
        string currencyCode = "AED",
        decimal exchangeRate = 1m) =>
        ReceiveAsync(db, [line], currencyCode, exchangeRate);

    private async Task<Guid> ReceiveAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<ReceiveLine> lines,
        string currencyCode = "AED",
        decimal exchangeRate = 1m,
        Guid? shipmentId = null)
    {
        var handler = new ReceiveGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock());

        return await handler.Handle(
            new ReceiveGoodsCommand(
                SupplierId: _supplier,
                BranchId: _branchOfA,
                WarehouseId: _warehouseOfA,
                Lines: lines,
                ImportShipmentId: shipmentId,
                CurrencyCode: currencyCode,
                ExchangeRate: exchangeRate),
            CancellationToken.None);
    }

    private async Task<Guid> CreateShipmentAsync(ApplicationDbContext db)
    {
        var handler = new CreateImportShipmentCommandHandler(db, Numbers(db));

        return await handler.Handle(
            new CreateImportShipmentCommand(_supplier, _branchOfA, TransportDocument: "BL-12345"),
            CancellationToken.None);
    }

    private async Task<Guid> AddChargeAsync(
        ApplicationDbContext db,
        Guid shipmentId,
        ImportChargeType type,
        decimal amount,
        string currency = "AED",
        decimal rate = 1m)
    {
        var handler = new AddImportChargeCommandHandler(db, new StubClock());

        return await handler.Handle(
            new AddImportChargeCommand(shipmentId, type, amount, currency, rate),
            CancellationToken.None);
    }

    private async Task<ApportionmentResultDto> ApportionAsync(ApplicationDbContext db, Guid shipmentId)
    {
        var handler = new ApportionLandedCostCommandHandler(db, Ledger(db), new StubUser(), new StubClock());

        return await handler.Handle(new ApportionLandedCostCommand(shipmentId), CancellationToken.None);
    }

    /// <summary>Sells stock straight through the ledger — P5 does not exist yet.</summary>
    private async Task SellAsync(ApplicationDbContext db, Guid productId, decimal quantity)
    {
        await using var transaction = await db.BeginTransactionAsync();

        await Ledger(db).PostAsync(new StockPosting(
            WarehouseId: _warehouseOfA,
            BranchId: _branchOfA,
            ProductId: productId,
            Type: MovementType.Sale,
            Quantity: quantity,
            ReferenceType: StockReferenceType.Invoice));

        await transaction.CommitAsync();
    }

    private StockLedger Ledger(ApplicationDbContext db) =>
        new(
            new ApplicationDbContextAccessor(db),
            db,
            new StubTenant(_companyA),
            new StubClock(),
            new WeightedAverageCosting());

    private DocumentNumberGenerator Numbers(ApplicationDbContext db) =>
        new(new ApplicationDbContextAccessor(db), db, new StubTenant(_companyA), new StubClock());

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
        public bool IsAuthenticated => true;
        public bool IsPlatformAdmin => false;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "tests";
    }

    /// <summary>Fixed at 2026 so the document-number sequences seeded for that year are the ones used.</summary>
    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
    }
}
