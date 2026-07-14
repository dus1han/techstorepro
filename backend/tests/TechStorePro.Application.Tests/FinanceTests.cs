using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Finance.Accounts;
using TechStorePro.Application.Finance.Expenses;
using TechStorePro.Application.Finance.Queries;
using TechStorePro.Application.Finance.Services;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Purchasing.Invoices;
using TechStorePro.Application.Purchasing.Payments;
using TechStorePro.Application.Sales.Deliveries;
using TechStorePro.Application.Sales.Invoices;
using TechStorePro.Application.Sales.Payments;
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
/// Cash, bank and expenses (§33, §34), against a real PostgreSQL.
///
/// <b>Every claim this module makes is a claim about the database</b> — a row lock, a SUM, a movement and
/// the document that caused it landing in one transaction — so none of it can be proven against an
/// in-memory provider that would pass while the real till paid out money it did not have.
///
/// The two figures worth watching in this file:
///
/// <list type="number">
/// <item><b>A foreign supplier payment debits the bank by what left the bank</b>, not by what the invoice
///   was booked at. The AED 70 of FX gain from P4 is P&amp;L, not money, and it must not appear in any
///   account — or the shop's bank balance would disagree with the shop's actual bank, for ever.</item>
/// <item><b>Store credit banks nothing.</b> The shop already had that money. A tender line that wrote a
///   cash movement would put the same notes in the drawer twice.</item>
/// </list>
/// </summary>
public class FinanceTests : IAsyncLifetime
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
    private Guid _customer;
    private Guid _supplier;
    private Guid _laptop;

    private Guid _cash;          // pays into the till
    private Guid _transfer;      // pays into/out of the bank
    private Guid _storeCredit;   // pays into nothing, on purpose

    private Guid _till;          // cash, no overdraft — a drawer cannot have one
    private Guid _bank;          // overdraft allowed, as a real bank account may be
    private Guid _rent;

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

        // Zero-rated: the tax arithmetic is proven in SalesTests, and mixing it in here would make every
        // expected figure a number nobody can check in their head.
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

        var supplier = new Supplier
        {
            CompanyId = company.Id,
            Code = "S-1",
            Name = "Shenzhen Components",
            Type = SupplierType.Overseas,
            DefaultCurrency = "USD"
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
            CurrencyCode = "AED",
            AllowsOverdraft = true
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

        var transfer = new PaymentMethod
        {
            CompanyId = company.Id,
            Name = "Bank transfer",
            Kind = PaymentMethodKind.BankTransfer,
            FinancialAccountId = bank.Id,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        // No account, and that is the rule rather than an omission — see PaymentMethod.Validate.
        var storeCredit = new PaymentMethod
        {
            CompanyId = company.Id,
            Name = "Store credit",
            Kind = PaymentMethodKind.StoreCredit,
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        seed.PaymentMethods.AddRange(cash, transfer, storeCredit);

        var rent = new ExpenseCategory { CompanyId = company.Id, Name = "Rent" };
        seed.ExpenseCategories.Add(rent);

        foreach (var (type, prefix) in new[]
                 {
                     (DocumentType.DeliveryNote, "DLV"),
                     (DocumentType.Invoice, "INV"),
                     (DocumentType.Payment, "PAY"),
                     (DocumentType.CreditNote, "CN"),
                     (DocumentType.Expense, "EXP"),
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
        _supplier = supplier.Id;
        _laptop = laptop.Id;
        _cash = cash.Id;
        _transfer = transfer.Id;
        _storeCredit = storeCredit.Id;
        _till = till.Id;
        _bank = bank.Id;
        _rent = rent.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // --- The ledger's guarantees ---------------------------------------------------------------------

    [Fact]
    public async Task Money_cannot_move_outside_a_transaction()
    {
        // The lock the overdraw check depends on is released the moment the SELECT finishes if there is no
        // ambient transaction — so two clerks emptying the same drawer would both pass. Failing loudly
        // beats finding out from the till at closing time. Same rule, same reason, as IStockLedger.
        await using var db = CreateContext(_companyA);

        var act = async () => await Accounts(db).PostAsync(
            new AccountPosting(_till, 100m, AccountTransactionSource.OpeningBalance, "Float"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*inside a transaction*");
    }

    [Fact]
    public async Task A_till_cannot_pay_out_money_it_does_not_hold()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, _till, 300m, AccountTransactionSource.OpeningBalance, "Float");

        await using var transaction = await db.BeginTransactionAsync();

        var act = async () => await Accounts(db).PostAsync(
            new AccountPosting(_till, -500m, AccountTransactionSource.Expense, "Too much"));

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*holds 300*", "the drawer has 300 in it and there is no such thing as negative notes");
    }

    [Fact]
    public async Task A_bank_with_an_overdraft_may_go_below_zero_and_a_till_may_not()
    {
        // The asymmetry is the point: a bank can lend the shop money, and a cash drawer cannot.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, _bank, -5_000m, AccountTransactionSource.Expense, "Paid before the money came in");

        (await Accounts(db).BalanceAsync(_bank)).Should().Be(-5_000m);
    }

    [Fact]
    public async Task Two_payouts_in_one_transaction_cannot_both_spend_the_last_of_the_till()
    {
        // The trap a ledger with no cached balance falls into: the second posting's check reads the
        // database, which does not yet contain the first posting — because nothing has been saved. Without
        // counting its own pending work, a till holding 500 would pay out 500 twice inside one commit.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, _till, 500m, AccountTransactionSource.OpeningBalance, "Float");

        await using var transaction = await db.BeginTransactionAsync();

        var ledger = Accounts(db);

        await ledger.PostAsync(new AccountPosting(_till, -500m, AccountTransactionSource.Expense, "First"));

        var act = async () => await ledger.PostAsync(
            new AccountPosting(_till, -500m, AccountTransactionSource.Expense, "Second"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*The money is not there*");
    }

    [Fact]
    public async Task Banking_the_till_writes_two_movements_and_not_one()
    {
        // The same reasoning as a stock transfer: one row with a "from" and a "to" would make each
        // account's statement depend on the other's, and the money would belong to both ends at once.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, _till, 4_000m, AccountTransactionSource.OpeningBalance, "Takings");

        await new TransferBetweenAccountsCommandHandler(db, Accounts(db))
            .Handle(
                new TransferBetweenAccountsCommand(
                    FromAccountId: _till,
                    ToAccountId: _bank,
                    AmountOut: 3_000m,
                    Description: "Banked"),
                CancellationToken.None);

        (await Accounts(db).BalanceAsync(_till)).Should().Be(1_000m);
        (await Accounts(db).BalanceAsync(_bank)).Should().Be(3_000m);

        var movements = await db.AccountTransactions
            .Where(t => t.Source == AccountTransactionSource.TransferOut
                || t.Source == AccountTransactionSource.TransferIn)
            .ToListAsync();

        movements.Should().HaveCount(2);
        movements.Sum(m => m.Amount).Should().Be(0m, "a transfer creates no money and destroys none");
    }

    [Fact]
    public async Task A_till_cannot_be_banked_beyond_what_is_in_it()
    {
        await using var db = CreateContext(_companyA);

        await PostAsync(db, _till, 1_000m, AccountTransactionSource.OpeningBalance, "Takings");

        var act = async () => await new TransferBetweenAccountsCommandHandler(db, Accounts(db))
            .Handle(
                new TransferBetweenAccountsCommand(_till, _bank, AmountOut: 2_000m),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*The money is not there*");
    }

    // --- Expenses (§34) ------------------------------------------------------------------------------

    [Fact]
    public async Task Recording_an_expense_takes_the_money_out_of_the_account()
    {
        // An expense is recorded and paid in one act. There is no draft and no accrual, because there is
        // no general ledger to accrue into (§45 D3).
        await using var db = CreateContext(_companyA);

        var expenseId = await RecordExpenseAsync(db, 12_000m, "July rent", _bank);

        var expense = await db.Expenses.FirstAsync(e => e.Id == expenseId);

        expense.Number.Should().StartWith("EXP-");
        expense.Status.Should().Be(ExpenseStatus.Recorded);
        expense.AmountBase.Should().Be(12_000m);

        (await Accounts(db).BalanceAsync(_bank)).Should().Be(-12_000m);

        var movement = await db.AccountTransactions
            .FirstAsync(t => t.Source == AccountTransactionSource.Expense);

        movement.Amount.Should().Be(-12_000m, "money went out, and the sign is what makes the balance a SUM");
        movement.SourceId.Should().Be(expenseId, "a statement row must walk back to the document behind it");
        movement.SourceNumber.Should().Be(expense.Number);
    }

    [Fact]
    public async Task Cancelling_an_expense_puts_the_money_back_and_leaves_both_rows_standing()
    {
        // Not a delete and not an edit. Editing the amount of a paid expense would silently restate a bank
        // balance that has already been reconciled, and nothing would record that it had happened.
        await using var db = CreateContext(_companyA);

        var expenseId = await RecordExpenseAsync(db, 12_000m, "July rent", _bank);

        await new CancelExpenseCommandHandler(db, Accounts(db), new StubClock())
            .Handle(new CancelExpenseCommand(expenseId, "Paid twice by mistake"), CancellationToken.None);

        var expense = await db.Expenses.FirstAsync(e => e.Id == expenseId);

        expense.Status.Should().Be(ExpenseStatus.Cancelled);
        expense.CancelledReason.Should().Be("Paid twice by mistake");

        (await Accounts(db).BalanceAsync(_bank)).Should().Be(0m, "the money came back");

        var movements = await db.AccountTransactions
            .Where(t => t.SourceId == expenseId)
            .ToListAsync();

        movements.Should().HaveCount(2, "the mistake stays visible next to its reversal — that is the point");
        movements.Sum(m => m.Amount).Should().Be(0m);
    }

    [Fact]
    public async Task A_cancelled_expense_is_not_counted_against_the_shop()
    {
        // The one thing the summary has to get right: the cancellation already gave the money back in the
        // account ledger, so counting the expense as well would charge the shop for a payment it took back.
        await using var db = CreateContext(_companyA);

        var kept = await RecordExpenseAsync(db, 12_000m, "July rent", _bank);
        var undone = await RecordExpenseAsync(db, 900m, "Courier, booked twice", _bank);

        await new CancelExpenseCommandHandler(db, Accounts(db), new StubClock())
            .Handle(new CancelExpenseCommand(undone, "Duplicate"), CancellationToken.None);

        var summary = await new GetExpenseSummaryQueryHandler(db, new StubClock())
            .Handle(
                new GetExpenseSummaryQuery(From: Today.AddMonths(-1), To: Today.AddDays(1)),
                CancellationToken.None);

        summary.TotalBase.Should().Be(12_000m);
        summary.Categories.Should().ContainSingle().Which.CategoryName.Should().Be("Rent");

        kept.Should().NotBe(undone);
    }

    [Fact]
    public async Task An_expense_cannot_empty_a_till_that_is_already_empty()
    {
        await using var db = CreateContext(_companyA);

        var act = async () => await RecordExpenseAsync(db, 500m, "Parking", _till);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*The money is not there*");
    }

    // --- The money paths that already existed ---------------------------------------------------------

    [Fact]
    public async Task A_customer_payment_lands_in_the_till()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, 2, unitCost: 600m);
        var invoiceId = await SellAsync(db, quantity: 1);

        await PayAsync(db, [new TenderLine(_cash, 1_000m)], [new AllocationLine(invoiceId, 1_000m)]);

        (await Accounts(db).BalanceAsync(_till)).Should().Be(1_000m);
    }

    [Fact]
    public async Task Cash_and_card_on_one_sale_land_in_two_different_accounts()
    {
        // The tender lines exist because one sale is settled two ways (§23). P7's addition is that the two
        // halves of that money are in two physically different places, and the cash position has to say so.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, 2, unitCost: 600m);
        var invoiceId = await SellAsync(db, quantity: 1);

        await PayAsync(
            db,
            [new TenderLine(_cash, 400m), new TenderLine(_transfer, 600m)],
            [new AllocationLine(invoiceId, 1_000m)]);

        (await Accounts(db).BalanceAsync(_till)).Should().Be(400m);
        (await Accounts(db).BalanceAsync(_bank)).Should().Be(600m);
    }

    [Fact]
    public async Task Spending_store_credit_puts_nothing_in_the_till()
    {
        // <b>The trap.</b> A customer tendering 1,000 of store credit hands over no money — the shop took
        // that money when the goods came back, and it is in the drawer already. A tender line that wrote a
        // cash movement would count the same notes twice, and the till would come up over by exactly the
        // credit the shop had issued.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, 2, unitCost: 600m);

        // The customer buys, pays cash, returns the goods and takes store credit. 1,000 is now in the till
        // and 1,000 of credit is on their account.
        var firstInvoice = await SellAsync(db, quantity: 1);
        await PayAsync(db, [new TenderLine(_cash, 1_000m)], [new AllocationLine(firstInvoice, 1_000m)]);

        var line = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == firstInvoice);

        await new IssueCreditNoteCommandHandler(
                db, Tenant(), Ledger(db), Accounts(db), Numbers(db), new StubClock())
            .Handle(
                new IssueCreditNoteCommand(
                    SalesInvoiceId: firstInvoice,
                    Lines: [new ReturnLine(line.Id, 1)],
                    Refund: RefundMethod.StoreCredit,
                    Reason: "Changed their mind",
                    WarehouseId: _warehouseOfA),
                CancellationToken.None);

        (await Accounts(db).BalanceAsync(_till))
            .Should().Be(1_000m, "a store-credit refund hands back no money — the shop keeps it");

        // Now they spend the credit on a second laptop.
        var secondInvoice = await SellAsync(db, quantity: 1);
        await PayAsync(db, [new TenderLine(_storeCredit, 1_000m)], [new AllocationLine(secondInvoice, 1_000m)]);

        (await Accounts(db).BalanceAsync(_till))
            .Should().Be(1_000m, "spending credit moves no money: the till holds what it held before");

        (await db.AccountTransactions.CountAsync(t => t.Source == AccountTransactionSource.CustomerPayment))
            .Should().Be(1, "only the first payment was money");
    }

    [Fact]
    public async Task A_cash_refund_takes_the_money_out_of_the_drawer_it_came_into()
    {
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, 2, unitCost: 600m);
        var invoiceId = await SellAsync(db, quantity: 1);

        await PayAsync(db, [new TenderLine(_cash, 1_000m)], [new AllocationLine(invoiceId, 1_000m)]);

        var line = await db.SalesInvoiceLines.FirstAsync(l => l.SalesInvoiceId == invoiceId);

        await new IssueCreditNoteCommandHandler(
                db, Tenant(), Ledger(db), Accounts(db), Numbers(db), new StubClock())
            .Handle(
                new IssueCreditNoteCommand(
                    SalesInvoiceId: invoiceId,
                    Lines: [new ReturnLine(line.Id, 1)],
                    Refund: RefundMethod.CashRefund,
                    Reason: "Faulty",
                    WarehouseId: _warehouseOfA,
                    RefundFromAccountId: _till),
                CancellationToken.None);

        (await Accounts(db).BalanceAsync(_till)).Should().Be(0m, "the notes went back over the counter");

        var customer = await db.Customers.FirstAsync(c => c.Id == _customer);
        customer.Balance.Should().Be(0m, "net zero: the credit note took the debt off and the refund put it back");
    }

    [Fact]
    public async Task A_foreign_supplier_payment_debits_the_bank_by_what_left_the_bank_not_by_what_the_invoice_was_booked_at()
    {
        // <b>The arithmetic of this slice.</b> A USD 1,000 invoice booked at 3.67 is a debt of AED 3,670,
        // and AED 3,670 is what comes off the supplier's balance — that is P4, and it is what makes the
        // supplier reach exactly zero. But the bank hands over AED 3,600, because that is the rate on the
        // day, and AED 3,600 is what leaves the account.
        //
        // The AED 70 is a realised FX gain. It is P&L, and it is not money: nobody can point at it, and it
        // never entered or left any account. Debit the bank by the booked 3,670 — the number that balances
        // so neatly against the supplier, and which is sitting right there in the handler — and the shop's
        // bank account in this system would disagree with the shop's actual bank by 70 dirhams, for ever,
        // on every foreign bill it ever paid.
        await using var db = CreateContext(_companyA);

        var invoiceId = await new RecordSupplierInvoiceCommandHandler(db, Numbers(db), new StubClock())
            .Handle(
                new RecordSupplierInvoiceCommand(
                    SupplierId: _supplier,
                    BranchId: _branchOfA,
                    SupplierReference: "SH-1",
                    Lines: [new SupplierInvoiceLineInput("Components", 1m, 1_000m)],
                    CurrencyCode: "USD",
                    ExchangeRate: 3.67m,
                    DueAt: Today.AddDays(30),
                    Post: true),
                CancellationToken.None);

        await new PaySupplierCommandHandler(db, Tenant(), Accounts(db), Numbers(db), new StubClock())
            .Handle(
                new PaySupplierCommand(
                    SupplierId: _supplier,
                    BranchId: _branchOfA,
                    PaymentMethodId: _transfer,
                    Amount: 1_000m,
                    Allocations: [new SupplierAllocationLine(invoiceId, 1_000m)],
                    CurrencyCode: "USD",
                    ExchangeRate: 3.60m),
                CancellationToken.None);

        var supplier = await db.Suppliers.FirstAsync(s => s.Id == _supplier);
        supplier.Balance.Should().Be(0m, "the debt was booked at 3.67 and is discharged at 3.67");

        (await Accounts(db).BalanceAsync(_bank))
            .Should().Be(-3_600m, "AED 3,600 is what the bank actually handed over — not the booked 3,670");

        var gain = await db.SupplierPaymentAllocations.SumAsync(a => a.Amount * (a.InvoiceExchangeRate - a.PaymentExchangeRate));
        gain.Should().Be(70m, "and the 70 is a gain, which is P&L and not money");
    }

    // --- The cash position (§33) ----------------------------------------------------------------------

    [Fact]
    public async Task The_cash_position_is_the_sum_of_the_movements_and_there_is_no_cache_to_disagree_with_it()
    {
        // Note what this test does NOT assert: a variance. The receivables and payables reports of slice 1
        // carry one, because Customer.Balance and Supplier.Balance are hand-maintained caches that had
        // never been checked against the documents beneath them. An account has no stored balance at all —
        // so the cash position IS the sum of the movements rather than a claim about them, and there is
        // nothing it could drift away from.
        await using var db = CreateContext(_companyA);

        await ReceiveAsync(db, 2, unitCost: 600m);
        var invoiceId = await SellAsync(db, quantity: 1);

        await PayAsync(db, [new TenderLine(_cash, 1_000m)], [new AllocationLine(invoiceId, 1_000m)]);
        await RecordExpenseAsync(db, 12_000m, "July rent", _bank);

        await new TransferBetweenAccountsCommandHandler(db, Accounts(db))
            .Handle(new TransferBetweenAccountsCommand(_till, _bank, AmountOut: 600m), CancellationToken.None);

        var position = await new GetCashPositionQueryHandler(db, Tenant(), new StubClock())
            .Handle(new GetCashPositionQuery(), CancellationToken.None);

        position.BaseCurrency.Should().Be("AED");
        position.CashTotalBase.Should().Be(400m, "1,000 taken, 600 banked");
        position.BankTotalBase.Should().Be(-11_400m, "600 banked, 12,000 of rent paid");
        position.TotalBase.Should().Be(-11_000m);

        // And it agrees, account by account, with the ledger it was summed from.
        foreach (var account in position.Accounts)
        {
            var fromLedger = await Accounts(db).BalanceAsync(account.AccountId);
            account.Balance.Should().Be(fromLedger);
        }
    }

    [Fact]
    public async Task A_statement_opens_with_everything_before_the_window_not_with_the_opening_balance_row()
    {
        // A statement for July opens with what was there on the 1st of July, which includes every movement
        // since the account was created. Reading the literal OpeningBalance row instead would show a
        // statement that starts from the day the shop was founded, whatever dates were asked for.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, _bank, 10_000m, AccountTransactionSource.OpeningBalance, "Float", Today.AddDays(-60));
        await RecordExpenseAsync(db, 2_000m, "May rent", _bank, Today.AddDays(-40));
        await RecordExpenseAsync(db, 3_000m, "July rent", _bank, Today.AddDays(-2));

        var statement = await new GetAccountStatementQueryHandler(db, new StubClock())
            .Handle(
                new GetAccountStatementQuery(_bank, From: Today.AddDays(-10), To: Today),
                CancellationToken.None);

        statement.OpeningBalance.Should().Be(8_000m, "10,000 in, 2,000 of May rent out — all before the window");
        statement.Rows.Should().ContainSingle("only July's rent falls inside it");
        statement.Rows.Single().Out.Should().Be(3_000m);
        statement.ClosingBalance.Should().Be(5_000m);
    }

    [Fact]
    public async Task An_account_holding_money_cannot_be_closed()
    {
        // Closing it would drop the money out of the cash position while it was still, physically, in the
        // drawer. The same reasoning that stops P2 retiring a customer who still owes.
        await using var db = CreateContext(_companyA);

        await PostAsync(db, _till, 500m, AccountTransactionSource.OpeningBalance, "Float");

        var act = async () => await new UpdateAccountCommandHandler(db, Accounts(db))
            .Handle(
                new UpdateAccountCommand(_till, Name: "Till", IsActive: false),
                CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*still holds 500*");
    }

    [Fact]
    public async Task Store_credit_cannot_be_pointed_at_an_account()
    {
        // The rule, on the entity, so no code path can dodge it.
        var method = new PaymentMethod
        {
            Name = "Store credit",
            Kind = PaymentMethodKind.StoreCredit,
            FinancialAccountId = Guid.NewGuid()
        };

        var act = () => method.Validate();

        act.Should().Throw<DomainException>().WithMessage("*already holds it*");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_cash_account_cannot_be_given_an_overdraft()
    {
        // An overdrawn till is not a debt, it is a counting error.
        var account = new FinancialAccount
        {
            Name = "Till",
            Kind = FinancialAccountKind.Cash,
            AllowsOverdraft = true
        };

        var act = () => account.Validate();

        act.Should().Throw<DomainException>().WithMessage("*negative notes*");

        await Task.CompletedTask;
    }

    // --- Helpers --------------------------------------------------------------------------------------

    private static readonly DateTimeOffset Today = new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Puts money in an account through the ledger, inside a transaction, the way everything does.</summary>
    private async Task PostAsync(
        ApplicationDbContext db,
        Guid accountId,
        decimal amount,
        AccountTransactionSource source,
        string description,
        DateTimeOffset? occurredAt = null)
    {
        await using var transaction = await db.BeginTransactionAsync();

        await Accounts(db).PostAsync(
            new AccountPosting(accountId, amount, source, description, OccurredAt: occurredAt));

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<Guid> RecordExpenseAsync(
        ApplicationDbContext db,
        decimal amount,
        string description,
        Guid accountId,
        DateTimeOffset? on = null) =>
        await new RecordExpenseCommandHandler(db, Accounts(db), Numbers(db), new StubClock())
            .Handle(
                new RecordExpenseCommand(
                    ExpenseCategoryId: _rent,
                    BranchId: _branchOfA,
                    FinancialAccountId: accountId,
                    Amount: amount,
                    Description: description,
                    ExpenseDate: on),
                CancellationToken.None);

    private async Task PayAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<TenderLine> methods,
        IReadOnlyCollection<AllocationLine>? allocations) =>
        await new RecordPaymentCommandHandler(db, Tenant(), Numbers(db), Accounts(db), new StubClock())
            .Handle(
                new RecordPaymentCommand(
                    CustomerId: _customer,
                    BranchId: _branchOfA,
                    Methods: methods,
                    Allocations: allocations),
                CancellationToken.None);

    /// <summary>Deliver and invoice, so there is a real debt for the money to settle.</summary>
    private async Task<Guid> SellAsync(ApplicationDbContext db, decimal quantity)
    {
        var deliveryId = await new DeliverGoodsCommandHandler(db, Ledger(db), Numbers(db), new StubClock())
            .Handle(
                new DeliverGoodsCommand(
                    BranchId: _branchOfA,
                    WarehouseId: _warehouseOfA,
                    Lines: [new DeliverLine(_laptop, quantity)],
                    CustomerId: _customer),
                CancellationToken.None);

        return await new RaiseInvoiceCommandHandler(
                db, Tenant(), Pricer(db), Discounts(db), Numbers(db), new StubClock())
            .Handle(new RaiseInvoiceCommand(deliveryId), CancellationToken.None);
    }

    private async Task ReceiveAsync(ApplicationDbContext db, decimal quantity, decimal unitCost)
    {
        await using var transaction = await db.BeginTransactionAsync();

        await Ledger(db).PostAsync(new StockPosting(
            WarehouseId: _warehouseOfA,
            BranchId: _branchOfA,
            ProductId: _laptop,
            Type: MovementType.Receipt,
            Quantity: quantity,
            ReferenceType: StockReferenceType.GoodsReceipt,
            UnitCost: unitCost));

        await db.SaveChangesAsync();
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

    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => Today;
    }
}
