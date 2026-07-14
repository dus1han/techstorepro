using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Returns;

public record CreditNoteLineDto(
    Guid Id,
    Guid SalesInvoiceLineId,
    Guid? ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxPercent,
    decimal NetTotal,
    decimal TaxAmount,
    decimal LineTotal,
    bool RestockedToShelf);

public record CreditNoteDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid SalesInvoiceId,
    string InvoiceNumber,
    CreditNoteStatus Status,
    RefundMethod RefundMethod,
    string CurrencyCode,
    DateTimeOffset IssuedAt,
    string Reason,
    decimal NetTotal,
    decimal TaxTotal,
    decimal Total,
    IReadOnlyCollection<CreditNoteLineDto> Lines);

[RequiresPermission(FeatureCatalog.CreditNotes, PermissionAction.View)]
public record GetCreditNotesQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? CustomerId = null) : IRequest<PagedResult<CreditNoteDto>>;

public class GetCreditNotesQueryHandler : IRequestHandler<GetCreditNotesQuery, PagedResult<CreditNoteDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCreditNotesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<CreditNoteDto>> Handle(
        GetCreditNotesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.CreditNotes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c =>
                c.Number.ToLower().Contains(term)
                || c.Customer.Name.ToLower().Contains(term)
                || c.SalesInvoice.Number.ToLower().Contains(term));
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(c => c.CustomerId == customerId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var notes = await query
            .Include(c => c.Customer)
            .Include(c => c.SalesInvoice)
            .Include(c => c.Lines)
            .OrderByDescending(c => c.IssuedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = notes
            .Select(c => new CreditNoteDto(
                c.Id,
                c.Number,
                c.CustomerId,
                c.Customer.Name,
                c.SalesInvoiceId,
                c.SalesInvoice.Number,
                c.Status,
                c.RefundMethod,
                c.CurrencyCode,
                c.IssuedAt,
                c.Reason,
                c.NetTotal,
                c.TaxTotal,
                c.Total,
                c.Lines.Select(l => new CreditNoteLineDto(
                    l.Id,
                    l.SalesInvoiceLineId,
                    l.ProductId,
                    l.Description,
                    l.Quantity,
                    l.UnitPrice,
                    l.TaxPercent,
                    l.NetTotal,
                    l.TaxAmount,
                    l.LineTotal,
                    l.RestockedToShelf)).ToList()))
            .ToList();

        return new PagedResult<CreditNoteDto>(items, total, page, pageSize);
    }
}

public record StoreCreditEntryDto(
    Guid Id,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string Reason,
    Guid? CreditNoteId,
    Guid? CustomerPaymentId);

/// <param name="Balance">The sum of the entries — never a stored number, so it can always explain itself.</param>
public record StoreCreditDto(
    Guid CustomerId,
    string CustomerName,
    decimal Balance,
    IReadOnlyCollection<StoreCreditEntryDto> Entries);

/// <summary>
/// What credit a customer holds, and where every dirham of it came from. "Why do I have 240 credit?" is a
/// question the counter gets asked, and this is the answer.
/// </summary>
[RequiresPermission(FeatureCatalog.CreditNotes, PermissionAction.View)]
public record GetStoreCreditQuery(Guid CustomerId) : IRequest<StoreCreditDto>;

public class GetStoreCreditQueryHandler : IRequestHandler<GetStoreCreditQuery, StoreCreditDto>
{
    private readonly IApplicationDbContext _db;

    public GetStoreCreditQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<StoreCreditDto> Handle(GetStoreCreditQuery request, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var entries = await _db.StoreCreditEntries
            .AsNoTracking()
            .Where(e => e.CustomerId == request.CustomerId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new StoreCreditEntryDto(
                e.Id,
                e.Amount,
                e.OccurredAt,
                e.Reason,
                e.CreditNoteId,
                e.CustomerPaymentId))
            .ToListAsync(cancellationToken);

        return new StoreCreditDto(
            customer.Id,
            customer.Name,
            entries.Sum(e => e.Amount),
            entries);
    }
}
