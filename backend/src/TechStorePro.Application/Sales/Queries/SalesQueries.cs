using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Sales.Queries;

// A note on why these handlers materialise the page and then map it in C#, rather than projecting to a
// DTO in SQL as the purchasing queries do:
//
// The money on a sales line — NetTotal, TaxAmount, LineTotal — is a *computed property* backed by
// SalesMath, and EF Ignores it. It does not exist as a column, so it cannot appear in a projection: EF
// would refuse to translate it. Re-expressing the same arithmetic in the projection would work, and it
// is exactly the trap it looks like — the tax rule (§45 D7: discount first, then tax, rounded at the
// line) would then exist in two places, and the day someone changes one, quotes and invoices would
// disagree by a fils and nobody would know which was right.
//
// So the arithmetic stays in one place, and the page is mapped in memory. It costs one materialisation
// of at most 100 rows, which is what a paged list is.

public record SalesLineDto(
    Guid Id,
    Guid? ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxPercent,
    decimal NetTotal,
    decimal TaxAmount,
    decimal LineTotal,
    string? PriceSource);

public record QuotationDto(
    Guid Id,
    string Number,
    Guid? CustomerId,
    string? CustomerName,
    Guid BranchId,
    QuotationStatus Status,
    string CurrencyCode,
    DateTimeOffset QuotedAt,
    DateTimeOffset? ValidUntil,
    decimal NetTotal,
    decimal TaxTotal,
    decimal Total,
    string? Notes,
    IReadOnlyCollection<SalesLineDto> Lines);

[RequiresPermission(FeatureCatalog.Quotations, PermissionAction.View)]
public record GetQuotationsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    QuotationStatus? Status = null,
    Guid? CustomerId = null) : IRequest<PagedResult<QuotationDto>>;

public class GetQuotationsQueryHandler : IRequestHandler<GetQuotationsQuery, PagedResult<QuotationDto>>
{
    private readonly IApplicationDbContext _db;

    public GetQuotationsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<QuotationDto>> Handle(
        GetQuotationsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.Quotations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(q =>
                q.Number.ToLower().Contains(term)
                || (q.Customer != null && q.Customer.Name.ToLower().Contains(term)));
        }

        if (request.Status is { } status)
        {
            query = query.Where(q => q.Status == status);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(q => q.CustomerId == customerId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var quotations = await query
            .Include(q => q.Customer)
            .Include(q => q.Lines)
            .OrderByDescending(q => q.QuotedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = quotations
            .Select(q => new QuotationDto(
                q.Id,
                q.Number,
                q.CustomerId,
                q.Customer?.Name,
                q.BranchId,
                q.Status,
                q.CurrencyCode,
                q.QuotedAt,
                q.ValidUntil,
                q.NetTotal,
                q.TaxTotal,
                q.Total,
                q.Notes,
                q.Lines.Select(l => new SalesLineDto(
                    l.Id,
                    l.ProductId,
                    l.Description,
                    l.Quantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.DiscountAmount,
                    l.TaxPercent,
                    l.NetTotal,
                    l.TaxAmount,
                    l.LineTotal,
                    l.PriceSource)).ToList()))
            .ToList();

        return new PagedResult<QuotationDto>(items, total, page, pageSize);
    }
}

public record SalesOrderLineDto(
    Guid Id,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal DeliveredQuantity,
    decimal OutstandingQuantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxPercent,
    decimal NetTotal,
    decimal TaxAmount,
    decimal LineTotal,
    string? PriceSource,
    Guid? StockReservationId);

public record SalesOrderDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid BranchId,
    Guid WarehouseId,
    Guid? QuotationId,
    SalesOrderStatus Status,
    string CurrencyCode,
    DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt,
    decimal NetTotal,
    decimal TaxTotal,
    decimal Total,
    string? Notes,
    IReadOnlyCollection<SalesOrderLineDto> Lines);

[RequiresPermission(FeatureCatalog.SalesOrders, PermissionAction.View)]
public record GetSalesOrdersQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    SalesOrderStatus? Status = null,
    Guid? CustomerId = null) : IRequest<PagedResult<SalesOrderDto>>;

public class GetSalesOrdersQueryHandler : IRequestHandler<GetSalesOrdersQuery, PagedResult<SalesOrderDto>>
{
    private readonly IApplicationDbContext _db;

    public GetSalesOrdersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<SalesOrderDto>> Handle(
        GetSalesOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.SalesOrders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(o =>
                o.Number.ToLower().Contains(term) || o.Customer.Name.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(o => o.CustomerId == customerId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var orders = await query
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .OrderByDescending(o => o.OrderedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = orders
            .Select(o => new SalesOrderDto(
                o.Id,
                o.Number,
                o.CustomerId,
                o.Customer.Name,
                o.BranchId,
                o.WarehouseId,
                o.QuotationId,
                o.Status,
                o.CurrencyCode,
                o.OrderedAt,
                o.ExpectedAt,
                o.NetTotal,
                o.TaxTotal,
                o.Total,
                o.Notes,
                o.Lines.Select(l => new SalesOrderLineDto(
                    l.Id,
                    l.ProductId,
                    l.Description,
                    l.Quantity,
                    l.DeliveredQuantity,
                    l.OutstandingQuantity,
                    l.UnitPrice,
                    l.DiscountPercent,
                    l.DiscountAmount,
                    l.TaxPercent,
                    l.NetTotal,
                    l.TaxAmount,
                    l.LineTotal,
                    l.PriceSource,
                    l.StockReservationId)).ToList()))
            .ToList();

        return new PagedResult<SalesOrderDto>(items, total, page, pageSize);
    }
}

public record DeliveryLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitCost,
    decimal CostTotal,
    IReadOnlyCollection<string> Serials);

public record DeliveryDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid BranchId,
    Guid WarehouseId,
    Guid? SalesOrderId,
    DeliveryStatus Status,
    DateTimeOffset DeliveredAt,
    string? DeliveredTo,
    decimal CostTotal,
    string? Notes,
    IReadOnlyCollection<DeliveryLineDto> Lines);

[RequiresPermission(FeatureCatalog.Deliveries, PermissionAction.View)]
public record GetDeliveriesQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    DeliveryStatus? Status = null,
    Guid? CustomerId = null) : IRequest<PagedResult<DeliveryDto>>;

public class GetDeliveriesQueryHandler : IRequestHandler<GetDeliveriesQuery, PagedResult<DeliveryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetDeliveriesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<DeliveryDto>> Handle(
        GetDeliveriesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.Deliveries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(d =>
                d.Number.ToLower().Contains(term) || d.Customer.Name.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(d => d.CustomerId == customerId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var deliveries = await query
            .Include(d => d.Customer)
            .Include(d => d.Lines).ThenInclude(l => l.Product)
            .Include(d => d.Lines).ThenInclude(l => l.Serials)
            .OrderByDescending(d => d.DeliveredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = deliveries
            .Select(d => new DeliveryDto(
                d.Id,
                d.Number,
                d.CustomerId,
                d.Customer.Name,
                d.BranchId,
                d.WarehouseId,
                d.SalesOrderId,
                d.Status,
                d.DeliveredAt,
                d.DeliveredTo,
                d.CostTotal,
                d.Notes,
                d.Lines.Select(l => new DeliveryLineDto(
                    l.Id,
                    l.ProductId,
                    l.Product.Name,
                    l.Quantity,
                    l.UnitCost,
                    l.CostTotal,
                    l.Serials.Select(s => s.SerialNumber).ToList())).ToList()))
            .ToList();

        return new PagedResult<DeliveryDto>(items, total, page, pageSize);
    }
}

public record InvoiceLineDto(
    Guid Id,
    Guid? ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxPercent,
    decimal NetTotal,
    decimal TaxAmount,
    decimal LineTotal,
    decimal UnitCost,
    decimal GrossProfit,
    string? PriceSource,
    IReadOnlyCollection<string> Serials);

public record SalesInvoiceDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid BranchId,
    Guid? SalesOrderId,
    Guid? DeliveryId,
    SalesInvoiceStatus Status,
    string CurrencyCode,
    DateTimeOffset InvoicedAt,
    DateTimeOffset? DueAt,
    decimal NetTotal,
    decimal TaxTotal,
    decimal Total,
    decimal CostTotal,
    decimal GrossProfit,

    /// <summary>Received against this invoice, and what is still owed. The screen that takes a payment
    /// needs both, and having it infer them from the status would be a guess.</summary>
    decimal PaidAmount,
    decimal OutstandingAmount,
    string? Notes,
    IReadOnlyCollection<InvoiceLineDto> Lines);

[RequiresPermission(FeatureCatalog.SalesInvoices, PermissionAction.View)]
public record GetInvoicesQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    SalesInvoiceStatus? Status = null,
    Guid? CustomerId = null) : IRequest<PagedResult<SalesInvoiceDto>>;

public class GetInvoicesQueryHandler : IRequestHandler<GetInvoicesQuery, PagedResult<SalesInvoiceDto>>
{
    private readonly IApplicationDbContext _db;

    public GetInvoicesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<SalesInvoiceDto>> Handle(
        GetInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.SalesInvoices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Number.ToLower().Contains(term) || i.Customer.Name.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(i => i.CustomerId == customerId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var invoices = await SalesInvoiceMapping
            .WithGraph(query)
            .OrderByDescending(i => i.InvoicedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = invoices.Select(SalesInvoiceMapping.ToDto).ToList();

        return new PagedResult<SalesInvoiceDto>(items, total, page, pageSize);
    }
}

[RequiresPermission(FeatureCatalog.SalesInvoices, PermissionAction.View)]
public record GetInvoiceByIdQuery(Guid InvoiceId) : IRequest<SalesInvoiceDto>;

public class GetInvoiceByIdQueryHandler : IRequestHandler<GetInvoiceByIdQuery, SalesInvoiceDto>
{
    private readonly IApplicationDbContext _db;

    public GetInvoiceByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<SalesInvoiceDto> Handle(
        GetInvoiceByIdQuery request,
        CancellationToken cancellationToken)
    {
        var invoice = await SalesInvoiceMapping
            .WithGraph(_db.SalesInvoices.AsNoTracking())
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new NotFoundException("Invoice", request.InvoiceId);

        return SalesInvoiceMapping.ToDto(invoice);
    }
}

/// <summary>
/// One invoice shape, loaded and mapped one way. The list and the single fetch must not drift apart —
/// a total that reads differently on the list than on the document itself is the kind of thing that
/// destroys trust in every other number on the screen.
/// </summary>
internal static class SalesInvoiceMapping
{
    public static IQueryable<SalesInvoice> WithGraph(IQueryable<SalesInvoice> query) =>
        query
            .Include(i => i.Customer)

            // The allocations, because PaidAmount is computed from them. Without this Include they load
            // as an empty collection and every invoice reads as unpaid — a receivables report that would
            // chase customers who have already paid.
            .Include(i => i.Allocations)
            .Include(i => i.Lines).ThenInclude(l => l.DeliveryLine!).ThenInclude(dl => dl.Serials);

    public static SalesInvoiceDto ToDto(SalesInvoice i) => new(
        i.Id,
        i.Number,
        i.CustomerId,
        i.Customer.Name,
        i.BranchId,
        i.SalesOrderId,
        i.DeliveryId,
        i.Status,
        i.CurrencyCode,
        i.InvoicedAt,
        i.DueAt,
        i.NetTotal,
        i.TaxTotal,
        i.Total,
        i.CostTotal,
        i.GrossProfit,
        i.PaidAmount,
        i.OutstandingAmount,
        i.Notes,
        i.Lines.Select(l => new InvoiceLineDto(
            l.Id,
            l.ProductId,
            l.Description,
            l.Quantity,
            l.UnitPrice,
            l.DiscountPercent,
            l.DiscountAmount,
            l.TaxPercent,
            l.NetTotal,
            l.TaxAmount,
            l.LineTotal,
            l.UnitCost,
            l.GrossProfit,
            l.PriceSource,
            l.DeliveryLine is not null
                ? l.DeliveryLine.Serials.Select(s => s.SerialNumber).ToList()
                : [])).ToList());
}
