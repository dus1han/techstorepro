using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Reports.Queries;

// What the shop owes its suppliers, and how late it is. The mirror of the receivables ageing, and simpler
// in one way and harder in another.
//
// SIMPLER: there is no such thing as a supplier credit note in this system, so nothing drives a wedge
// between the invoice and the balance except money paid on account. The identity is shorter:
//
//      Σ (invoice.Outstanding × invoice.ExchangeRate)  −  advances  =  Supplier.Balance
//
// HARDER: purchases are in any currency (§45 D8 — the asymmetry with sales is deliberate; the shop
// genuinely owes dollars to an overseas supplier, but it does not have to bill in them). So a payable has
// two figures and the report must show both: what the supplier will chase — USD 1,000 — and what it is
// worth in the shop's own money.
//
// AND THE RATE IT IS WORTH IT AT IS THE INVOICE'S, NOT TODAY'S. That is the load-bearing decision in this
// file:
//
//   * It is the only valuation that reconciles. The balance was raised by Total × ExchangeRate and is
//     reduced, on settlement, by the allocation measured at the *invoice's* rate — that is what
//     PaySupplierCommand does, and the AED residue between the two rates is booked as a realised FX gain.
//     Value the open payable at today's spot instead and the report stops tying to the balance it exists
//     to prove.
//
//   * Revaluing an open payable at today's rate would book an *unrealised* gain, and this system has no
//     such concept anywhere — it books FX only when the money actually moves. Inventing unrealised FX in a
//     report, of all places, would be the wrong end of the system to invent it in.
//
// So a USD 1,000 invoice raised at 3.67 sits in this report at AED 3,670 until it is paid, whatever the
// rate does in the meantime. When it is paid at 3.60, AED 3,600 leaves the bank and the AED 70 becomes a
// realised gain — and the payable goes to zero, which it must, because the debt is discharged.

public record PayablesInvoiceDto(
    Guid InvoiceId,
    string Number,
    string? SupplierReference,
    Guid SupplierId,
    string SupplierName,
    DateTimeOffset InvoicedAt,
    DateTimeOffset DueAt,
    int DaysOverdue,
    AgeingBucket Bucket,

    /// <summary>In the supplier's currency — the number they will quote at you on the phone.</summary>
    decimal Total,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string CurrencyCode,
    decimal ExchangeRate,

    /// <summary>The same debt in the shop's own money, at the rate the invoice was booked at.</summary>
    decimal OutstandingBase);

public record PayablesAgeingRowDto(
    Guid SupplierId,
    string SupplierName,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days90Plus,
    decimal TotalDue,

    /// <summary>Money paid to the supplier that no invoice has claimed yet — an advance. It comes off.</summary>
    decimal Advances,
    decimal NetPayable,
    decimal StoredBalance,

    /// <summary>NetPayable − StoredBalance. Must be zero. Null on a backdated report.</summary>
    decimal? Variance,
    int OpenInvoices,
    DateTimeOffset? OldestDueAt);

public record PayablesTotalsDto(
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days90Plus,
    decimal TotalDue,
    decimal Advances,
    decimal NetPayable,
    decimal StoredBalance,
    decimal? Variance);

/// <summary>Every figure on this report is in the company's base currency, bar the per-invoice detail.</summary>
public record PayablesAgeingDto(
    DateTimeOffset AsOf,
    string CurrencyCode,
    IReadOnlyCollection<PayablesAgeingRowDto> Rows,
    PayablesTotalsDto Totals,
    IReadOnlyCollection<PayablesInvoiceDto> Invoices);

[RequiresPermission(FeatureCatalog.Payables, PermissionAction.View)]
public record GetPayablesAgeingQuery(
    DateTimeOffset? AsOf = null,
    Guid? SupplierId = null) : IRequest<PayablesAgeingDto>;

public class GetPayablesAgeingQueryHandler
    : IRequestHandler<GetPayablesAgeingQuery, PayablesAgeingDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetPayablesAgeingQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PayablesAgeingDto> Handle(
        GetPayablesAgeingQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var asOf = request.AsOf ?? now;
        var backdated = asOf < now;

        var paidByInvoice = await _db.SupplierPayments
            .Where(p => p.PaidAt <= asOf)
            .SelectMany(p => p.Allocations)
            .GroupBy(a => a.SupplierInvoiceId)
            .Select(g => new { InvoiceId = g.Key, Paid = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, cancellationToken);

        var settledLater = backdated
            ? await _db.SupplierPayments
                .Where(p => p.PaidAt > asOf)
                .SelectMany(p => p.Allocations)
                .Select(a => a.SupplierInvoiceId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : [];

        var invoicesQuery = _db.SupplierInvoices
            .Include(i => i.Lines)
            .Include(i => i.Supplier)
            .Where(i => i.InvoicedAt <= asOf)
            .Where(i => i.Status == SupplierInvoiceStatus.Posted
                || i.Status == SupplierInvoiceStatus.PartiallyPaid
                || settledLater.Contains(i.Id));

        if (request.SupplierId is { } supplierId)
        {
            invoicesQuery = invoicesQuery.Where(i => i.SupplierId == supplierId);
        }

        var invoices = await invoicesQuery.ToListAsync(cancellationToken);

        var lines = new List<PayablesInvoiceDto>();

        foreach (var invoice in invoices)
        {
            var paid = paidByInvoice.GetValueOrDefault(invoice.Id);
            var outstanding = invoice.Total - paid;

            if (outstanding == 0m)
            {
                continue;
            }

            var dueAt = Ageing.DueDate(invoice.DueAt, invoice.InvoicedAt);

            lines.Add(new PayablesInvoiceDto(
                invoice.Id,
                invoice.Number,
                invoice.SupplierReference,
                invoice.SupplierId,
                invoice.Supplier.Name,
                invoice.InvoicedAt,
                dueAt,
                Ageing.DaysOverdue(dueAt, asOf),
                Ageing.Bucket(dueAt, asOf),
                invoice.Total,
                paid,
                outstanding,
                invoice.CurrencyCode,
                invoice.ExchangeRate,
                outstanding * invoice.ExchangeRate));
        }

        // An advance comes off the balance at the rate the money actually left at — it settles no invoice,
        // so there is no invoice rate for it to be measured against.
        var advances = (await _db.SupplierPayments
                .Where(p => p.PaidAt <= asOf)
                .Select(p => new
                {
                    p.SupplierId,
                    Unallocated = p.Amount - p.Allocations.Sum(a => a.Amount),
                    p.ExchangeRate
                })
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.SupplierId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Unallocated * p.ExchangeRate));

        var supplierIds = lines.Select(l => l.SupplierId)
            .Concat(advances.Where(x => x.Value != 0m).Select(x => x.Key))
            .Distinct()
            .ToList();

        var suppliers = await _db.Suppliers
            .Where(s => supplierIds.Contains(s.Id) || s.Balance != 0m)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var rows = new List<PayablesAgeingRowDto>();

        foreach (var (id, supplier) in suppliers)
        {
            if (request.SupplierId is { } only && id != only)
            {
                continue;
            }

            var theirs = lines.Where(l => l.SupplierId == id).ToList();
            var columns = new AgeingColumns();
            var advance = advances.GetValueOrDefault(id);

            foreach (var line in theirs)
            {
                if (line.OutstandingBase > 0m)
                {
                    columns.Add(line.Bucket, line.OutstandingBase);
                }
                else
                {
                    advance += -line.OutstandingBase;
                }
            }

            var net = columns.TotalDue - advance;

            rows.Add(new PayablesAgeingRowDto(
                id,
                supplier.Name,
                columns.Current,
                columns.Days1To30,
                columns.Days31To60,
                columns.Days61To90,
                columns.Days90Plus,
                columns.TotalDue,
                advance,
                net,
                supplier.Balance,
                backdated ? null : net - supplier.Balance,
                theirs.Count(l => l.OutstandingBase > 0m),
                theirs.Where(l => l.OutstandingBase > 0m)
                    .Select(l => (DateTimeOffset?)l.DueAt)
                    .DefaultIfEmpty(null)
                    .Min()));
        }

        rows = [.. rows.OrderByDescending(r => r.NetPayable).ThenBy(r => r.SupplierName)];

        var totals = new PayablesTotalsDto(
            rows.Sum(r => r.Current),
            rows.Sum(r => r.Days1To30),
            rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90),
            rows.Sum(r => r.Days90Plus),
            rows.Sum(r => r.TotalDue),
            rows.Sum(r => r.Advances),
            rows.Sum(r => r.NetPayable),
            rows.Sum(r => r.StoredBalance),
            backdated ? null : rows.Sum(r => r.NetPayable) - rows.Sum(r => r.StoredBalance));

        return new PayablesAgeingDto(
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

public record SupplierStatementDto(
    Guid SupplierId,
    string SupplierName,
    string CurrencyCode,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal OpeningBalance,
    IReadOnlyCollection<StatementLineDto> Lines,
    decimal ClosingBalance,
    decimal StoredBalance,
    decimal? Variance);

/// <summary>
/// The supplier's account, in the shop's own money. <b>Every line is base currency</b>, because the
/// statement's job is to explain <c>Supplier.Balance</c>, and that is what the balance is kept in — an
/// account mixing dirhams and dollars down one column would not add up to anything.
///
/// A payment that settles a foreign-currency invoice therefore appears at the <em>invoice's</em> rate, not
/// the rate the money left at: that is the amount of debt it actually discharged. The difference between
/// the two rates is the realised FX gain, and it is not a movement on the supplier's account at all — it
/// is the shop's own profit, and it belongs in the P&amp;L rather than here.
/// </summary>
[RequiresPermission(FeatureCatalog.Payables, PermissionAction.View)]
public record GetSupplierStatementQuery(
    Guid SupplierId,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<SupplierStatementDto>;

public class GetSupplierStatementQueryHandler
    : IRequestHandler<GetSupplierStatementQuery, SupplierStatementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetSupplierStatementQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SupplierStatementDto> Handle(
        GetSupplierStatementQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var to = request.To ?? now;
        var from = request.From ?? to.AddMonths(-3);

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.SupplierId);

        var movements = new List<StatementLineDto>();

        var invoices = await _db.SupplierInvoices
            .Include(i => i.Lines)
            .Where(i => i.SupplierId == supplier.Id
                && i.InvoicedAt <= to
                && i.Status != SupplierInvoiceStatus.Draft
                && i.Status != SupplierInvoiceStatus.Cancelled)
            .ToListAsync(cancellationToken);

        // Credit on a supplier statement is what the shop owes: an invoice raises it.
        movements.AddRange(invoices.Select(i => new StatementLineDto(
            i.InvoicedAt,
            "Invoice",
            i.Number,
            i.SupplierReference,
            0m,
            i.TotalBase,
            0m)));

        var payments = await _db.SupplierPayments
            .Include(p => p.Allocations)
            .Where(p => p.SupplierId == supplier.Id && p.PaidAt <= to)
            .ToListAsync(cancellationToken);

        movements.AddRange(payments.Select(p => new StatementLineDto(
            p.PaidAt,
            "Payment",
            p.Number,
            p.Reference,
            p.Allocations.Sum(a => a.Amount * a.InvoiceExchangeRate)
                + (p.UnallocatedAmount * p.ExchangeRate),
            0m,
            0m)));

        var opening = movements
            .Where(m => m.At < from)
            .Sum(m => m.Credit - m.Debit);

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
            running += movement.Credit - movement.Debit;
            lines.Add(movement with { RunningBalance = running });
        }

        return new SupplierStatementDto(
            supplier.Id,
            supplier.Name,
            await CompanyCurrencyAsync(cancellationToken),
            from,
            to,
            opening,
            lines,
            running,
            supplier.Balance,
            to < now ? null : running - supplier.Balance);
    }

    private async Task<string> CompanyCurrencyAsync(CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(cancellationToken);

        return company?.BaseCurrency ?? "AED";
    }
}
