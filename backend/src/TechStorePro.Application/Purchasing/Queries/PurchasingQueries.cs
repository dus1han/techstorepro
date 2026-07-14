using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Purchasing.Queries;

// These handlers materialise the page and then map it in C#, for the same reason the sales queries do:
// the money on a purchasing line — LineTotal, TaxAmount, LandedUnitCost — is a computed property with no
// column behind it, so it cannot appear in a SQL projection. Re-expressing the arithmetic in the
// projection would work, and it is exactly the trap it looks like: the rule would then exist in two
// places, and the day one changed, the list and the document would disagree by a fils and nobody would
// know which was right. It costs one materialisation of at most 100 rows, which is what a paged list is.

// --- Purchase orders ----------------------------------------------------------------------------

public record PurchaseOrderLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    decimal Quantity,
    decimal ReceivedQuantity,
    decimal OutstandingQuantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal LineTotal,
    string? Notes);

public record PurchaseOrderDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    Guid BranchId,
    Guid WarehouseId,
    PurchaseOrderStatus Status,
    string CurrencyCode,
    decimal ExchangeRate,
    DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt,
    DateTimeOffset? ApprovedAt,
    decimal Total,

    /// <summary>The order's value in the company's own money. An overseas order in USD is still a
    /// commitment in AED, and that is the number the shop budgets against.</summary>
    decimal TotalBase,
    bool IsFullyReceived,
    string? Notes,
    IReadOnlyCollection<PurchaseOrderLineDto> Lines);

[RequiresPermission(FeatureCatalog.PurchaseOrders, PermissionAction.View)]
public record GetPurchaseOrdersQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    PurchaseOrderStatus? Status = null,
    Guid? SupplierId = null) : IRequest<PagedResult<PurchaseOrderDto>>;

public class GetPurchaseOrdersQueryHandler
    : IRequestHandler<GetPurchaseOrdersQuery, PagedResult<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _db;

    public GetPurchaseOrdersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<PurchaseOrderDto>> Handle(
        GetPurchaseOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.PurchaseOrders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(o =>
                o.Number.ToLower().Contains(term) || o.Supplier.Name.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(o => o.SupplierId == supplierId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var orders = await PurchaseOrderMapping
            .WithGraph(query)
            .OrderByDescending(o => o.OrderedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseOrderDto>(
            orders.Select(PurchaseOrderMapping.ToDto).ToList(),
            total,
            page,
            pageSize);
    }
}

[RequiresPermission(FeatureCatalog.PurchaseOrders, PermissionAction.View)]
public record GetPurchaseOrderByIdQuery(Guid PurchaseOrderId) : IRequest<PurchaseOrderDto>;

public class GetPurchaseOrderByIdQueryHandler : IRequestHandler<GetPurchaseOrderByIdQuery, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _db;

    public GetPurchaseOrderByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PurchaseOrderDto> Handle(
        GetPurchaseOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var order = await PurchaseOrderMapping
            .WithGraph(_db.PurchaseOrders.AsNoTracking())
            .FirstOrDefaultAsync(o => o.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new NotFoundException("Purchase order", request.PurchaseOrderId);

        return PurchaseOrderMapping.ToDto(order);
    }
}

/// <summary>
/// One order shape, loaded and mapped one way. The list and the single fetch must not drift apart — a
/// total that reads differently on the list than on the document destroys trust in every other number
/// on the screen.
/// </summary>
internal static class PurchaseOrderMapping
{
    public static IQueryable<PurchaseOrder> WithGraph(IQueryable<PurchaseOrder> query) =>
        query
            .Include(o => o.Supplier)
            .Include(o => o.Lines).ThenInclude(l => l.Product);

    public static PurchaseOrderDto ToDto(PurchaseOrder o) => new(
        o.Id,
        o.Number,
        o.SupplierId,
        o.Supplier.Name,
        o.BranchId,
        o.WarehouseId,
        o.Status,
        o.CurrencyCode,
        o.ExchangeRate,
        o.OrderedAt,
        o.ExpectedAt,
        o.ApprovedAt,
        o.Total,
        o.Total * o.ExchangeRate,
        o.IsFullyReceived,
        o.Notes,
        o.Lines.Select(l => new PurchaseOrderLineDto(
            l.Id,
            l.ProductId,
            l.Product.Name,
            l.Product.Sku,
            l.Quantity,
            l.ReceivedQuantity,
            l.OutstandingQuantity,
            l.UnitPrice,
            l.DiscountPercent,
            l.LineTotal,
            l.Notes)).ToList());
}

// --- Goods receipts -----------------------------------------------------------------------------

public record GoodsReceiptLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSku,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal LineTotal,

    /// <summary>The container's charges folded into this line — zero until the shipment is costed, and
    /// zero forever for a local purchase, because nothing was shipped.</summary>
    decimal ApportionedCost,

    /// <summary>What the ledger actually booked, per unit, in base currency: goods price plus its share
    /// of the freight. This is the number that feeds the moving average.</summary>
    decimal LandedUnitCost,
    IReadOnlyCollection<string> Serials,
    string? Notes);

public record GoodsReceiptDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    Guid BranchId,
    Guid WarehouseId,
    string WarehouseName,
    Guid? PurchaseOrderId,
    string? PurchaseOrderNumber,
    Guid? ImportShipmentId,
    string? ImportShipmentNumber,
    string CurrencyCode,
    decimal ExchangeRate,
    string? SupplierReference,
    DateTimeOffset ReceivedAt,
    decimal GoodsTotal,
    decimal GoodsTotalBase,
    string? Notes,
    IReadOnlyCollection<GoodsReceiptLineDto> Lines);

[RequiresPermission(FeatureCatalog.GoodsReceipts, PermissionAction.View)]
public record GetGoodsReceiptsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? SupplierId = null,
    Guid? ImportShipmentId = null) : IRequest<PagedResult<GoodsReceiptDto>>;

public class GetGoodsReceiptsQueryHandler
    : IRequestHandler<GetGoodsReceiptsQuery, PagedResult<GoodsReceiptDto>>
{
    private readonly IApplicationDbContext _db;

    public GetGoodsReceiptsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<GoodsReceiptDto>> Handle(
        GetGoodsReceiptsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.GoodsReceipts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(r =>
                r.Number.ToLower().Contains(term)
                || r.Supplier.Name.ToLower().Contains(term)
                || (r.SupplierReference != null && r.SupplierReference.ToLower().Contains(term)));
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(r => r.SupplierId == supplierId);
        }

        if (request.ImportShipmentId is { } shipmentId)
        {
            query = query.Where(r => r.ImportShipmentId == shipmentId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var receipts = await GoodsReceiptMapping
            .WithGraph(query)
            .OrderByDescending(r => r.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<GoodsReceiptDto>(
            receipts.Select(GoodsReceiptMapping.ToDto).ToList(),
            total,
            page,
            pageSize);
    }
}

[RequiresPermission(FeatureCatalog.GoodsReceipts, PermissionAction.View)]
public record GetGoodsReceiptByIdQuery(Guid GoodsReceiptId) : IRequest<GoodsReceiptDto>;

public class GetGoodsReceiptByIdQueryHandler : IRequestHandler<GetGoodsReceiptByIdQuery, GoodsReceiptDto>
{
    private readonly IApplicationDbContext _db;

    public GetGoodsReceiptByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<GoodsReceiptDto> Handle(
        GetGoodsReceiptByIdQuery request,
        CancellationToken cancellationToken)
    {
        var receipt = await GoodsReceiptMapping
            .WithGraph(_db.GoodsReceipts.AsNoTracking())
            .FirstOrDefaultAsync(r => r.Id == request.GoodsReceiptId, cancellationToken)
            ?? throw new NotFoundException("Goods receipt", request.GoodsReceiptId);

        return GoodsReceiptMapping.ToDto(receipt);
    }
}

internal static class GoodsReceiptMapping
{
    public static IQueryable<GoodsReceipt> WithGraph(IQueryable<GoodsReceipt> query) =>
        query
            .Include(r => r.Supplier)
            .Include(r => r.Warehouse)
            .Include(r => r.PurchaseOrder)
            .Include(r => r.ImportShipment)
            .Include(r => r.Lines).ThenInclude(l => l.Product)
            .Include(r => r.Lines).ThenInclude(l => l.Serials);

    public static GoodsReceiptDto ToDto(GoodsReceipt r) => new(
        r.Id,
        r.Number,
        r.SupplierId,
        r.Supplier.Name,
        r.BranchId,
        r.WarehouseId,
        r.Warehouse.Name,
        r.PurchaseOrderId,
        r.PurchaseOrder?.Number,
        r.ImportShipmentId,
        r.ImportShipment?.Number,
        r.CurrencyCode,
        r.ExchangeRate,
        r.SupplierReference,
        r.ReceivedAt,
        r.GoodsTotal,
        r.GoodsTotalBase,
        r.Notes,
        r.Lines.Select(l => new GoodsReceiptLineDto(
            l.Id,
            l.ProductId,
            l.Product.Name,
            l.Product.Sku,
            l.Quantity,
            l.UnitPrice,
            l.DiscountPercent,
            l.LineTotal,
            l.ApportionedCost,
            l.LandedUnitCost,
            l.Serials.Select(s => s.SerialNumber).ToList(),
            l.Notes)).ToList());
}

// --- Supplier invoices --------------------------------------------------------------------------

public record SupplierInvoiceLineDto(
    Guid Id,
    Guid? ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal TaxPercent,
    decimal NetTotal,
    decimal TaxAmount,
    decimal LineTotal);

public record SupplierInvoiceDto(
    Guid Id,
    string Number,

    /// <summary>The supplier's own invoice number — what they will quote when they chase payment.</summary>
    string SupplierReference,
    Guid SupplierId,
    string SupplierName,
    Guid BranchId,
    Guid? GoodsReceiptId,
    string? GoodsReceiptNumber,
    SupplierInvoiceStatus Status,
    string CurrencyCode,
    decimal ExchangeRate,
    DateTimeOffset InvoicedAt,
    DateTimeOffset? DueAt,
    decimal Total,

    /// <summary>What the company owes in its own money, fixed at the invoice-date rate.</summary>
    decimal TotalBase,

    /// <summary>Paid and still owing, in the invoice's currency. The screen that pays a bill needs both,
    /// and having it infer them from the status would be a guess.</summary>
    decimal PaidAmount,
    decimal OutstandingAmount,
    string? Notes,
    IReadOnlyCollection<SupplierInvoiceLineDto> Lines);

[RequiresPermission(FeatureCatalog.SupplierInvoices, PermissionAction.View)]
public record GetSupplierInvoicesQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    SupplierInvoiceStatus? Status = null,
    Guid? SupplierId = null) : IRequest<PagedResult<SupplierInvoiceDto>>;

public class GetSupplierInvoicesQueryHandler
    : IRequestHandler<GetSupplierInvoicesQuery, PagedResult<SupplierInvoiceDto>>
{
    private readonly IApplicationDbContext _db;

    public GetSupplierInvoicesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<SupplierInvoiceDto>> Handle(
        GetSupplierInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.SupplierInvoices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Number.ToLower().Contains(term)
                || i.SupplierReference.ToLower().Contains(term)
                || i.Supplier.Name.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(i => i.SupplierId == supplierId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var invoices = await SupplierInvoiceMapping
            .WithGraph(query)
            .OrderByDescending(i => i.InvoicedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierInvoiceDto>(
            invoices.Select(SupplierInvoiceMapping.ToDto).ToList(),
            total,
            page,
            pageSize);
    }
}

internal static class SupplierInvoiceMapping
{
    public static IQueryable<SupplierInvoice> WithGraph(IQueryable<SupplierInvoice> query) =>
        query
            .Include(i => i.Supplier)
            .Include(i => i.GoodsReceipt)

            // The allocations, because PaidAmount is computed from them. Without this Include they load
            // as an empty collection and every invoice reads as unpaid — a payables report that would
            // pay the same supplier twice.
            .Include(i => i.Allocations)
            .Include(i => i.Lines);

    public static SupplierInvoiceDto ToDto(SupplierInvoice i) => new(
        i.Id,
        i.Number,
        i.SupplierReference,
        i.SupplierId,
        i.Supplier.Name,
        i.BranchId,
        i.GoodsReceiptId,
        i.GoodsReceipt?.Number,
        i.Status,
        i.CurrencyCode,
        i.ExchangeRate,
        i.InvoicedAt,
        i.DueAt,
        i.Total,
        i.TotalBase,
        i.PaidAmount,
        i.OutstandingAmount,
        i.Notes,
        i.Lines.Select(l => new SupplierInvoiceLineDto(
            l.Id,
            l.ProductId,
            l.Description,
            l.Quantity,
            l.UnitPrice,
            l.DiscountPercent,
            l.TaxPercent,
            l.NetTotal,
            l.TaxAmount,
            l.LineTotal)).ToList());
}

// --- Supplier payments --------------------------------------------------------------------------

public record SupplierPaymentAllocationDto(
    Guid Id,
    Guid SupplierInvoiceId,
    string SupplierInvoiceNumber,
    decimal Amount,
    decimal InvoiceExchangeRate,
    decimal PaymentExchangeRate,

    /// <summary>Positive is a gain: the debt cost less to settle than it was booked at. It is P&amp;L,
    /// not a reduction in the cost of the stock — the goods did not become cheaper to buy.</summary>
    decimal ExchangeGainOrLoss);

public record SupplierPaymentDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    Guid BranchId,
    Guid PaymentMethodId,
    string PaymentMethodName,
    string? Reference,
    decimal Amount,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal AmountBase,
    decimal AllocatedAmount,

    /// <summary>Paid but not yet pointed at an invoice — an advance. A real state, not an error.</summary>
    decimal UnallocatedAmount,
    decimal ExchangeGainOrLoss,
    DateTimeOffset PaidAt,
    string? Notes,
    IReadOnlyCollection<SupplierPaymentAllocationDto> Allocations);

[RequiresPermission(FeatureCatalog.SupplierPayments, PermissionAction.View)]
public record GetSupplierPaymentsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? SupplierId = null) : IRequest<PagedResult<SupplierPaymentDto>>;

public class GetSupplierPaymentsQueryHandler
    : IRequestHandler<GetSupplierPaymentsQuery, PagedResult<SupplierPaymentDto>>
{
    private readonly IApplicationDbContext _db;

    public GetSupplierPaymentsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<SupplierPaymentDto>> Handle(
        GetSupplierPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.SupplierPayments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p =>
                p.Number.ToLower().Contains(term)
                || p.Supplier.Name.ToLower().Contains(term)
                || (p.Reference != null && p.Reference.ToLower().Contains(term)));
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(p => p.SupplierId == supplierId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var payments = await query
            .Include(p => p.Supplier)
            .Include(p => p.PaymentMethod)
            .Include(p => p.Allocations).ThenInclude(a => a.SupplierInvoice)
            .OrderByDescending(p => p.PaidAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = payments
            .Select(p => new SupplierPaymentDto(
                p.Id,
                p.Number,
                p.SupplierId,
                p.Supplier.Name,
                p.BranchId,
                p.PaymentMethodId,
                p.PaymentMethod.Name,
                p.Reference,
                p.Amount,
                p.CurrencyCode,
                p.ExchangeRate,
                p.AmountBase,
                p.AllocatedAmount,
                p.UnallocatedAmount,
                p.ExchangeGainOrLoss,
                p.PaidAt,
                p.Notes,
                p.Allocations.Select(a => new SupplierPaymentAllocationDto(
                    a.Id,
                    a.SupplierInvoiceId,
                    a.SupplierInvoice.Number,
                    a.Amount,
                    a.InvoiceExchangeRate,
                    a.PaymentExchangeRate,
                    a.ExchangeGainOrLoss)).ToList()))
            .ToList();

        return new PagedResult<SupplierPaymentDto>(items, total, page, pageSize);
    }
}
