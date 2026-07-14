using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Purchasing.GoodsReceipts;
using TechStorePro.Application.Purchasing.Imports;
using TechStorePro.Application.Purchasing.Invoices;
using TechStorePro.Application.Purchasing.Orders;
using TechStorePro.Application.Purchasing.Payments;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Finance;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Purchasing;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Finance;
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
    private Guid _bankTransfer;

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

        // P7: the money has to leave from somewhere. The bank carries an overdraft, as a real one does —
        // these tests are about landed cost and FX, not about whether the shop can afford the container.
        var bank = new FinancialAccount
        {
            CompanyId = company.Id,
            Name = "Bank",
            Kind = FinancialAccountKind.Bank,
            CurrencyCode = "AED",
            AllowsOverdraft = true
        };

        seed.FinancialAccounts.Add(bank);

        var bankTransfer = new PaymentMethod
        {
            CompanyId = company.Id,
            Name = "Bank transfer",
            Kind = PaymentMethodKind.BankTransfer,
            RequiresReference = true,
            FinancialAccountId = bank.Id
        };

        seed.PaymentMethods.Add(bankTransfer);

        // Every document takes a number, and the sequences must exist before the first one is raised.
        foreach (var (type, prefix) in new[]
                 {
                     (DocumentType.GoodsReceipt, "GRN"),
                     (DocumentType.ImportShipment, "IMP"),
                     (DocumentType.PurchaseOrder, "PO"),
                     (DocumentType.SupplierInvoice, "SINV"),
                     (DocumentType.SupplierPayment, "SPAY")
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
        _bankTransfer = bankTransfer.Id;
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

    // --- Purchase orders --------------------------------------------------------------------------

    [Fact]
    public async Task Goods_cannot_be_received_against_an_unapproved_order()
    {
        // Approving is what commits the company's money, and it is the gate on receiving. A draft that
        // could take delivery would make the approval a formality nobody had to perform.
        await using var db = CreateContext(_companyA);

        var orderId = await CreateOrderAsync(db, (_cable, 100m, 50m));

        var act = async () => await ReceiveAgainstOrderAsync(db, orderId);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Approve it first*");
    }

    [Fact]
    public async Task Receiving_against_an_order_ticks_it_off_and_closes_it()
    {
        await using var db = CreateContext(_companyA);

        var orderId = await CreateOrderAsync(db, (_cable, 100m, 50m));
        await ApproveOrderAsync(db, orderId);

        await ReceiveAgainstOrderAsync(db, orderId);

        await using var fresh = CreateContext(_companyA);

        var order = await fresh.PurchaseOrders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);

        order.Status.Should().Be(PurchaseOrderStatus.Received);
        order.Lines.Single().ReceivedQuantity.Should().Be(100m);
        order.IsFullyReceived.Should().BeTrue();
    }

    [Fact]
    public async Task A_part_delivery_leaves_the_order_open()
    {
        // The supplier sent 60 of the 100 ordered. The order is not done, and closing it would lose the
        // shop's claim to the other forty.
        await using var db = CreateContext(_companyA);

        var orderId = await CreateOrderAsync(db, (_cable, 100m, 50m));
        await ApproveOrderAsync(db, orderId);

        await ReceiveAgainstOrderAsync(db, orderId, quantity: 60m);

        await using var fresh = CreateContext(_companyA);

        var order = await fresh.PurchaseOrders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);

        order.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        order.Lines.Single().OutstandingQuantity.Should().Be(40m);
    }

    [Fact]
    public async Task An_order_closes_even_when_the_receipt_does_not_name_its_lines()
    {
        // The regression this exists for was silent and expensive. A receipt that named the order but not
        // its individual lines posted the stock, captured the serials — and left the order sitting at
        // Approved for ever. Fully delivered, still showing as outstanding, and chased. Nothing errored,
        // so nobody found out until someone asked why the supplier kept sending goods that had already
        // arrived.
        await using var db = CreateContext(_companyA);

        var orderId = await CreateOrderAsync(db, (_cable, 100m, 50m));
        await ApproveOrderAsync(db, orderId);

        var handler = new ReceiveGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock());

        await handler.Handle(
            new ReceiveGoodsCommand(
                SupplierId: _supplier,
                BranchId: _branchOfA,
                WarehouseId: _warehouseOfA,

                // No PurchaseOrderLineId — the caller names the order and nothing more.
                Lines: [new ReceiveLine(_cable, Quantity: 100, UnitPrice: 50m)],
                PurchaseOrderId: orderId),
            CancellationToken.None);

        await using var fresh = CreateContext(_companyA);

        var order = await fresh.PurchaseOrders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);

        order.Status.Should().Be(PurchaseOrderStatus.Received, "everything on it arrived");
        order.Lines.Single().ReceivedQuantity.Should().Be(100m);

        // And the receipt remembers which line it fulfilled, so the next one need not guess again.
        var receiptLine = await fresh.GoodsReceiptLines.FirstAsync(l => l.ProductId == _cable);
        receiptLine.PurchaseOrderLineId.Should().Be(order.Lines.Single().Id);
    }

    [Fact]
    public async Task An_order_that_goods_have_arrived_against_cannot_be_cancelled()
    {
        // The stock is on the shelf and the supplier will invoice for it. An order claiming it never
        // happened would leave the receipt pointing at nothing.
        await using var db = CreateContext(_companyA);

        var orderId = await CreateOrderAsync(db, (_cable, 100m, 50m));
        await ApproveOrderAsync(db, orderId);
        await ReceiveAgainstOrderAsync(db, orderId);

        var handler = new CancelPurchaseOrderCommandHandler(db);

        var act = async () => await handler.Handle(
            new CancelPurchaseOrderCommand(orderId, "changed our minds"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already been received*");
    }

    [Fact]
    public async Task An_order_does_not_move_stock()
    {
        // Only the goods receipt does. An order that reserved or booked stock would have the shop
        // selling laptops that are still in a factory in Shenzhen.
        await using var db = CreateContext(_companyA);

        var orderId = await CreateOrderAsync(db, (_laptop, 10m, 1_000m));
        await ApproveOrderAsync(db, orderId);

        await using var fresh = CreateContext(_companyA);

        (await fresh.StockMovements.AnyAsync()).Should().BeFalse();
        (await fresh.StockBalances.AnyAsync()).Should().BeFalse();
    }

    // --- Supplier invoices ------------------------------------------------------------------------

    [Fact]
    public async Task Posting_an_invoice_puts_the_debt_on_the_supplier_and_moves_no_stock()
    {
        // The receipt already moved the stock. An invoice that moved it as well would double it.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, new ReceiveLine(_cable, 100, 50m));

        var movementsAfterReceipt = await db.StockMovements.CountAsync();

        await RecordInvoiceAsync(db, total: 5_000m);

        await using var fresh = CreateContext(_companyA);

        var supplier = await fresh.Suppliers.FirstAsync(s => s.Id == _supplier);
        supplier.Balance.Should().Be(5_000m, "posting the invoice is what creates the debt");

        (await fresh.StockMovements.CountAsync()).Should().Be(
            movementsAfterReceipt,
            "the goods receipt moved the stock; the invoice must not move it again");
    }

    [Fact]
    public async Task A_draft_invoice_owes_nothing_until_it_is_posted()
    {
        // A bill somebody is still checking against the receipt is not yet a debt.
        await using var db = CreateContext(_companyA);

        var invoiceId = await RecordInvoiceAsync(db, total: 5_000m, post: false);

        (await db.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(0m);

        await using var second = CreateContext(_companyA);

        var handler = new PostSupplierInvoiceCommandHandler(second);
        await handler.Handle(new PostSupplierInvoiceCommand(invoiceId), CancellationToken.None);

        await using var fresh = CreateContext(_companyA);
        (await fresh.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(5_000m);
    }

    [Fact]
    public async Task An_invoice_billed_in_a_foreign_currency_owes_base_currency_at_the_invoice_rate()
    {
        // The supplier bills USD 1,000. What the shop owes is a number in AED, and it is fixed on the
        // day the invoice is raised — not re-read later, or the debt would move with the market.
        await using var db = CreateContext(_companyA);

        await RecordInvoiceAsync(db, total: 1_000m, currency: "USD", rate: 3.67m);

        await using var fresh = CreateContext(_companyA);

        (await fresh.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(3_670m);
    }

    // --- Supplier payments, and the FX gain the phase was really about ----------------------------

    [Fact]
    public async Task Paying_a_local_invoice_clears_the_balance()
    {
        await using var db = CreateContext(_companyA);

        var invoiceId = await RecordInvoiceAsync(db, total: 5_000m);

        await PayAsync(db, amount: 5_000m, allocations: [new SupplierAllocationLine(invoiceId, 5_000m)]);

        await using var fresh = CreateContext(_companyA);

        var invoice = await fresh.SupplierInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .FirstAsync(i => i.Id == invoiceId);

        invoice.Status.Should().Be(SupplierInvoiceStatus.Paid);
        invoice.OutstandingAmount.Should().Be(0m);

        (await fresh.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(0m);
    }

    [Fact]
    public async Task A_dollar_invoice_paid_at_a_better_rate_realises_a_gain_and_still_clears_the_balance()
    {
        // The worked example from the plan, and the one piece of arithmetic in this module that is easy
        // to get wrong.
        //
        // A USD 1,000 invoice booked at 3.67 is a debt of AED 3,670. Pay it when the rate is 3.60 and
        // only AED 3,600 leaves the bank. The shop is AED 70 better off — a gain it made by owing
        // dollars, not by selling anything.
        //
        // The trap: subtract only what left the bank, and the supplier's balance keeps AED 70 owing
        // forever, on an invoice that is fully settled in the currency it was billed in. No amount of
        // paying would ever clear it, because the residue is not a debt — it is the gain.
        await using var db = CreateContext(_companyA);

        var invoiceId = await RecordInvoiceAsync(db, total: 1_000m, currency: "USD", rate: 3.67m);

        (await db.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(3_670m);

        var paymentId = await PayAsync(
            db,
            amount: 1_000m,
            allocations: [new SupplierAllocationLine(invoiceId, 1_000m)],
            currency: "USD",
            rate: 3.60m);

        await using var fresh = CreateContext(_companyA);

        var payment = await fresh.SupplierPayments
            .Include(p => p.Allocations)
            .FirstAsync(p => p.Id == paymentId);

        payment.AmountBase.Should().Be(3_600m, "that is what actually left the bank");
        payment.ExchangeGainOrLoss.Should().Be(70m, "the dirham strengthened between invoice and payment");

        var invoice = await fresh.SupplierInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .FirstAsync(i => i.Id == invoiceId);

        invoice.Status.Should().Be(SupplierInvoiceStatus.Paid, "USD 1,000 billed, USD 1,000 paid");

        (await fresh.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(
            0m,
            "the debt is gone — the 70 the shop kept is a gain, not an amount still owed");
    }

    [Fact]
    public async Task An_fx_gain_does_not_touch_the_cost_of_the_stock()
    {
        // The laptops did not become cheaper to buy; the currency moved. Folding the gain back into the
        // moving average would restate the cost of stock that arrived years ago (D1), and it would never
        // wash out.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(
            db,
            new ReceiveLine(_cable, Quantity: 10, UnitPrice: 100m),
            currencyCode: "USD",
            exchangeRate: 3.67m);

        var costAfterReceipt =
            (await db.StockBalances.SingleAsync(b => b.ProductId == _cable)).AverageCost;

        costAfterReceipt.Should().Be(367m);

        var invoiceId = await RecordInvoiceAsync(db, total: 1_000m, currency: "USD", rate: 3.67m);

        await PayAsync(
            db,
            amount: 1_000m,
            allocations: [new SupplierAllocationLine(invoiceId, 1_000m)],
            currency: "USD",
            rate: 3.60m);

        await using var fresh = CreateContext(_companyA);

        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).AverageCost.Should().Be(
            costAfterReceipt,
            "the gain belongs in the P&L, not in the cost of the goods");
    }

    [Fact]
    public async Task One_transfer_settles_three_invoices()
    {
        // The reason a payment is a header plus allocations rather than a column on an invoice. A shop
        // that pays its supplier monthly does this constantly.
        await using var db = CreateContext(_companyA);

        var first = await RecordInvoiceAsync(db, total: 1_000m, reference: "SUP-001");
        var second = await RecordInvoiceAsync(db, total: 2_000m, reference: "SUP-002");
        var third = await RecordInvoiceAsync(db, total: 3_000m, reference: "SUP-003");

        await PayAsync(
            db,
            amount: 6_000m,
            allocations:
            [
                new SupplierAllocationLine(first, 1_000m),
                new SupplierAllocationLine(second, 2_000m),
                new SupplierAllocationLine(third, 3_000m)
            ]);

        await using var fresh = CreateContext(_companyA);

        var invoices = await fresh.SupplierInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .ToListAsync();

        invoices.Should().OnlyContain(i => i.Status == SupplierInvoiceStatus.Paid);
        (await fresh.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(0m);
    }

    [Fact]
    public async Task One_invoice_is_settled_by_two_instalments()
    {
        await using var db = CreateContext(_companyA);

        var invoiceId = await RecordInvoiceAsync(db, total: 5_000m);

        await PayAsync(db, amount: 2_000m, allocations: [new SupplierAllocationLine(invoiceId, 2_000m)]);

        await using var midway = CreateContext(_companyA);

        var half = await midway.SupplierInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .FirstAsync(i => i.Id == invoiceId);

        half.Status.Should().Be(SupplierInvoiceStatus.PartiallyPaid);
        half.OutstandingAmount.Should().Be(3_000m);

        await PayAsync(midway, amount: 3_000m, allocations: [new SupplierAllocationLine(invoiceId, 3_000m)]);

        await using var fresh = CreateContext(_companyA);

        var settled = await fresh.SupplierInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .FirstAsync(i => i.Id == invoiceId);

        settled.Status.Should().Be(SupplierInvoiceStatus.Paid);
        (await fresh.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(0m);
    }

    [Fact]
    public async Task An_invoice_cannot_be_paid_twice_over()
    {
        // Over-paying is refused against *that invoice*. The extra money is real and belongs on the
        // account as an advance, not hidden inside a document that would then show as more than settled.
        await using var db = CreateContext(_companyA);

        var invoiceId = await RecordInvoiceAsync(db, total: 5_000m);

        await PayAsync(db, amount: 5_000m, allocations: [new SupplierAllocationLine(invoiceId, 5_000m)]);

        var act = async () => await PayAsync(
            db,
            amount: 5_000m,
            allocations: [new SupplierAllocationLine(invoiceId, 5_000m)]);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*outstanding*");
    }

    [Fact]
    public async Task Money_paid_before_the_invoice_arrives_is_an_advance_not_an_error()
    {
        // A real state. It sits as an unallocated credit and takes the supplier's balance negative,
        // which is exactly what "they owe us" looks like.
        await using var db = CreateContext(_companyA);

        var paymentId = await PayAsync(db, amount: 2_000m, allocations: []);

        await using var fresh = CreateContext(_companyA);

        var payment = await fresh.SupplierPayments
            .Include(p => p.Allocations)
            .FirstAsync(p => p.Id == paymentId);

        payment.UnallocatedAmount.Should().Be(2_000m);

        (await fresh.Suppliers.FirstAsync(s => s.Id == _supplier)).Balance.Should().Be(-2_000m);
    }

    [Fact]
    public async Task A_payment_cannot_settle_another_suppliers_invoice()
    {
        // Both balances would be wrong and the error invisible: the invoice would look paid, and the
        // right supplier would keep chasing.
        await using var db = CreateContext(_companyA);

        var other = new Supplier { CompanyId = _companyA, Code = "SUP-2", Name = "Dubai Traders" };
        db.Suppliers.Add(other);
        await db.SaveChangesAsync();

        var invoiceId = await RecordInvoiceAsync(db, total: 1_000m);

        var handler = new PaySupplierCommandHandler(db, Tenant(), Accounts(db), Numbers(db), new StubClock());

        var act = async () => await handler.Handle(
            new PaySupplierCommand(
                SupplierId: other.Id,
                BranchId: _branchOfA,
                PaymentMethodId: _bankTransfer,
                Amount: 1_000m,
                Allocations: [new SupplierAllocationLine(invoiceId, 1_000m)],
                Reference: "TRF-1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*different supplier*");
    }

    // --- Fixture --------------------------------------------------------------------------------

    private static string[] Serials(int count) =>
        Enumerable.Range(1, count).Select(i => $"SN-{i:D3}").ToArray();

    private async Task<Guid> CreateOrderAsync(
        ApplicationDbContext db,
        params (Guid ProductId, decimal Quantity, decimal UnitPrice)[] lines)
    {
        var handler = new CreatePurchaseOrderCommandHandler(db, Numbers(db), new StubClock());

        return await handler.Handle(
            new CreatePurchaseOrderCommand(
                SupplierId: _supplier,
                BranchId: _branchOfA,
                WarehouseId: _warehouseOfA,
                Lines: lines
                    .Select(l => new PurchaseOrderLineInput(l.ProductId, l.Quantity, l.UnitPrice))
                    .ToList()),
            CancellationToken.None);
    }

    private async Task ApproveOrderAsync(ApplicationDbContext db, Guid orderId)
    {
        var handler = new ApprovePurchaseOrderCommandHandler(db, new StubUser(), new StubClock());

        await handler.Handle(new ApprovePurchaseOrderCommand(orderId), CancellationToken.None);
    }

    /// <summary>Receives the order's only line, in full unless a smaller quantity is asked for.</summary>
    private async Task<Guid> ReceiveAgainstOrderAsync(
        ApplicationDbContext db,
        Guid orderId,
        decimal? quantity = null)
    {
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstAsync(o => o.Id == orderId);

        var line = order.Lines.Single();

        var handler = new ReceiveGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock());

        return await handler.Handle(
            new ReceiveGoodsCommand(
                SupplierId: _supplier,
                BranchId: _branchOfA,
                WarehouseId: _warehouseOfA,
                Lines:
                [
                    new ReceiveLine(
                        line.ProductId,
                        quantity ?? line.Quantity,
                        line.UnitPrice,
                        PurchaseOrderLineId: line.Id)
                ],
                PurchaseOrderId: orderId),
            CancellationToken.None);
    }

    private async Task<Guid> RecordInvoiceAsync(
        ApplicationDbContext db,
        decimal total,
        string currency = "AED",
        decimal rate = 1m,
        bool post = true,
        string reference = "SUP-INV-1")
    {
        var handler = new RecordSupplierInvoiceCommandHandler(db, Numbers(db), new StubClock());

        return await handler.Handle(
            new RecordSupplierInvoiceCommand(
                SupplierId: _supplier,
                BranchId: _branchOfA,
                SupplierReference: reference,
                Lines: [new SupplierInvoiceLineInput("Goods as delivered", Quantity: 1, UnitPrice: total)],
                CurrencyCode: currency,
                ExchangeRate: rate,
                Post: post),
            CancellationToken.None);
    }

    private async Task<Guid> PayAsync(
        ApplicationDbContext db,
        decimal amount,
        IReadOnlyCollection<SupplierAllocationLine> allocations,
        string currency = "AED",
        decimal rate = 1m)
    {
        var handler = new PaySupplierCommandHandler(db, Tenant(), Accounts(db), Numbers(db), new StubClock());

        return await handler.Handle(
            new PaySupplierCommand(
                SupplierId: _supplier,
                BranchId: _branchOfA,
                PaymentMethodId: _bankTransfer,
                Amount: amount,
                Allocations: allocations,
                CurrencyCode: currency,
                ExchangeRate: rate,
                Reference: "TRF-9001"),
            CancellationToken.None);
    }

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

    private AccountLedger Accounts(ApplicationDbContext db) =>
        new(new ApplicationDbContextAccessor(db), db, new StubTenant(_companyA), new StubClock());

    private DocumentNumberGenerator Numbers(ApplicationDbContext db) =>
        new(new ApplicationDbContextAccessor(db), db, new StubTenant(_companyA), new StubClock());

    private StubTenant Tenant() => new(_companyA);

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
