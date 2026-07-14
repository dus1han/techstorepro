using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Reports.Queries;

// What a customer owes, and how late it is.
//
// ---------------------------------------------------------------------------------------------------
// THE THING THAT MAKES THIS REPORT HARD, AND THE IDENTITY THAT MAKES IT TRUE
// ---------------------------------------------------------------------------------------------------
//
// Customer.Balance is a stored decimal, maintained by hand in seven places, and its own doc comment says
// it is "a cache of the ledger, and P7's receivables report must be able to prove it." Proving it is the
// job of this file, and it is not as simple as summing the invoices, because the sum of the invoices is
// *guaranteed* to disagree with the balance. Two things drive a wedge between them, both deliberate:
//
//   1. AN OFFSET CREDIT NOTE MOVES THE BALANCE BUT NOT THE INVOICE. Issuing one takes its total off
//      Customer.Balance and writes no CustomerPaymentAllocation, so the invoice it credits keeps its full
//      OutstandingAmount and stays Posted for ever. A naive ageing would show a fully-credited invoice
//      sitting in the ninety-day column at 100% of its value, forever, while the customer's balance
//      correctly reads zero — and the shop would chase a debt that does not exist.
//
//   2. AN UNALLOCATED PAYMENT MOVES THE BALANCE BUT NOT THE INVOICE EITHER, in the other direction.
//      RecordPaymentCommand deducts the whole tender from the balance; only the allocated slices become
//      allocation rows. Money on account reduces what the customer owes and reduces no invoice.
//
// So the report nets both, per invoice and per customer, and the result is an identity that has to hold:
//
//      Σ (invoice.Outstanding − offset credit notes against it)  −  unallocated payments  =  Customer.Balance
//        └──────────── bucketed, when positive ────────────┘        └──── Credits ────┘
//
// That is asserted as a test. It is the whole point: a receivables report that cannot reproduce the
// balance is a report nobody can trust, and this one either reproduces it or shows the variance.
//
// Only OffsetAgainstBalance is netted against the invoice, and that is not an oversight. A store-credit
// note is net-zero on the balance (the debt stands; the customer holds a voucher, tracked in its own
// ledger and shown here as a memo), and a cash or bank refund is raised only against an invoice that was
// already paid — the money goes back out of the till, the invoice stays settled, the balance does not
// move. Netting either of those against the invoice would make the report disagree with the balance by
// exactly the amount it was trying to explain.
//
// ---------------------------------------------------------------------------------------------------
// WHY THIS MATERIALISES RATHER THAN PROJECTING
// ---------------------------------------------------------------------------------------------------
//
// SalesInvoice.Total is computed from its lines through SalesMath, which rounds away-from-zero at each
// line. It is EF-Ignore()d: there is no column, and it cannot appear in a SQL projection. Re-expressing
// that arithmetic in the query is the trap the purchasing queries already warn about — the rule would
// live in two places, and the day one changed, the report and the invoice would disagree by a fils and
// nobody would know which was right. So the invoices are materialised and totalled by the domain itself.
//
// The read is bounded to *open* documents, not to history: invoices still owing, plus the few that carry
// an offset credit note, plus (only when asOf is backdated) those a later payment settled. Everything
// that stays out — draft, cancelled, and the great mass of invoices paid and done with — is exactly the
// set that contributes nothing to a debt report. What is summed in SQL, and safely, is the plain stored
// decimals: an allocation's amount, a tender's amount, a store-credit entry. Those carry no rounding rule
// to duplicate, so there is nothing to get out of step.

/// <summary>One open invoice, as the ageing sees it.</summary>
public record ReceivablesInvoiceDto(
    Guid InvoiceId,
    string Number,
    Guid CustomerId,
    string CustomerName,
    DateTimeOffset InvoicedAt,

    /// <summary>The invoice's own due date, or its invoice date where no terms were given.</summary>
    DateTimeOffset DueAt,
    int DaysOverdue,
    AgeingBucket Bucket,
    decimal Total,
    decimal PaidAmount,

    /// <summary>Taken off this invoice by an offset credit note — see the note at the top of this file.</summary>
    decimal CreditedAmount,

    /// <summary>Total − paid − credited. Negative means the invoice was over-credited and the shop owes.</summary>
    decimal OutstandingAmount,
    string CurrencyCode);

/// <summary>One customer's debt, aged.</summary>
public record ReceivablesAgeingRowDto(
    Guid CustomerId,
    string CustomerName,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days90Plus,

    /// <summary>The five buckets. Only positive debt lands here.</summary>
    decimal TotalDue,

    /// <summary>
    /// Money held against the customer that no invoice has claimed: payments taken on account, and any
    /// invoice credited past zero. It is a positive number and it comes <em>off</em> the debt.
    /// </summary>
    decimal Credits,

    /// <summary>TotalDue − Credits. What the customer actually owes, and what the balance should say.</summary>
    decimal NetReceivable,

    /// <summary>Customer.Balance — the stored figure this report exists to prove.</summary>
    decimal StoredBalance,

    /// <summary>
    /// NetReceivable − StoredBalance. <b>It must be zero.</b> Anything else is drift in a cache that has
    /// no rebuild path, and the shop wants to know on the day it happens rather than at the year end.
    ///
    /// Null when the report is backdated: Customer.Balance is today's number, and subtracting a historical
    /// position from it would manufacture a difference that means nothing.
    /// </summary>
    decimal? Variance,

    /// <summary>
    /// Vouchers the customer holds. A memo, not a receivable: store credit is net-zero against the balance
    /// when it is issued and comes back as a payment when it is spent. A customer can owe 500 and hold 240
    /// of store credit at the same time, and both numbers are true.
    /// </summary>
    decimal StoreCredit,
    int OpenInvoices,
    DateTimeOffset? OldestDueAt);

public record ReceivablesTotalsDto(
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days90Plus,
    decimal TotalDue,
    decimal Credits,
    decimal NetReceivable,
    decimal StoredBalance,
    decimal? Variance,
    decimal StoreCredit);

public record ReceivablesAgeingDto(
    DateTimeOffset AsOf,
    string CurrencyCode,
    IReadOnlyCollection<ReceivablesAgeingRowDto> Rows,
    ReceivablesTotalsDto Totals,
    IReadOnlyCollection<ReceivablesInvoiceDto> Invoices);

/// <param name="AsOf">The date to age against. Defaults to now. Documents raised after it are ignored.</param>
[RequiresPermission(FeatureCatalog.Receivables, PermissionAction.View)]
public record GetReceivablesAgeingQuery(
    DateTimeOffset? AsOf = null,
    Guid? CustomerId = null,
    Guid? BranchId = null) : IRequest<ReceivablesAgeingDto>;

public class GetReceivablesAgeingQueryHandler
    : IRequestHandler<GetReceivablesAgeingQuery, ReceivablesAgeingDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetReceivablesAgeingQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ReceivablesAgeingDto> Handle(
        GetReceivablesAgeingQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var asOf = request.AsOf ?? now;
        var backdated = asOf < now;

        // What each invoice has been paid, as of the report date. An allocation row carries no date of its
        // own — the payment it belongs to does — so the cutoff is applied to the payment. Plain stored
        // amounts, so this sums in SQL.
        var paidByInvoice = await _db.CustomerPayments
            .Where(p => p.PaidAt <= asOf)
            .SelectMany(p => p.Allocations)
            .GroupBy(a => a.SalesInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Paid = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, cancellationToken);

        // Offset credit notes, per invoice. These are the ones that genuinely reduce the debt. Their totals
        // come off the lines through SalesMath, so they are materialised rather than summed in SQL.
        var offsetCredits = await _db.CreditNotes
            .Include(c => c.Lines)
            .Where(c => c.Status == CreditNoteStatus.Issued
                && c.RefundMethod == RefundMethod.OffsetAgainstBalance
                && c.IssuedAt <= asOf)
            .ToListAsync(cancellationToken);

        var creditByInvoice = offsetCredits
            .GroupBy(c => c.SalesInvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Total));

        // An invoice a payment settled *after* the report date was still open on the date being reported.
        // Only a backdated report can have any, which is why this is not paid for on the common path.
        var settledLater = backdated
            ? await _db.CustomerPayments
                .Where(p => p.PaidAt > asOf)
                .SelectMany(p => p.Allocations)
                .Select(a => a.SalesInvoiceId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : [];

        var creditedIds = creditByInvoice.Keys.ToList();

        var invoicesQuery = _db.SalesInvoices
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Where(i => i.InvoicedAt <= asOf)
            .Where(i => i.Status == SalesInvoiceStatus.Posted
                || i.Status == SalesInvoiceStatus.PartiallyPaid
                || creditedIds.Contains(i.Id)
                || settledLater.Contains(i.Id));

        if (request.CustomerId is { } customerId)
        {
            invoicesQuery = invoicesQuery.Where(i => i.CustomerId == customerId);
        }

        if (request.BranchId is { } branchId)
        {
            invoicesQuery = invoicesQuery.Where(i => i.BranchId == branchId);
        }

        var invoices = await invoicesQuery.ToListAsync(cancellationToken);

        var lines = new List<ReceivablesInvoiceDto>();

        foreach (var invoice in invoices)
        {
            var paid = paidByInvoice.GetValueOrDefault(invoice.Id);
            var credited = creditByInvoice.GetValueOrDefault(invoice.Id);
            var outstanding = invoice.Total - paid - credited;

            if (outstanding == 0m)
            {
                continue;
            }

            var dueAt = Ageing.DueDate(invoice.DueAt, invoice.InvoicedAt);

            lines.Add(new ReceivablesInvoiceDto(
                invoice.Id,
                invoice.Number,
                invoice.CustomerId,
                invoice.Customer.Name,
                invoice.InvoicedAt,
                dueAt,
                Ageing.DaysOverdue(dueAt, asOf),
                Ageing.Bucket(dueAt, asOf),
                invoice.Total,
                paid,
                credited,
                outstanding,
                invoice.CurrencyCode));
        }

        // Money on account: tendered, less whatever was pointed at an invoice.
        var onAccount = (await _db.CustomerPayments
                .Where(p => p.PaidAt <= asOf)
                .Select(p => new
                {
                    p.CustomerId,
                    Tendered = p.Methods.Sum(m => m.Amount),
                    Allocated = p.Allocations.Sum(a => a.Amount)
                })
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.CustomerId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Tendered - p.Allocated));

        var storeCredit = await _db.StoreCreditEntries
            .Where(e => e.OccurredAt <= asOf)
            .GroupBy(e => e.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Balance, cancellationToken);

        // Every customer this report has something to say about: one with an open invoice, one holding money
        // on account, or one whose stored balance is not zero — that last is what surfaces drift in a
        // customer whose documents all net out but whose cached balance does not.
        var customerIds = lines.Select(l => l.CustomerId)
            .Concat(onAccount.Where(x => x.Value != 0m).Select(x => x.Key))
            .Distinct()
            .ToList();

        var customers = await _db.Customers
            .Where(c => customerIds.Contains(c.Id) || c.Balance != 0m)
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var subjects = request.CustomerId is { } only
            ? customers.Keys.Where(id => id == only).ToList()
            : customers.Keys.Union(customerIds).Distinct().ToList();

        var rows = new List<ReceivablesAgeingRowDto>();

        foreach (var id in subjects)
        {
            if (!customers.TryGetValue(id, out var customer))
            {
                continue;
            }

            var theirs = lines.Where(l => l.CustomerId == id).ToList();
            var columns = new AgeingColumns();

            // Positive debt is aged. An over-credited invoice is not a debt at all — it is a credit, and it
            // goes below with the money on account. Ageing it would net it against a genuine ninety-day
            // debt and make the column the shop most needs to see look smaller than it is.
            var credits = onAccount.GetValueOrDefault(id);

            foreach (var line in theirs)
            {
                if (line.OutstandingAmount > 0m)
                {
                    columns.Add(line.Bucket, line.OutstandingAmount);
                }
                else
                {
                    credits += -line.OutstandingAmount;
                }
            }

            var net = columns.TotalDue - credits;

            rows.Add(new ReceivablesAgeingRowDto(
                id,
                customer.Name,
                columns.Current,
                columns.Days1To30,
                columns.Days31To60,
                columns.Days61To90,
                columns.Days90Plus,
                columns.TotalDue,
                credits,
                net,
                customer.Balance,
                backdated ? null : net - customer.Balance,
                storeCredit.GetValueOrDefault(id),
                theirs.Count(l => l.OutstandingAmount > 0m),
                theirs.Where(l => l.OutstandingAmount > 0m)
                    .Select(l => (DateTimeOffset?)l.DueAt)
                    .DefaultIfEmpty(null)
                    .Min()));
        }

        rows = [.. rows.OrderByDescending(r => r.NetReceivable).ThenBy(r => r.CustomerName)];

        var totals = new ReceivablesTotalsDto(
            rows.Sum(r => r.Current),
            rows.Sum(r => r.Days1To30),
            rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90),
            rows.Sum(r => r.Days90Plus),
            rows.Sum(r => r.TotalDue),
            rows.Sum(r => r.Credits),
            rows.Sum(r => r.NetReceivable),
            rows.Sum(r => r.StoredBalance),
            backdated ? null : rows.Sum(r => r.NetReceivable) - rows.Sum(r => r.StoredBalance),
            rows.Sum(r => r.StoreCredit));

        return new ReceivablesAgeingDto(
            asOf,
            await CompanyCurrencyAsync(cancellationToken),
            rows,
            totals,
            [.. lines.OrderBy(l => l.DueAt)]);
    }

    private async Task<string> CompanyCurrencyAsync(CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(cancellationToken);

        return company?.BaseCurrency ?? "AED";
    }
}

// --- The statement --------------------------------------------------------------------------------

/// <summary>
/// One movement on a customer's account. <c>Debit</c> raises what they owe, <c>Credit</c> reduces it, and
/// exactly one of the two is non-zero on any line.
/// </summary>
public record StatementLineDto(
    DateTimeOffset At,
    string DocumentType,
    string Number,
    string? Reference,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

public record CustomerStatementDto(
    Guid CustomerId,
    string CustomerName,
    string CurrencyCode,
    DateTimeOffset From,
    DateTimeOffset To,

    /// <summary>What they owed the moment before <c>From</c> — every movement before it, replayed.</summary>
    decimal OpeningBalance,
    IReadOnlyCollection<StatementLineDto> Lines,
    decimal ClosingBalance,
    decimal StoreCredit,
    decimal StoredBalance,

    /// <summary>ClosingBalance − StoredBalance, when the statement runs to today. Null when it does not.</summary>
    decimal? Variance);

/// <summary>
/// A statement of account: what the customer owed at the start, every document that moved it, what they
/// owe now. It is the document a shop emails when it wants to be paid, and the one a customer disputes.
///
/// <b>Only movements that change the balance appear, and that is deliberate.</b> A store-credit note and a
/// cash refund are both net-zero against the balance — the first leaves the debt standing and hands over a
/// voucher (which shows up here as a payment on the day it is spent), the second gives money back against
/// an invoice that was already settled. Listing either as a line would break the one property a statement
/// must have: opening, plus everything printed on it, equals closing. A customer who cannot add up their
/// own statement will not pay it.
/// </summary>
[RequiresPermission(FeatureCatalog.Receivables, PermissionAction.View)]
public record GetCustomerStatementQuery(
    Guid CustomerId,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<CustomerStatementDto>;

public class GetCustomerStatementQueryHandler
    : IRequestHandler<GetCustomerStatementQuery, CustomerStatementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetCustomerStatementQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<CustomerStatementDto> Handle(
        GetCustomerStatementQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var to = request.To ?? now;
        var from = request.From ?? to.AddMonths(-3);

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var movements = new List<StatementLineDto>();

        var invoices = await _db.SalesInvoices
            .Include(i => i.Lines)
            .Where(i => i.CustomerId == customer.Id
                && i.InvoicedAt <= to
                && i.Status != SalesInvoiceStatus.Draft
                && i.Status != SalesInvoiceStatus.Cancelled)
            .ToListAsync(cancellationToken);

        movements.AddRange(invoices.Select(i => new StatementLineDto(
            i.InvoicedAt, "Invoice", i.Number, null, i.Total, 0m, 0m)));

        var payments = await _db.CustomerPayments
            .Include(p => p.Methods)
            .Where(p => p.CustomerId == customer.Id && p.PaidAt <= to)
            .ToListAsync(cancellationToken);

        movements.AddRange(payments.Select(p => new StatementLineDto(
            p.PaidAt, "Payment", p.Number, p.Reference, 0m, p.Amount, 0m)));

        var credits = await _db.CreditNotes
            .Include(c => c.Lines)
            .Where(c => c.CustomerId == customer.Id
                && c.Status == CreditNoteStatus.Issued
                && c.RefundMethod == RefundMethod.OffsetAgainstBalance
                && c.IssuedAt <= to)
            .ToListAsync(cancellationToken);

        movements.AddRange(credits.Select(c => new StatementLineDto(
            c.IssuedAt, "Credit note", c.Number, c.Reason, 0m, c.Total, 0m)));

        // The opening balance is not stored anywhere — there is no balance ledger, only a cached figure on
        // the customer (§45 D3, no general ledger). So it is replayed: every movement before the window,
        // summed. That the replay of *all* movements lands exactly on Customer.Balance is the same identity
        // the ageing asserts, and it is checked here too.
        var opening = movements
            .Where(m => m.At < from)
            .Sum(m => m.Debit - m.Credit);

        var window = movements
            .Where(m => m.At >= from)
            .OrderBy(m => m.At)
            .ThenBy(m => m.DocumentType)
            .ThenBy(m => m.Number)
            .ToList();

        var running = opening;
        var lines = new List<StatementLineDto>();

        foreach (var movement in window)
        {
            running += movement.Debit - movement.Credit;
            lines.Add(movement with { RunningBalance = running });
        }

        var storeCredit = await _db.StoreCreditEntries
            .Where(e => e.CustomerId == customer.Id && e.OccurredAt <= to)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        return new CustomerStatementDto(
            customer.Id,
            customer.Name,
            await CompanyCurrencyAsync(cancellationToken),
            from,
            to,
            opening,
            lines,
            running,
            storeCredit,
            customer.Balance,
            to < now ? null : running - customer.Balance);
    }

    private async Task<string> CompanyCurrencyAsync(CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(cancellationToken);

        return company?.BaseCurrency ?? "AED";
    }
}
