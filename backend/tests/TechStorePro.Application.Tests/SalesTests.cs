using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Sales.Deliveries;
using TechStorePro.Application.Sales.Invoices;
using TechStorePro.Application.Sales.Orders;
using TechStorePro.Application.Sales.Payments;
using TechStorePro.Application.Sales.Pos;
using TechStorePro.Application.Sales.Queries;
using TechStorePro.Application.Sales.Quotations;
using TechStorePro.Application.Sales.Returns;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Finance;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;
using TechStorePro.Infrastructure.Catalog;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Finance;
using TechStorePro.Infrastructure.Inventory;
using TechStorePro.Infrastructure.Persistence;
using TechStorePro.Infrastructure.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// Sales, against a real PostgreSQL.
///
/// <b>This is the file that proves the shop can sell something without losing track of it.</b> The
/// arithmetic of a line is unit-tested in <c>Domain.Tests/Sales</c>; what needs a database to be true is
/// everything else: that the stock actually left, that the serial went with it, that the cost booked
/// against the sale is the one the ledger valued it at, that the customer owes the money — and that no
/// path through the module can sell the same laptop twice.
/// </summary>
public class SalesTests : IAsyncLifetime
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
    private Guid _customer;      // corporate, 50,000 credit limit
    private Guid _walkIn;        // no credit

    private Guid _laptop;        // serial-tracked, sells at 1,500
    private Guid _cable;         // untracked, sells at 80

    private Guid _cash;
    private Guid _card;          // requires a reference — a card slip that cannot be reconciled is useless
    private Guid _storeCredit;   // tender, not a discount: the shop has already had this money

    private Guid _till;          // P7: where cash tendered at the counter actually lands
    private Guid _bankAccount;

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

        // The company's own tax rate. Nothing in the codebase assumes a jurisdiction or a percentage
        // (§45 D7) — this shop happens to configure 5%.
        var vat = new TaxRate
        {
            CompanyId = company.Id,
            Name = "Standard VAT",
            Percent = 5m,
            IsDefault = true,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        seed.TaxRates.Add(vat);

        var corporate = new Customer
        {
            CompanyId = company.Id,
            Code = "C-1",
            Name = "Omar Trading",
            Type = CustomerType.Corporate,
            CreditLimit = 50_000m,
            PaymentTermDays = 30
        };

        var walkIn = new Customer
        {
            CompanyId = company.Id,
            Code = "C-2",
            Name = "Walk-in",
            Type = CustomerType.WalkIn
        };

        seed.Customers.AddRange(corporate, walkIn);

        var laptop = new Product
        {
            CompanyId = company.Id,
            ItemCode = "LAPTOP",
            Sku = "LAPTOP",
            Name = "Laptop",
            Unit = "each",
            TrackingMode = TrackingMode.Serial,
            SellingPrice = 1_500m,
            TaxRateId = vat.Id
        };

        var cable = new Product
        {
            CompanyId = company.Id,
            ItemCode = "CABLE",
            Sku = "CABLE",
            Name = "HDMI cable",
            Unit = "each",
            TrackingMode = TrackingMode.None,
            SellingPrice = 80m,
            TaxRateId = vat.Id
        };

        seed.Products.AddRange(laptop, cable);

        // P7: money has to arrive somewhere. Every method that moves money names the account it lands in;
        // store credit deliberately names none, because no money moves when a customer spends a voucher —
        // the shop took that money when the goods came back.
        var till = new FinancialAccount
        {
            CompanyId = company.Id,
            Name = "Till",
            Kind = FinancialAccountKind.Cash,
            CurrencyCode = "AED",
            BranchId = branch.Id
        };

        var bank = new FinancialAccount
        {
            CompanyId = company.Id,
            Name = "Bank",
            Kind = FinancialAccountKind.Bank,
            CurrencyCode = "AED"
        };

        seed.FinancialAccounts.AddRange(till, bank);

        var cash = new PaymentMethod
        {
            CompanyId = company.Id,
            Name = "Cash",
            Kind = PaymentMethodKind.Cash,
            FinancialAccountId = till.Id,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var card = new PaymentMethod
        {
            CompanyId = company.Id,
            Name = "Card",
            Kind = PaymentMethodKind.Card,
            RequiresReference = true,
            FinancialAccountId = bank.Id,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var storeCredit = new PaymentMethod
        {
            CompanyId = company.Id,
            Name = "Store credit",
            Kind = PaymentMethodKind.StoreCredit,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        seed.PaymentMethods.AddRange(cash, card, storeCredit);

        foreach (var (type, prefix) in new[]
                 {
                     (DocumentType.Quotation, "QT"),
                     (DocumentType.SalesOrder, "SO"),
                     (DocumentType.DeliveryNote, "DLV"),
                     (DocumentType.Invoice, "INV"),
                     (DocumentType.Payment, "PAY"),
                     (DocumentType.CreditNote, "CN")
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
        _customer = corporate.Id;
        _walkIn = walkIn.Id;
        _laptop = laptop.Id;
        _cable = cable.Id;
        _cash = cash.Id;
        _card = card.Id;
        _storeCredit = storeCredit.Id;
        _till = till.Id;
        _bankAccount = bank.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // --- The money path ---------------------------------------------------------------------------

    [Fact]
    public async Task Selling_a_serial_tracked_laptop_end_to_end()
    {
        // The flow the development plan names as the one that loses money if it breaks:
        // quote → order (reserves) → delivery (picks the serial) → invoice.
        //
        // Two laptops are in stock at 1,200 (what P4's landed cost put them at). One is sold at 1,500.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, quantity: 2, unitCost: 1_200m, serials: ["SN-A", "SN-B"]);

        var quotationId = await QuoteAsync(db, _customer, (_laptop, 1m));
        await AcceptQuotationAsync(db, quotationId);

        var orderId = await ConvertAsync(db, quotationId);
        await ConfirmAsync(db, orderId);

        // Reserved, not yet moved: the laptop is promised but still on the shelf.
        await using (var reserved = CreateContext(_companyA))
        {
            var balance = await reserved.StockBalances.SingleAsync(b => b.ProductId == _laptop);
            balance.Quantity.Should().Be(2m, "a reservation promises stock, it does not move it");
            balance.ReservedQuantity.Should().Be(1m);
            (balance.Quantity - balance.ReservedQuantity).Should().Be(1m, "available = quantity − reserved");
        }

        var deliveryId = await DeliverAsync(db, orderId, (_laptop, 1m, new[] { "SN-A" }));
        var invoiceId = await InvoiceAsync(db, deliveryId);

        await using var fresh = CreateContext(_companyA);

        // 1. The stock left, and the reservation went with it.
        var after = await fresh.StockBalances.SingleAsync(b => b.ProductId == _laptop);
        after.Quantity.Should().Be(1m);
        after.ReservedQuantity.Should().Be(0m, "the delivery consumed the promise rather than competing with it");
        after.AverageCost.Should().Be(1_200m, "issuing stock does not change what the rest of it cost");

        // 2. The right machine left, and it is not coming back to the shelf by itself.
        var serialA = await fresh.Serials.FirstAsync(s => s.SerialNumber == "SN-A");
        var serialB = await fresh.Serials.FirstAsync(s => s.SerialNumber == "SN-B");

        serialA.Status.Should().Be(SerialStatus.Sold);
        serialA.WarehouseId.Should().BeNull("a sold unit is not in any warehouse");
        serialB.Status.Should().Be(SerialStatus.InStock, "the other one never moved");

        // 3. The serial is bound to the invoice line that sold it. P6's warranty claim walks this link
        //    backwards, two years from now, from a machine on the counter to the sale that put it there.
        var invoice = await fresh.SalesInvoices
            .Include(i => i.Lines)
            .FirstAsync(i => i.Id == invoiceId);

        serialA.SoldInvoiceLineId.Should().Be(invoice.Lines.Single().Id);

        // 4. The money. Tax-exclusive (D7): 1,500 net, 5% tax, 1,575 total.
        invoice.NetTotal.Should().Be(1_500m);
        invoice.TaxTotal.Should().Be(75m);
        invoice.Total.Should().Be(1_575m);
        invoice.Status.Should().Be(SalesInvoiceStatus.Posted);
        invoice.CurrencyCode.Should().Be("AED", "sales are in the company's base currency (D8)");

        // 5. COGS is what the ledger valued the issue at — not what the product costs today.
        invoice.CostTotal.Should().Be(1_200m);
        invoice.GrossProfit.Should().Be(300m, "1,500 net − 1,200 cost; the 75 of tax is not the shop's money");

        // 6. The customer owes for it.
        var customer = await fresh.Customers.FirstAsync(c => c.Id == _customer);
        customer.Balance.Should().Be(1_575m, "posting the bill and recording the debt are one act");

        // 7. The order knows it is done.
        var order = await fresh.SalesOrders.FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(SalesOrderStatus.Delivered);
    }

    [Fact]
    public async Task The_same_serial_cannot_be_sold_twice()
    {
        // Quantities alone cannot prevent this. The serial state machine can: Sold is not a status a unit
        // returns from by itself.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 2, 1_200m, ["SN-A", "SN-B"]);

        var first = await DeliverDirectAsync(db, _customer, (_laptop, 1m, new[] { "SN-A" }));
        first.Should().NotBeEmpty();

        var act = async () => await DeliverDirectAsync(db, _customer, (_laptop, 1m, new[] { "SN-A" }));

        await act.Should().ThrowAsync<DomainException>();

        // And the second attempt left nothing behind: one laptop sold, one still on the shelf.
        await using var fresh = CreateContext(_companyA);

        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _laptop)).Quantity.Should().Be(1m);
        (await fresh.Deliveries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Overselling_is_refused_under_the_lock()
    {
        // "Prevent overselling" is a subtraction — available = quantity − reserved — and it is enforced
        // by the ledger, not by the caller.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 5, 40m);

        var orderId = await OrderAsync(db, _customer, (_cable, 5m));
        await ConfirmAsync(db, orderId);   // all five now promised

        var second = await OrderAsync(db, _customer, (_cable, 1m));

        var act = async () => await ConfirmAsync(db, second);

        await act.Should().ThrowAsync<InsufficientStockException>(
            "the sixth cable does not exist, and the first order already holds the five that do");
    }

    [Fact]
    public async Task A_delivery_consumes_its_reservation_rather_than_competing_with_it()
    {
        // The subtle one. Reserving all five and then delivering all five must succeed: without handing
        // the reservation back to the ledger, the delivery's own availability check would see zero
        // available and refuse the goods the order had already promised.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 5, 40m);

        var orderId = await OrderAsync(db, _customer, (_cable, 5m));
        await ConfirmAsync(db, orderId);

        var deliveryId = await DeliverAsync(db, orderId, (_cable, 5m, null));

        deliveryId.Should().NotBeEmpty();

        await using var fresh = CreateContext(_companyA);

        var balance = await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.Quantity.Should().Be(0m);
        balance.ReservedQuantity.Should().Be(0m, "the promise was kept, not left holding an empty shelf");

        var reservation = await fresh.StockReservations.SingleAsync();
        reservation.Status.Should().Be(ReservationStatus.Fulfilled);
    }

    [Fact]
    public async Task The_ledger_still_proves_itself_after_a_sale()
    {
        // stock_balances is a cache of stock_movements. If sales wrote stock any way other than through
        // the ledger, this is the test that would catch it — the same audit the nightly job runs.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 3, 1_200m, ["SN-A", "SN-B", "SN-C"]);
        await ReceiveAsync(db, _cable, 10, 40m);

        var deliveryId = await DeliverDirectAsync(db, _customer, (_laptop, 1m, new[] { "SN-B" }), (_cable, 4m, null));
        await InvoiceAsync(db, deliveryId);

        var audit = await new BalanceAuditor(db).AuditAsync();

        audit.Agrees.Should().BeTrue("nothing in sales may write stock except through IStockLedger");
        audit.Discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_line_rolls_the_whole_delivery_back()
    {
        // Three lines out of the door and the fourth refused would leave stock gone with no document
        // accounting for it.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);
        await ReceiveAsync(db, _laptop, 1, 1_200m, ["SN-A"]);

        // The cables are fine; the laptop asks for a serial that does not exist.
        var act = async () => await DeliverDirectAsync(
            db,
            _customer,
            (_cable, 2m, null),
            (_laptop, 1m, new[] { "SN-NOBODY" }));

        await act.Should().ThrowAsync<Exception>();

        await using var fresh = CreateContext(_companyA);

        (await fresh.Deliveries.AnyAsync()).Should().BeFalse();
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).Quantity
            .Should().Be(10m, "the cables never left — the delivery was refused as a whole");
    }

    // --- What the invoice is, and is not ------------------------------------------------------------

    [Fact]
    public async Task A_delivery_cannot_be_billed_twice()
    {
        // The second invoice would double the customer's debt and double the revenue, and would look
        // exactly as legitimate as the first.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var deliveryId = await DeliverDirectAsync(db, _customer, (_cable, 2m, null));
        await InvoiceAsync(db, deliveryId);

        var act = async () => await InvoiceAsync(db, deliveryId);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already been invoiced*");

        await using var fresh = CreateContext(_companyA);

        (await fresh.SalesInvoices.CountAsync()).Should().Be(1);
        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance.Should().Be(168m, "2 × 80 + 5% tax");
    }

    [Fact]
    public async Task Invoicing_moves_no_stock()
    {
        // The delivery already moved it. An invoice that moved it too would issue the same goods twice.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var deliveryId = await DeliverDirectAsync(db, _customer, (_cable, 3m, null));

        var movementsBefore = await db.StockMovements.CountAsync();

        await InvoiceAsync(db, deliveryId);

        await using var fresh = CreateContext(_companyA);

        (await fresh.StockMovements.CountAsync()).Should().Be(movementsBefore, "an invoice is money, not goods");
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).Quantity.Should().Be(7m);
    }

    [Fact]
    public async Task Cancelling_an_invoice_takes_the_debt_back_off_the_customer()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var deliveryId = await DeliverDirectAsync(db, _customer, (_cable, 2m, null));
        var invoiceId = await InvoiceAsync(db, deliveryId);

        await new CancelInvoiceCommandHandler(db)
            .Handle(new CancelInvoiceCommand(invoiceId, "Raised against the wrong account"), CancellationToken.None);

        await using var fresh = CreateContext(_companyA);

        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance.Should().Be(0m);

        // But the goods are still gone. Cancelling paperwork does not put stock back on the shelf; a
        // credit note does.
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).Quantity.Should().Be(8m);
    }

    // --- The decisions this phase was blocked on ----------------------------------------------------

    [Fact]
    public async Task A_sale_cannot_be_raised_in_a_foreign_currency()
    {
        // Decision D8. Invoicing in USD would create an exchange exposure on the receivable that nothing
        // in this system measures. Accepting the currency and ignoring the exposure is the worst option.
        await using var db = CreateContext(_companyA);

        var act = async () => await new CreateQuotationCommandHandler(db, Tenant(), Pricer(db), Numbers(db), new StubClock())
            .Handle(
                new CreateQuotationCommand(
                    BranchId: _branchOfA,
                    Lines: [new QuoteLine(_cable, 1m)],
                    CustomerId: _customer,
                    CurrencyCode: "USD"),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*AED*");
    }

    [Fact]
    public async Task The_price_the_customer_is_charged_says_where_it_came_from()
    {
        // P2 built a resolver that reports which list it used. A sale that did not record the answer
        // would leave "why was this customer charged that?" unanswerable a month later.
        await using var db = CreateContext(_companyA);

        var quotationId = await QuoteAsync(db, _customer, (_laptop, 1m));

        await using var fresh = CreateContext(_companyA);

        var line = await fresh.QuotationLines.FirstAsync(l => l.QuotationId == quotationId);

        line.UnitPrice.Should().Be(1_500m);
        line.TaxPercent.Should().Be(5m, "snapshotted, not linked — changing the rate must not restate this quote");
        line.PriceSource.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_order_beyond_the_customers_credit_limit_is_refused_before_the_goods_move()
    {
        // Checked at confirmation, because that is when the shop commits goods to someone who has not
        // paid. At delivery it would be too late — the laptop is already in their car.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 50, 1_200m, Serials(50));

        var customer = await db.Customers.FirstAsync(c => c.Id == _customer);
        customer.Balance = 49_000m;   // 50,000 limit, 1,000 of room
        await db.SaveChangesAsync();

        var orderId = await OrderAsync(db, _customer, (_laptop, 1m));   // 1,575 with tax

        var act = async () => await ConfirmAsync(db, orderId);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*credit limit*");

        // And nothing was reserved: a refused order holds no stock.
        await using var fresh = CreateContext(_companyA);
        (await fresh.StockReservations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_an_order_gives_the_shelf_back()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 5, 40m);

        var orderId = await OrderAsync(db, _customer, (_cable, 5m));
        await ConfirmAsync(db, orderId);

        await new CancelSalesOrderCommandHandler(db, Ledger(db))
            .Handle(new CancelSalesOrderCommand(orderId, "Customer changed their mind"), CancellationToken.None);

        await using var fresh = CreateContext(_companyA);

        var balance = await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable);
        balance.ReservedQuantity.Should().Be(0m);
        (balance.Quantity - balance.ReservedQuantity).Should().Be(5m, "all five are back on the shelf to sell");

        var reservation = await fresh.StockReservations.SingleAsync();
        reservation.Status.Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public async Task A_counter_sale_needs_no_order()
    {
        // The walk-in takes the goods there and then. Requiring an order would produce orders written
        // after the fact — the same fiction requiring a PO for every receipt would produce (§25).
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var deliveryId = await DeliverDirectAsync(db, _walkIn, (_cable, 1m, null));
        var invoiceId = await InvoiceAsync(db, deliveryId);

        await using var fresh = CreateContext(_companyA);

        var delivery = await fresh.Deliveries.FirstAsync(d => d.Id == deliveryId);
        delivery.SalesOrderId.Should().BeNull();
        delivery.Number.Should().StartWith("DLV-");

        var invoice = await fresh.SalesInvoices.Include(i => i.Lines).FirstAsync(i => i.Id == invoiceId);
        invoice.Number.Should().StartWith("INV-");
        invoice.Total.Should().Be(84m, "80 + 5% tax");
    }

    [Fact]
    public async Task An_order_cannot_be_delivered_before_it_is_confirmed()
    {
        // A draft order has reserved nothing, so delivering against it would take stock the shop never
        // promised — and quietly, since the order would still read as unconfirmed afterwards.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 5, 40m);

        var orderId = await OrderAsync(db, _customer, (_cable, 1m));

        var act = async () => await DeliverAsync(db, orderId, (_cable, 1m, null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Confirm it first*");
    }

    [Fact]
    public async Task An_order_cannot_be_over_delivered()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var orderId = await OrderAsync(db, _customer, (_cable, 2m));
        await ConfirmAsync(db, orderId);

        var act = async () => await DeliverAsync(db, orderId, (_cable, 3m, null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*nobody ordered*");
    }

    // --- Payments (requirements §23) ----------------------------------------------------------------

    [Fact]
    public async Task One_sale_can_be_settled_by_cash_and_card_together()
    {
        // The reason tender is a table and not a column on the payment. The customer pays 500 in notes
        // and puts the rest on a card; a single payment_method_id could not say so, and two payments
        // would make one sale look like two.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 1, 1_200m, ["SN-A"]);

        var deliveryId = await DeliverDirectAsync(db, _customer, (_laptop, 1m, new[] { "SN-A" }));
        var invoiceId = await InvoiceAsync(db, deliveryId);   // 1,575 with tax

        await PayAsync(
            db,
            _customer,
            methods: [new TenderLine(_cash, 500m), new TenderLine(_card, 1_075m, "AUTH-99")],
            allocations: [new AllocationLine(invoiceId, 1_575m)]);

        await using var fresh = CreateContext(_companyA);

        var payment = await fresh.CustomerPayments
            .Include(p => p.Methods)
            .Include(p => p.Allocations)
            .SingleAsync();

        payment.Methods.Should().HaveCount(2);
        payment.Amount.Should().Be(1_575m);
        payment.UnallocatedAmount.Should().Be(0m);

        var invoice = await fresh.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .FirstAsync(i => i.Id == invoiceId);

        invoice.Status.Should().Be(SalesInvoiceStatus.Paid);
        invoice.OutstandingAmount.Should().Be(0m);

        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance
            .Should().Be(0m, "the bill was raised and settled — the customer owes nothing");
    }

    [Fact]
    public async Task One_payment_can_settle_two_invoices()
    {
        // And the mirror of it: a single invoice_id on a payment could express neither this nor the
        // instalment case below.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var first = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 1m, null)));    // 84
        var second = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 2m, null)));   // 168

        await PayAsync(
            db,
            _customer,
            methods: [new TenderLine(_cash, 252m)],
            allocations: [new AllocationLine(first, 84m), new AllocationLine(second, 168m)]);

        await using var fresh = CreateContext(_companyA);

        var invoices = await fresh.SalesInvoices.Include(i => i.Lines).Include(i => i.Allocations).ToListAsync();

        invoices.Should().OnlyContain(i => i.Status == SalesInvoiceStatus.Paid);
        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance.Should().Be(0m);
    }

    [Fact]
    public async Task An_invoice_can_be_settled_by_two_instalments()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 2m, null)));  // 168

        await PayAsync(db, _customer, [new TenderLine(_cash, 100m)], [new AllocationLine(invoiceId, 100m)]);

        await using (var half = CreateContext(_companyA))
        {
            var invoice = await half.SalesInvoices
                .Include(i => i.Lines).Include(i => i.Allocations)
                .FirstAsync(i => i.Id == invoiceId);

            invoice.Status.Should().Be(SalesInvoiceStatus.PartiallyPaid);
            invoice.OutstandingAmount.Should().Be(68m);
        }

        await PayAsync(db, _customer, [new TenderLine(_cash, 68m)], [new AllocationLine(invoiceId, 68m)]);

        await using var fresh = CreateContext(_companyA);

        var settled = await fresh.SalesInvoices
            .Include(i => i.Lines).Include(i => i.Allocations)
            .FirstAsync(i => i.Id == invoiceId);

        settled.Status.Should().Be(SalesInvoiceStatus.Paid);
        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance.Should().Be(0m);
    }

    [Fact]
    public async Task Money_that_arrives_before_the_invoice_is_a_credit_on_the_account()
    {
        // A deposit. It is not lost and not guessed at: it takes the balance negative, which is exactly
        // what "the shop owes them" looks like.
        await using var db = CreateContext(_companyA);

        await PayAsync(db, _customer, [new TenderLine(_cash, 1_000m)], allocations: null);

        await using var fresh = CreateContext(_companyA);

        var payment = await fresh.CustomerPayments.Include(p => p.Methods).Include(p => p.Allocations).SingleAsync();

        payment.UnallocatedAmount.Should().Be(1_000m);

        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance
            .Should().Be(-1_000m, "a negative balance is a credit, not a bug");
    }

    [Fact]
    public async Task A_payment_cannot_settle_more_of_an_invoice_than_it_owes()
    {
        // The over-payment is real money and it belongs on the account as a credit — not hidden inside a
        // document that would then show as more than settled.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 1m, null)));  // 84

        var act = async () => await PayAsync(
            db,
            _customer,
            [new TenderLine(_cash, 200m)],
            [new AllocationLine(invoiceId, 200m)]);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*outstanding*");
    }

    [Fact]
    public async Task One_customers_money_cannot_settle_anothers_debt()
    {
        // Both balances would end up wrong, and the wrong person would be chased.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 1m, null)));

        var act = async () => await PayAsync(
            db,
            _walkIn,
            [new TenderLine(_cash, 84m)],
            [new AllocationLine(invoiceId, 84m)]);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*different customer*");
    }

    [Fact]
    public async Task A_card_payment_without_its_reference_is_refused()
    {
        // Without the slip number the money cannot be matched to the bank statement, and it becomes
        // unreconcilable the moment it lands.
        await using var db = CreateContext(_companyA);

        var act = async () => await PayAsync(db, _customer, [new TenderLine(_card, 100m)], null);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*reference*");
    }

    // --- The till (requirements §22) ----------------------------------------------------------------

    [Fact]
    public async Task Selling_at_the_till_moves_the_goods_bills_them_and_takes_the_money_at_once()
    {
        // One call, one transaction, three documents. At a counter those are a single act.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 1, 1_200m, ["SN-A"]);

        var result = await SellAtCounterAsync(
            db,
            _walkIn,
            lines: [new CounterSaleLine(_laptop, 1m, SerialNumbers: ["SN-A"])],
            methods: [new TenderLine(_cash, 2_000m)]);

        result.Total.Should().Be(1_575m, "1,500 + 5% tax");
        result.Paid.Should().Be(2_000m);
        result.Change.Should().Be(425m, "the customer handed over 2,000 for a 1,575 sale");

        await using var fresh = CreateContext(_companyA);

        // The goods left, the serial went with them, and it is bound to the invoice line that sold it.
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _laptop)).Quantity.Should().Be(0m);

        var serial = await fresh.Serials.FirstAsync(s => s.SerialNumber == "SN-A");
        serial.Status.Should().Be(SerialStatus.Sold);
        serial.SoldInvoiceLineId.Should().NotBeNull();

        // The money the till actually keeps is the sale, not the notes handed over. Recording 2,000 would
        // leave the shop holding money it gave straight back, and the customer in credit for the change.
        var payment = await fresh.CustomerPayments.Include(p => p.Methods).Include(p => p.Allocations).SingleAsync();
        payment.Amount.Should().Be(1_575m);
        payment.UnallocatedAmount.Should().Be(0m);

        var invoice = await fresh.SalesInvoices
            .Include(i => i.Lines).Include(i => i.Allocations)
            .FirstAsync(i => i.Id == result.InvoiceId);

        invoice.Status.Should().Be(SalesInvoiceStatus.Paid);

        (await fresh.Customers.FirstAsync(c => c.Id == _walkIn)).Balance
            .Should().Be(0m, "a walk-in who paid cash owes nothing and is owed nothing");

        // And the ledger still proves itself.
        (await new BalanceAuditor(fresh).AuditAsync()).Agrees.Should().BeTrue();
    }

    [Fact]
    public async Task A_declined_card_leaves_the_laptop_on_the_shelf()
    {
        // The point of doing all three in one transaction. Under-tendering at the till is not a counter
        // sale — it is a credit sale, and it needs an account and somebody to chase.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 1, 1_200m, ["SN-A"]);

        var act = async () => await SellAtCounterAsync(
            db,
            _walkIn,
            lines: [new CounterSaleLine(_laptop, 1m, SerialNumbers: ["SN-A"])],
            methods: [new TenderLine(_cash, 100m)]);   // nowhere near the 1,575 it comes to

        await act.Should().ThrowAsync<DomainException>().WithMessage("*tendered*");

        await using var fresh = CreateContext(_companyA);

        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _laptop)).Quantity
            .Should().Be(1m, "the laptop never left");

        (await fresh.Serials.FirstAsync()).Status.Should().Be(SerialStatus.InStock);
        (await fresh.Deliveries.AnyAsync()).Should().BeFalse();
        (await fresh.SalesInvoices.AnyAsync()).Should().BeFalse("no invoice is chasing anybody for it");
        (await fresh.CustomerPayments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task A_discount_at_the_till_reaches_the_bill()
    {
        // The haggle at the counter has to survive onto the invoice, or the customer is charged the list
        // price they just talked the salesperson out of.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var result = await SellAtCounterAsync(
            db,
            _walkIn,
            lines: [new CounterSaleLine(_cable, 1m, DiscountPercent: 10m)],
            methods: [new TenderLine(_cash, 100m)]);

        // 80 − 10% = 72 net; 5% tax = 3.60; total 75.60. Note the tax is charged on the discounted net.
        result.Total.Should().Be(75.60m);
        result.Change.Should().Be(24.40m);

        await using var fresh = CreateContext(_companyA);

        var line = await fresh.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == result.InvoiceId);

        line.DiscountPercent.Should().Be(10m);
        line.NetTotal.Should().Be(72m);
        line.TaxAmount.Should().Be(3.60m);
    }

    [Fact]
    public async Task The_invoice_list_reports_what_is_still_owed()
    {
        // The screen that takes a payment reads this. If the query forgot to load the allocations, every
        // invoice would come back reading as fully unpaid — and the shop would chase customers who had
        // already paid, using a figure that looked entirely authoritative.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 2m, null)));  // 168
        await PayAsync(db, _customer, [new TenderLine(_cash, 100m)], [new AllocationLine(invoiceId, 100m)]);

        await using var fresh = CreateContext(_companyA);

        var page = await new GetInvoicesQueryHandler(fresh)
            .Handle(new GetInvoicesQuery(), CancellationToken.None);

        var invoice = page.Items.Single();

        invoice.Total.Should().Be(168m);
        invoice.PaidAmount.Should().Be(100m);
        invoice.OutstandingAmount.Should().Be(68m);
        invoice.Status.Should().Be(SalesInvoiceStatus.PartiallyPaid);
    }

    // --- Returns and credit notes (requirements §24) -------------------------------------------------

    [Fact]
    public async Task A_returned_laptop_comes_back_but_not_onto_the_shelf()
    {
        // The serial goes to Returned, not InStock. A machine that came back is inspected before it is
        // sold to somebody else — and quantities alone cannot express "here, but not fit to sell", which
        // is exactly why serials exist.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 1, 1_200m, ["SN-A"]);

        var deliveryId = await DeliverDirectAsync(db, _customer, (_laptop, 1m, new[] { "SN-A" }));
        var invoiceId = await InvoiceAsync(db, deliveryId);
        await PayAsync(db, _customer, [new TenderLine(_cash, 1_575m)], [new AllocationLine(invoiceId, 1_575m)]);

        var invoiceLine = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceId);

        await CreditAsync(
            db,
            invoiceId,
            RefundMethod.CashRefund,
            new ReturnLine(invoiceLine.Id, 1m, SerialNumbers: ["SN-A"]));

        await using var fresh = CreateContext(_companyA);

        // The stock is back — at the cost it left at, not at today's average.
        var balance = await fresh.StockBalances.SingleAsync(b => b.ProductId == _laptop);
        balance.Quantity.Should().Be(1m);
        balance.AverageCost.Should().Be(1_200m);

        var serial = await fresh.Serials.FirstAsync(s => s.SerialNumber == "SN-A");
        serial.Status.Should().Be(SerialStatus.Returned, "not InStock — it has not been inspected yet");

        // Cash went back, so the customer neither owes nor is owed.
        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance
            .Should().Be(0m, "they paid 1,575 and were refunded 1,575");

        // And the ledger still proves itself after stock came back in.
        (await new BalanceAuditor(fresh).AuditAsync()).Agrees.Should().BeTrue();
    }

    [Fact]
    public async Task Cash_cannot_be_refunded_for_an_invoice_that_was_never_paid()
    {
        // It would hand the customer the shop's own money — and they would still owe for the goods they
        // kept. Offsetting against the balance is the right answer, and the error says so.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 1m, null)));
        var line = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceId);

        var act = async () => await CreditAsync(db, invoiceId, RefundMethod.CashRefund, new ReturnLine(line.Id, 1m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*never arrived*");
    }

    [Fact]
    public async Task Crediting_an_unpaid_invoice_simply_reduces_what_the_customer_owes()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 2m, null)));  // 168
        var line = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceId);

        await CreditAsync(db, invoiceId, RefundMethod.OffsetAgainstBalance, new ReturnLine(line.Id, 1m));  // 84 back

        await using var fresh = CreateContext(_companyA);

        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance
            .Should().Be(84m, "they owed 168 and gave one of the two cables back");

        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).Quantity.Should().Be(9m);
    }

    [Fact]
    public async Task A_refund_taken_as_store_credit_is_a_ledger_entry_and_not_a_balance()
    {
        // "Why do I have 84 credit?" has an answer only if every issue is a row.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 1m, null)));
        await PayAsync(db, _customer, [new TenderLine(_cash, 84m)], [new AllocationLine(invoiceId, 84m)]);

        var line = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceId);

        await CreditAsync(db, invoiceId, RefundMethod.StoreCredit, new ReturnLine(line.Id, 1m));

        await using var fresh = CreateContext(_companyA);

        var entries = await fresh.StoreCreditEntries.Where(e => e.CustomerId == _customer).ToListAsync();

        entries.Should().ContainSingle();
        entries.Sum(e => e.Amount).Should().Be(84m);
        entries.Single().Reason.Should().Contain("CN-");

        // The credit lives in its own ledger. It must NOT also sit on the balance, or the shop would owe
        // the customer twice — once as a negative balance and once as credit.
        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance
            .Should().Be(0m, "the credit is in the store-credit ledger, not double-counted on the balance");
    }

    [Fact]
    public async Task Store_credit_can_be_spent_and_cannot_be_overspent()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        // Buy one, pay for it, bring it back for store credit → 84 of credit.
        var firstInvoice = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 1m, null)));
        await PayAsync(db, _customer, [new TenderLine(_cash, 84m)], [new AllocationLine(firstInvoice, 84m)]);

        var firstLine = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == firstInvoice);
        await CreditAsync(db, firstInvoice, RefundMethod.StoreCredit, new ReturnLine(firstLine.Id, 1m));

        // Now buy two (168) and try to pay entirely from 84 of credit.
        var secondInvoice = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 2m, null)));

        var tooMuch = async () => await PayAsync(
            db,
            _customer,
            [new TenderLine(_storeCredit, 168m)],
            [new AllocationLine(secondInvoice, 168m)]);

        await tooMuch.Should().ThrowAsync<DomainException>().WithMessage("*holds 84*");

        // Spend the 84 that is really there, and pay the rest in cash.
        await PayAsync(
            db,
            _customer,
            [new TenderLine(_storeCredit, 84m), new TenderLine(_cash, 84m)],
            [new AllocationLine(secondInvoice, 168m)]);

        await using var fresh = CreateContext(_companyA);

        (await fresh.StoreCreditEntries.Where(e => e.CustomerId == _customer).SumAsync(e => e.Amount))
            .Should().Be(0m, "the credit was issued and then spent — the ledger nets to nothing");

        var invoice = await fresh.SalesInvoices
            .Include(i => i.Lines).Include(i => i.Allocations)
            .FirstAsync(i => i.Id == secondInvoice);

        invoice.Status.Should().Be(SalesInvoiceStatus.Paid);
    }

    [Fact]
    public async Task A_serial_cannot_be_returned_against_an_invoice_that_did_not_sell_it()
    {
        // Otherwise a customer could return a machine bought elsewhere and be refunded this invoice's
        // price for it.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _laptop, 2, 1_200m, ["SN-A", "SN-B"]);

        var invoiceA = await InvoiceAsync(
            db,
            await DeliverDirectAsync(db, _customer, (_laptop, 1m, new[] { "SN-A" })));

        await InvoiceAsync(db, await DeliverDirectAsync(db, _walkIn, (_laptop, 1m, new[] { "SN-B" })));

        var lineA = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceA);

        // SN-B belongs to the walk-in's invoice, not this one.
        var act = async () => await CreditAsync(
            db,
            invoiceA,
            RefundMethod.OffsetAgainstBalance,
            new ReturnLine(lineA.Id, 1m, SerialNumbers: ["SN-B"]));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*not sold on this invoice line*");
    }

    [Fact]
    public async Task More_cannot_be_credited_than_was_sold()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 2m, null)));
        var line = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceId);

        await CreditAsync(db, invoiceId, RefundMethod.OffsetAgainstBalance, new ReturnLine(line.Id, 1m));

        // One already came back. Asking for two more would refund three of the two that were sold — and,
        // because it restocks, conjure a cable out of a refund.
        var act = async () => await CreditAsync(
            db, invoiceId, RefundMethod.OffsetAgainstBalance, new ReturnLine(line.Id, 2m));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*nobody bought*");
    }

    [Fact]
    public async Task Faulty_goods_are_refunded_without_being_put_back_on_the_shelf()
    {
        // The money goes back; the stock does not come in. Booking a broken laptop into sellable stock
        // would simply sell it to the next customer.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, _cable, 10, 40m);

        var invoiceId = await InvoiceAsync(db, await DeliverDirectAsync(db, _customer, (_cable, 1m, null)));
        var line = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceId);

        await CreditAsync(
            db,
            invoiceId,
            RefundMethod.OffsetAgainstBalance,
            new ReturnLine(line.Id, 1m, Restock: false));

        await using var fresh = CreateContext(_companyA);

        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).Quantity
            .Should().Be(9m, "the cable was faulty — it is not back on the shelf");

        (await fresh.Customers.FirstAsync(c => c.Id == _customer)).Balance
            .Should().Be(0m, "but they were still credited for it");
    }

    // --- Discount approval (requirements §32) --------------------------------------------------------

    [Fact]
    public async Task Selling_below_the_price_lists_floor_needs_approval()
    {
        // The floor is what a salesperson may not go under on their own authority. Without the approve
        // permission, the sale is refused — silently accepting it would be the giveaway the floor exists
        // to stop, and nobody would ever know it happened.
        await using var db = CreateContext(_companyA);

        await GiveCableAFloorAsync(db, floor: 70m);
        await ReceiveAsync(db, _cable, 10, 40m);

        var act = async () => await SellAtCounterAsync(
            db,
            _walkIn,
            lines: [new CounterSaleLine(_cable, 1m, UnitPrice: 60m)],   // under the 70 floor
            methods: [new TenderLine(_cash, 100m)],
            mayApproveDiscounts: false);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*approval*");

        await using var fresh = CreateContext(_companyA);

        (await fresh.SalesInvoices.AnyAsync()).Should().BeFalse();
        (await fresh.StockBalances.SingleAsync(b => b.ProductId == _cable)).Quantity
            .Should().Be(10m, "the goods never left — the whole sale was refused");
    }

    [Fact]
    public async Task A_manager_may_authorise_a_price_below_the_floor_and_is_recorded_as_having_done_so()
    {
        // The question anyone asks later is not "was this approved?" but "who approved this?".
        await using var db = CreateContext(_companyA);

        await GiveCableAFloorAsync(db, floor: 70m);
        await ReceiveAsync(db, _cable, 10, 40m);

        var result = await SellAtCounterAsync(
            db,
            _walkIn,
            lines: [new CounterSaleLine(_cable, 1m, UnitPrice: 60m)],
            methods: [new TenderLine(_cash, 100m)],
            mayApproveDiscounts: true);

        result.Total.Should().Be(63m, "60 + 5% tax");

        await using var fresh = CreateContext(_companyA);

        var line = await fresh.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == result.InvoiceId);

        line.UnitPrice.Should().Be(60m);
        line.PriceSource.Should().Contain("Manual price");
    }

    [Fact]
    public async Task A_price_at_or_above_the_floor_needs_nobody()
    {
        await using var db = CreateContext(_companyA);

        await GiveCableAFloorAsync(db, floor: 70m);
        await ReceiveAsync(db, _cable, 10, 40m);

        var result = await SellAtCounterAsync(
            db,
            _walkIn,
            lines: [new CounterSaleLine(_cable, 1m, UnitPrice: 70m)],   // exactly on the floor
            methods: [new TenderLine(_cash, 100m)],
            mayApproveDiscounts: false);

        result.Total.Should().Be(73.50m);
    }

    // --- Fixture ------------------------------------------------------------------------------------

    /// <summary>Puts the cable on a price list with a floor, which is what makes a discount approvable.</summary>
    private async Task GiveCableAFloorAsync(ApplicationDbContext db, decimal floor)
    {
        var tier = new PriceTier { CompanyId = _companyA, Name = "Retail", IsDefault = true };
        db.PriceTiers.Add(tier);
        await db.SaveChangesAsync();

        var list = new PriceList
        {
            CompanyId = _companyA,
            Name = "Retail 2026",
            PriceTierId = tier.Id,
            CurrencyCode = "AED",
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        db.PriceLists.Add(list);
        await db.SaveChangesAsync();

        db.PriceListItems.Add(new PriceListItem
        {
            CompanyId = _companyA,
            PriceListId = list.Id,
            ProductId = _cable,
            UnitPrice = 80m,
            MinimumPrice = floor
        });

        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreditAsync(
        ApplicationDbContext db,
        Guid invoiceId,
        RefundMethod refund,
        params ReturnLine[] lines) =>
        await new IssueCreditNoteCommandHandler(
                db, Tenant(), Ledger(db), Accounts(db), Numbers(db), new StubClock())
            .Handle(
                new IssueCreditNoteCommand(
                    SalesInvoiceId: invoiceId,
                    Lines: lines,
                    Refund: refund,
                    Reason: "Customer changed their mind",
                    WarehouseId: _warehouseOfA,

                    // Only a cash or bank refund hands money back, and only that needs an account to hand
                    // it back out of. An offset moves no money and a store credit is a promise.
                    RefundFromAccountId: refund is RefundMethod.CashRefund or RefundMethod.BankRefund
                        ? _till
                        : null),
                CancellationToken.None);

    private async Task<Guid> PayAsync(
        ApplicationDbContext db,
        Guid customerId,
        IReadOnlyCollection<TenderLine> methods,
        IReadOnlyCollection<AllocationLine>? allocations) =>
        await new RecordPaymentCommandHandler(db, Tenant(), Numbers(db), Accounts(db), new StubClock())
            .Handle(
                new RecordPaymentCommand(
                    CustomerId: customerId,
                    BranchId: _branchOfA,
                    Methods: methods,
                    Allocations: allocations),
                CancellationToken.None);

    private async Task<CounterSaleResult> SellAtCounterAsync(
        ApplicationDbContext db,
        Guid customerId,
        IReadOnlyCollection<CounterSaleLine> lines,
        IReadOnlyCollection<TenderLine> methods,
        bool mayApproveDiscounts = true) =>
        await new SellAtCounterCommandHandler(
                db, Tenant(), Ledger(db), Accounts(db), Pricer(db), Discounts(db, mayApproveDiscounts),
                Numbers(db), new StubClock())
            .Handle(
                new SellAtCounterCommand(
                    CustomerId: customerId,
                    BranchId: _branchOfA,
                    WarehouseId: _warehouseOfA,
                    Lines: lines,
                    Methods: methods),
                CancellationToken.None);

    private static string[] Serials(int count) =>
        Enumerable.Range(1, count).Select(i => $"SN-{i:D3}").ToArray();

    /// <summary>Puts stock on the shelf the way P4 does — through the ledger, at a known cost.</summary>
    private async Task ReceiveAsync(
        ApplicationDbContext db,
        Guid productId,
        decimal quantity,
        decimal unitCost,
        IReadOnlyCollection<string>? serials = null)
    {
        await using var transaction = await db.BeginTransactionAsync();

        await Ledger(db).PostAsync(new StockPosting(
            WarehouseId: _warehouseOfA,
            BranchId: _branchOfA,
            ProductId: productId,
            Type: MovementType.Receipt,
            Quantity: quantity,
            ReferenceType: StockReferenceType.GoodsReceipt,
            UnitCost: unitCost,
            SerialNumbers: serials));

        await transaction.CommitAsync();
    }

    private async Task<Guid> QuoteAsync(
        ApplicationDbContext db,
        Guid customerId,
        params (Guid ProductId, decimal Quantity)[] lines) =>
        await new CreateQuotationCommandHandler(db, Tenant(), Pricer(db), Numbers(db), new StubClock())
            .Handle(
                new CreateQuotationCommand(
                    BranchId: _branchOfA,
                    Lines: lines.Select(l => new QuoteLine(l.ProductId, l.Quantity)).ToList(),
                    CustomerId: customerId),
                CancellationToken.None);

    private async Task AcceptQuotationAsync(ApplicationDbContext db, Guid quotationId) =>
        await new AcceptQuotationCommandHandler(db, new StubClock())
            .Handle(new AcceptQuotationCommand(quotationId), CancellationToken.None);

    private async Task<Guid> ConvertAsync(ApplicationDbContext db, Guid quotationId) =>
        await new ConvertQuotationCommandHandler(db, Numbers(db), new StubClock())
            .Handle(new ConvertQuotationCommand(quotationId, _warehouseOfA), CancellationToken.None);

    private async Task<Guid> OrderAsync(
        ApplicationDbContext db,
        Guid customerId,
        params (Guid ProductId, decimal Quantity)[] lines) =>
        await new CreateSalesOrderCommandHandler(
                db, Tenant(), Pricer(db), Discounts(db), Numbers(db), new StubClock())
            .Handle(
                new CreateSalesOrderCommand(
                    CustomerId: customerId,
                    BranchId: _branchOfA,
                    WarehouseId: _warehouseOfA,
                    Lines: lines.Select(l => new OrderLine(l.ProductId, l.Quantity)).ToList()),
                CancellationToken.None);

    private async Task ConfirmAsync(ApplicationDbContext db, Guid orderId) =>
        await new ConfirmSalesOrderCommandHandler(db, Ledger(db))
            .Handle(new ConfirmSalesOrderCommand(orderId), CancellationToken.None);

    private async Task<Guid> DeliverAsync(
        ApplicationDbContext db,
        Guid orderId,
        params (Guid ProductId, decimal Quantity, string[]? Serials)[] lines) =>
        await new DeliverGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock())
            .Handle(
                new DeliverGoodsCommand(
                    BranchId: _branchOfA,
                    WarehouseId: _warehouseOfA,
                    Lines: lines.Select(l => new DeliverLine(l.ProductId, l.Quantity, SerialNumbers: l.Serials)).ToList(),
                    SalesOrderId: orderId),
                CancellationToken.None);

    private async Task<Guid> DeliverDirectAsync(
        ApplicationDbContext db,
        Guid customerId,
        params (Guid ProductId, decimal Quantity, string[]? Serials)[] lines) =>
        await new DeliverGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock())
            .Handle(
                new DeliverGoodsCommand(
                    BranchId: _branchOfA,
                    WarehouseId: _warehouseOfA,
                    Lines: lines.Select(l => new DeliverLine(l.ProductId, l.Quantity, SerialNumbers: l.Serials)).ToList(),
                    CustomerId: customerId),
                CancellationToken.None);

    private async Task<Guid> InvoiceAsync(ApplicationDbContext db, Guid deliveryId) =>
        await new RaiseInvoiceCommandHandler(
                db, Tenant(), Pricer(db), Discounts(db), Numbers(db), new StubClock())
            .Handle(new RaiseInvoiceCommand(deliveryId), CancellationToken.None);

    private SalesLinePricer Pricer(ApplicationDbContext db) =>
        new(new PriceResolver(db, new StubClock()), new TaxResolver(db, new StubClock()));

    /// <summary>
    /// The real authorizer, with a stubbed permission service — so a test can be the salesperson who may
    /// not discount below the floor, or the manager who may.
    /// </summary>
    private static DiscountAuthorizer Discounts(ApplicationDbContext db, bool mayApprove = true) =>
        new(db, new StubPermissions(mayApprove), new StubUser());

    private StockLedger Ledger(ApplicationDbContext db) =>
        new(
            new ApplicationDbContextAccessor(db),
            db,
            new StubTenant(_companyA),
            new StubClock(),
            new WeightedAverageCosting());

    /// <summary>The real account ledger, as P7 wires it — the door money goes through.</summary>
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

    private sealed class StubPermissions(bool mayApprove) : IPermissionService
    {
        public Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<PermissionGrant>>([]);

        public Task<bool> HasPermissionAsync(string feature, PermissionAction action, CancellationToken ct = default) =>
            Task.FromResult(action != PermissionAction.Approve || mayApprove);

        public Task DemandAsync(string feature, PermissionAction action, CancellationToken ct = default) =>
            Task.CompletedTask;

        public void InvalidateCache(Guid companyUserId)
        {
        }
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
