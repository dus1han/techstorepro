using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Purchasing.Invoices;
using TechStorePro.Application.Purchasing.Payments;
using TechStorePro.Application.Reports;
using TechStorePro.Application.Reports.Queries;
using TechStorePro.Application.Sales.Deliveries;
using TechStorePro.Application.Sales.Invoices;
using TechStorePro.Application.Sales.Payments;
using TechStorePro.Application.Sales.Returns;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;
using TechStorePro.Infrastructure.Catalog;
using TechStorePro.Infrastructure.Configuration;
using TechStorePro.Infrastructure.Inventory;
using TechStorePro.Infrastructure.Persistence;
using TechStorePro.Infrastructure.Sales;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// The finance reports, against a real PostgreSQL.
///
/// <b>The assertion that matters in this file is not a bucket total — it is the variance.</b>
/// <c>Customer.Balance</c> and <c>Supplier.Balance</c> are stored decimals, maintained by hand in eleven
/// places between them, with no rebuild path and no invariant test that ever summed the documents back.
/// Their own doc comment says the balance is "a cache of the ledger, and P7's receivables report must be
/// able to prove it". These tests are that proof: in every scenario below — money on account, a credit
/// note that offsets, a credit note that does not, an advance to a supplier, a payable settled at a rate
/// the invoice was never booked at — the report is rebuilt from the documents and asserted to land on the
/// stored balance <em>exactly</em>.
///
/// If someone later adds a twelfth writer of Balance and forgets what it does to the receivable, one of
/// these goes red. That is the whole point of them.
/// </summary>
public class ReportsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("techstorepro_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    /// <summary>The clock every handler in these tests runs on. The ageing is measured from here.</summary>
    private static readonly DateTimeOffset Today = new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

    private Guid _companyA;
    private Guid _branchOfA;
    private Guid _warehouseOfA;
    private Guid _customer;
    private Guid _overTheCounter;
    private Guid _overseasSupplier;
    private Guid _laptop;
    private Guid _cash;

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

        // Zero-rated, deliberately. The tax arithmetic is proven in SalesTests; mixing it in here would only
        // make every expected figure in this file a number nobody can check in their head.
        var noTax = new TaxRate
        {
            CompanyId = company.Id,
            Name = "Zero",
            Percent = 0m,
            IsDefault = true,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        seed.TaxRates.Add(noTax);

        var customer = new Customer
        {
            CompanyId = company.Id,
            Code = "C-1",
            Name = "Omar Trading",
            Type = CustomerType.Corporate,
            CreditLimit = 500_000m,
            PaymentTermDays = 30
        };
        seed.Customers.Add(customer);

        // No terms at all — the counter trade. Their invoices carry no due date, and "no due date" is the
        // case the ageing is most likely to get wrong.
        var overTheCounter = new Customer
        {
            CompanyId = company.Id,
            Code = "C-2",
            Name = "Walk-in",
            Type = CustomerType.Individual,
            PaymentTermDays = 0
        };
        seed.Customers.Add(overTheCounter);

        var supplier = new Supplier
        {
            CompanyId = company.Id,
            Code = "S-1",
            Name = "Shenzhen Components",
            Type = SupplierType.Overseas,
            DefaultCurrency = "USD",
            PaymentTermDays = 30
        };
        seed.Suppliers.Add(supplier);

        var laptop = new Product
        {
            CompanyId = company.Id,
            ItemCode = "LAPTOP",
            Sku = "LAPTOP",
            Name = "Laptop",
            Unit = "each",
            TrackingMode = TrackingMode.None,
            SellingPrice = 1_000m,
            TaxRateId = noTax.Id
        };
        seed.Products.Add(laptop);

        var cash = new PaymentMethod
        {
            CompanyId = company.Id,
            Name = "Cash",
            Kind = PaymentMethodKind.Cash,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        seed.PaymentMethods.Add(cash);

        foreach (var (type, prefix) in new[]
                 {
                     (DocumentType.DeliveryNote, "DLV"),
                     (DocumentType.Invoice, "INV"),
                     (DocumentType.Payment, "PAY"),
                     (DocumentType.CreditNote, "CN"),
                     (DocumentType.GoodsReceipt, "GRN"),
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
        _customer = customer.Id;
        _overTheCounter = overTheCounter.Id;
        _overseasSupplier = supplier.Id;
        _laptop = laptop.Id;
        _cash = cash.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // --- Receivables ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_debt_is_aged_by_how_late_it_is_not_by_how_old_the_invoice_is()
    {
        // Three invoices, all raised long ago, all on thirty-day terms. What separates them is when they
        // fell due — which is the only thing a shop chasing money actually cares about.
        await using var db = CreateContext(_companyA);
        await StockAsync(db, quantity: 10m);

        await SellAsync(db, quantity: 1m, dueAt: Today.AddDays(10));    // not due yet
        await SellAsync(db, quantity: 2m, dueAt: Today.AddDays(-45));   // 45 days late
        await SellAsync(db, quantity: 3m, dueAt: Today.AddDays(-120));  // 120 days late

        var report = await AgeingAsync(db);
        var row = report.Rows.Single();

        row.Current.Should().Be(1_000m);
        row.Days31To60.Should().Be(2_000m);
        row.Days90Plus.Should().Be(3_000m);
        row.Days1To30.Should().Be(0m);
        row.Days61To90.Should().Be(0m);

        row.TotalDue.Should().Be(6_000m);
        row.OpenInvoices.Should().Be(3);
    }

    [Fact]
    public async Task An_invoice_with_no_terms_is_due_on_receipt_and_not_never()
    {
        // A customer with no payment terms gets an invoice with no due date at all. Read as "never due" it
        // would fall out of every bucket, and the ageing would under-report the debt by exactly the sales
        // least likely to ever be paid. It is due on receipt, so it has been late for forty days.
        await using var db = CreateContext(_companyA);
        await StockAsync(db, quantity: 5m);

        await SellAsync(db, quantity: 1m, invoicedAt: Today.AddDays(-40), customerId: _overTheCounter);

        var invoice = await db.SalesInvoices.SingleAsync();
        invoice.DueAt.Should().BeNull("the customer has no terms — the money was owed on the day");

        var report = await AgeingAsync(db);

        report.Invoices.Single().DaysOverdue.Should().Be(40);
        report.Rows.Single().Days31To60.Should().Be(1_000m);
    }

    [Fact]
    public async Task The_receivables_ageing_reproduces_the_customer_balance()
    {
        // The identity, with the two things that pull the invoices and the balance apart: a payment that is
        // only partly allocated, and one that is not allocated at all.
        await using var db = CreateContext(_companyA);
        await StockAsync(db, quantity: 10m);

        var first = await SellAsync(db, quantity: 3m, dueAt: Today.AddDays(-10));   // 3,000
        await SellAsync(db, quantity: 2m, dueAt: Today.AddDays(-70));               // 2,000

        // 1,200 against the first invoice, and 800 that lands on the account with nothing to point at.
        await PayAsync(db, 1_200m, allocations: [new AllocationLine(first, 1_200m)]);
        await PayAsync(db, 800m, allocations: null);

        var report = await AgeingAsync(db);
        var row = report.Rows.Single();

        row.TotalDue.Should().Be(3_800m);        // 1,800 still on the first + 2,000 on the second
        row.Credits.Should().Be(800m);           // the money on account
        row.NetReceivable.Should().Be(3_000m);

        var customer = await db.Customers.SingleAsync(c => c.Id == _customer);
        customer.Balance.Should().Be(3_000m);

        row.StoredBalance.Should().Be(3_000m);
        row.Variance.Should().Be(0m);            // the report proves the balance
    }

    [Fact]
    public async Task An_invoice_settled_by_a_credit_note_stops_being_chased()
    {
        // The one that would have been wrong. An offset credit note takes its total off the balance and
        // writes no allocation, so the invoice keeps its full outstanding and stays Posted for ever. A
        // report that read the invoice alone would chase this customer, in the ninety-day column, for a
        // debt they do not owe — and would do it for the rest of the life of the system.
        await using var db = CreateContext(_companyA);
        await StockAsync(db, quantity: 10m);

        var invoiceId = await SellAsync(db, quantity: 2m, dueAt: Today.AddDays(-120));

        var lineId = await db.SalesInvoiceLines
            .Where(l => l.SalesInvoiceId == invoiceId)
            .Select(l => l.Id)
            .SingleAsync();

        await CreditAsync(db, invoiceId, RefundMethod.OffsetAgainstBalance, new ReturnLine(lineId, 2m));

        var invoice = await db.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Allocations)
            .SingleAsync(i => i.Id == invoiceId);

        // The invoice itself has not moved, and that is the trap.
        invoice.Status.Should().Be(SalesInvoiceStatus.Posted);
        invoice.OutstandingAmount.Should().Be(2_000m);

        var report = await AgeingAsync(db);

        // The report has.
        report.Rows.Should().BeEmpty("the debt was credited away, so there is nothing to age and nobody to chase");

        var customer = await db.Customers.SingleAsync(c => c.Id == _customer);
        customer.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task Store_credit_is_a_memo_and_not_a_reduction_of_the_debt()
    {
        // The mirror of the test above, and the reason only OffsetAgainstBalance is netted. A store-credit
        // note leaves the debt exactly where it was and hands the customer a voucher. They owe 2,000 and
        // they hold 2,000 of credit, and both of those are true at once.
        await using var db = CreateContext(_companyA);
        await StockAsync(db, quantity: 10m);

        var keep = await SellAsync(db, quantity: 2m, dueAt: Today.AddDays(-5));
        var returned = await SellAsync(db, quantity: 2m, dueAt: Today.AddDays(-5));

        var lineId = await db.SalesInvoiceLines
            .Where(l => l.SalesInvoiceId == returned)
            .Select(l => l.Id)
            .SingleAsync();

        await CreditAsync(db, returned, RefundMethod.StoreCredit, new ReturnLine(lineId, 2m));

        var report = await AgeingAsync(db);
        var row = report.Rows.Single();

        row.TotalDue.Should().Be(4_000m, "the store-credit note did not pay either invoice off");
        row.StoreCredit.Should().Be(2_000m);
        row.NetReceivable.Should().Be(4_000m);

        var customer = await db.Customers.SingleAsync(c => c.Id == _customer);
        customer.Balance.Should().Be(4_000m);
        row.Variance.Should().Be(0m);

        keep.Should().NotBe(returned);
    }

    [Fact]
    public async Task A_statement_opens_moves_and_closes_on_the_balance()
    {
        // What a customer gets emailed, and what they will check with a calculator. Opening, plus every
        // line printed, must equal closing — and closing must equal what the system says they owe.
        await using var db = CreateContext(_companyA);
        await StockAsync(db, quantity: 10m);

        // Before the window: an invoice and a payment that together leave 400 owing.
        var old = await SellAsync(db, quantity: 1m, invoicedAt: Today.AddDays(-90), dueAt: Today.AddDays(-60));
        await PayAsync(db, 600m, [new AllocationLine(old, 600m)], paidAt: Today.AddDays(-80));

        // Inside it.
        var recent = await SellAsync(db, quantity: 2m, invoicedAt: Today.AddDays(-20), dueAt: Today.AddDays(10));
        await PayAsync(db, 500m, [new AllocationLine(recent, 500m)], paidAt: Today.AddDays(-5));

        var statement = await new GetCustomerStatementQueryHandler(db, new StubClock()).Handle(
            new GetCustomerStatementQuery(_customer, From: Today.AddDays(-30), To: Today),
            CancellationToken.None);

        statement.OpeningBalance.Should().Be(400m);
        statement.Lines.Should().HaveCount(2);

        var walked = statement.OpeningBalance + statement.Lines.Sum(l => l.Debit - l.Credit);
        walked.Should().Be(statement.ClosingBalance);

        statement.ClosingBalance.Should().Be(1_900m);   // 400 + 2,000 − 500
        statement.Lines.Last().RunningBalance.Should().Be(1_900m);

        var customer = await db.Customers.SingleAsync(c => c.Id == _customer);
        statement.ClosingBalance.Should().Be(customer.Balance);
        statement.Variance.Should().Be(0m);
    }

    // --- Payables ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_foreign_payable_is_valued_at_the_rate_it_was_booked_at()
    {
        // USD 1,000 at 3.67 is a commitment of AED 3,670, and it stays one until it is paid — whatever the
        // rate does in the meantime. Revaluing it at today's spot would book an unrealised gain, which is a
        // concept this system does not have anywhere, and would stop the report tying to the balance.
        await using var db = CreateContext(_companyA);

        await SupplierInvoiceAsync(db, amount: 1_000m, currency: "USD", rate: 3.67m, dueAt: Today.AddDays(-15));

        var report = await PayablesAgeingAsync(db);
        var invoice = report.Invoices.Single();

        invoice.Total.Should().Be(1_000m);
        invoice.CurrencyCode.Should().Be("USD");
        invoice.OutstandingAmount.Should().Be(1_000m, "the supplier will chase you for dollars");
        invoice.OutstandingBase.Should().Be(3_670m, "and the shop owes dirhams");

        var row = report.Rows.Single();
        row.Days1To30.Should().Be(3_670m);

        var supplier = await db.Suppliers.SingleAsync(s => s.Id == _overseasSupplier);
        supplier.Balance.Should().Be(3_670m);
        row.Variance.Should().Be(0m);
    }

    [Fact]
    public async Task Settling_a_foreign_payable_clears_it_even_though_less_money_left_the_bank()
    {
        // The FX case P4 exists to get right, seen from the report. The invoice was booked at 3.67 and paid
        // at 3.60: AED 3,600 left the bank, but AED 3,670 of debt was discharged. The 70 is a realised gain
        // and it is the shop's profit, not a residue left sitting on the supplier — and the payable must go
        // to zero, because the shop does not owe Shenzhen anything any more.
        await using var db = CreateContext(_companyA);

        var invoiceId = await SupplierInvoiceAsync(
            db, amount: 1_000m, currency: "USD", rate: 3.67m, dueAt: Today.AddDays(-15));

        await PaySupplierAsync(
            db, amount: 1_000m, currency: "USD", rate: 3.60m,
            allocations: [new SupplierAllocationLine(invoiceId, 1_000m)]);

        var report = await PayablesAgeingAsync(db);

        report.Rows.Should().BeEmpty("the debt is discharged");

        var supplier = await db.Suppliers.SingleAsync(s => s.Id == _overseasSupplier);
        supplier.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task The_payables_ageing_reproduces_the_supplier_balance_with_an_advance_on_it()
    {
        // An advance settles no invoice, so it has no invoice rate to be measured against: it comes off the
        // balance at what it actually cost. The report has to net it the same way or it cannot reconcile.
        await using var db = CreateContext(_companyA);

        await SupplierInvoiceAsync(db, amount: 1_000m, currency: "USD", rate: 3.67m, dueAt: Today.AddDays(-45));
        await PaySupplierAsync(db, amount: 500m, currency: "USD", rate: 3.60m, allocations: null);

        var report = await PayablesAgeingAsync(db);
        var row = report.Rows.Single();

        row.TotalDue.Should().Be(3_670m);
        row.Advances.Should().Be(1_800m);          // 500 × 3.60
        row.NetPayable.Should().Be(1_870m);

        var supplier = await db.Suppliers.SingleAsync(s => s.Id == _overseasSupplier);
        supplier.Balance.Should().Be(1_870m);
        row.Variance.Should().Be(0m);
    }

    [Fact]
    public async Task A_draft_supplier_invoice_owes_nothing_and_is_not_aged()
    {
        // A draft is a bill somebody is still checking against the receipt. It never hit the balance, so it
        // must not appear in the payables — a report that aged drafts would have the shop paying twice.
        await using var db = CreateContext(_companyA);

        await SupplierInvoiceAsync(
            db, amount: 1_000m, currency: "USD", rate: 3.67m, dueAt: Today.AddDays(-45), post: false);

        var report = await PayablesAgeingAsync(db);

        report.Rows.Should().BeEmpty();
        report.Invoices.Should().BeEmpty();

        var supplier = await db.Suppliers.SingleAsync(s => s.Id == _overseasSupplier);
        supplier.Balance.Should().Be(0m);
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private async Task<ReceivablesAgeingDto> AgeingAsync(ApplicationDbContext db) =>
        await new GetReceivablesAgeingQueryHandler(db, new StubClock()).Handle(
            new GetReceivablesAgeingQuery(), CancellationToken.None);

    private async Task<PayablesAgeingDto> PayablesAgeingAsync(ApplicationDbContext db) =>
        await new GetPayablesAgeingQueryHandler(db, new StubClock()).Handle(
            new GetPayablesAgeingQuery(), CancellationToken.None);

    /// <summary>Sells laptops on credit and returns the invoice — the shop's ordinary way of being owed money.</summary>
    private async Task<Guid> SellAsync(
        ApplicationDbContext db,
        decimal quantity,
        DateTimeOffset? dueAt = null,
        DateTimeOffset? invoicedAt = null,
        Guid? customerId = null)
    {
        var buyer = customerId ?? _customer;

        var deliveryId = await new DeliverGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock())
            .Handle(
                new DeliverGoodsCommand(
                    BranchId: _branchOfA,
                    WarehouseId: _warehouseOfA,
                    Lines: [new DeliverLine(_laptop, quantity)],
                    CustomerId: buyer),
                CancellationToken.None);

        return await new RaiseInvoiceCommandHandler(
                db, Tenant(), Pricer(db), Discounts(db), Numbers(db), new StubClock())
            .Handle(
                // Passing the due date explicitly is what lets a test place an invoice in a bucket. Leave it
                // null and the handler derives it from the customer's terms — or leaves it null too, where
                // they have none, which is the "due on receipt" case one of these tests is about.
                new RaiseInvoiceCommand(DeliveryId: deliveryId, InvoicedAt: invoicedAt, DueAt: dueAt),
                CancellationToken.None);
    }

    private async Task PayAsync(
        ApplicationDbContext db,
        decimal amount,
        IReadOnlyCollection<AllocationLine>? allocations,
        DateTimeOffset? paidAt = null) =>
        await new RecordPaymentCommandHandler(db, Tenant(), Numbers(db), new StubClock())
            .Handle(
                new RecordPaymentCommand(
                    CustomerId: _customer,
                    BranchId: _branchOfA,
                    Methods: [new TenderLine(_cash, amount)],
                    Allocations: allocations,
                    PaidAt: paidAt),
                CancellationToken.None);

    private async Task CreditAsync(
        ApplicationDbContext db,
        Guid invoiceId,
        RefundMethod refund,
        params ReturnLine[] lines) =>
        await new IssueCreditNoteCommandHandler(db, Tenant(), Ledger(db), Numbers(db), new StubClock())
            .Handle(
                new IssueCreditNoteCommand(
                    SalesInvoiceId: invoiceId,
                    Lines: lines,
                    Refund: refund,
                    Reason: "Returned",
                    WarehouseId: _warehouseOfA),
                CancellationToken.None);

    private async Task<Guid> SupplierInvoiceAsync(
        ApplicationDbContext db,
        decimal amount,
        string currency,
        decimal rate,
        DateTimeOffset dueAt,
        bool post = true) =>
        await new RecordSupplierInvoiceCommandHandler(db, Numbers(db), new StubClock())
            .Handle(
                new RecordSupplierInvoiceCommand(
                    SupplierId: _overseasSupplier,
                    BranchId: _branchOfA,
                    SupplierReference: $"SH-{amount}-{rate}",
                    Lines: [new SupplierInvoiceLineInput("Components", 1m, amount)],
                    CurrencyCode: currency,
                    ExchangeRate: rate,
                    DueAt: dueAt,
                    Post: post),
                CancellationToken.None);

    private async Task PaySupplierAsync(
        ApplicationDbContext db,
        decimal amount,
        string currency,
        decimal rate,
        IReadOnlyCollection<SupplierAllocationLine>? allocations) =>
        await new PaySupplierCommandHandler(db, Numbers(db), new StubClock())
            .Handle(
                new PaySupplierCommand(
                    SupplierId: _overseasSupplier,
                    BranchId: _branchOfA,
                    PaymentMethodId: _cash,
                    Amount: amount,
                    Allocations: allocations,
                    CurrencyCode: currency,
                    ExchangeRate: rate),
                CancellationToken.None);

    /// <summary>Puts stock on the shelf at a known cost, through the ledger, the way P4 does.</summary>
    private async Task StockAsync(ApplicationDbContext db, decimal quantity)
    {
        await using var transaction = await db.BeginTransactionAsync();

        await Ledger(db).PostAsync(new StockPosting(
            WarehouseId: _warehouseOfA,
            BranchId: _branchOfA,
            ProductId: _laptop,
            Type: MovementType.Receipt,
            Quantity: quantity,
            ReferenceType: StockReferenceType.GoodsReceipt,
            UnitCost: 600m));

        await transaction.CommitAsync();
    }

    private SalesLinePricer Pricer(ApplicationDbContext db) =>
        new(new PriceResolver(db, new StubClock()), new TaxResolver(db, new StubClock()));

    private static DiscountAuthorizer Discounts(ApplicationDbContext db) =>
        new(db, new StubPermissions(), new StubUser());

    private StockLedger Ledger(ApplicationDbContext db) =>
        new(
            new ApplicationDbContextAccessor(db),
            db,
            new StubTenant(_companyA),
            new StubClock(),
            new WeightedAverageCosting());

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

    private sealed class StubPermissions : IPermissionService
    {
        public Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<PermissionGrant>>([]);

        public Task<bool> HasPermissionAsync(string feature, PermissionAction action, CancellationToken ct = default) =>
            Task.FromResult(true);

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

    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => Today;
    }
}
