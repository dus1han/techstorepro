using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Serials;

public record SerialDto(
    Guid Id,
    string SerialNumber,
    Guid ProductId,
    string ProductName,
    string Sku,
    SerialStatus Status,
    Guid? WarehouseId,
    string? WarehouseName,
    decimal PurchaseCost,
    Guid? SupplierId,
    string? SupplierName,
    DateTimeOffset? WarrantyUntil,
    bool IsUnderWarranty,
    Guid? SoldInvoiceLineId);

public record SerialEventDto(
    SerialEventType Type,
    SerialStatus Status,
    Guid? WarehouseId,
    string? WarehouseName,
    StockReferenceType? ReferenceType,
    Guid? ReferenceId,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset At);

public record SerialHistoryDto(SerialDto Serial, IReadOnlyCollection<SerialEventDto> Events);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Serials, PermissionAction.View)]
public record GetSerialsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? ProductId = null,
    Guid? WarehouseId = null,
    SerialStatus? Status = null,
    bool? UnderWarranty = null) : IRequest<PagedResult<SerialDto>>;

public class GetSerialsQueryHandler : IRequestHandler<GetSerialsQuery, PagedResult<SerialDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetSerialsQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<SerialDto>> Handle(GetSerialsQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var query = _db.Serials.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // A scanner types the whole serial and expects one hit, but a human reading a sticker often
            // types only the tail of it — so this is a Contains, not an exact match.
            var term = request.Search.Trim().ToLower();

            query = query.Where(s =>
                s.SerialNumber.ToLower().Contains(term)
                || s.Product.Name.ToLower().Contains(term)
                || s.Product.Sku.ToLower().Contains(term));
        }

        if (request.ProductId is { } productId)
        {
            query = query.Where(s => s.ProductId == productId);
        }

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(s => s.WarehouseId == warehouseId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(s => s.Status == status);
        }

        if (request.UnderWarranty is { } underWarranty)
        {
            query = underWarranty
                ? query.Where(s => s.WarrantyUntil != null && s.WarrantyUntil > now)
                : query.Where(s => s.WarrantyUntil == null || s.WarrantyUntil <= now);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderBy(s => s.SerialNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SerialDto(
                s.Id,
                s.SerialNumber,
                s.ProductId,
                s.Product.Name,
                s.Product.Sku,
                s.Status,
                s.WarehouseId,
                s.Warehouse != null ? s.Warehouse.Name : null,
                s.PurchaseCost,
                s.SupplierId,
                s.Supplier != null ? s.Supplier.Name : null,
                s.WarrantyUntil,
                s.WarrantyUntil != null && s.WarrantyUntil > now,
                s.SoldInvoiceLineId))
            .ToListAsync(cancellationToken);

        return new PagedResult<SerialDto>(items, total, page, pageSize);
    }
}

// --- History ------------------------------------------------------------------------------------

/// <summary>
/// One machine's life: purchase → supplier → customer → warranty → repair (requirements §18).
///
/// This is the query a warranty claim runs. A customer puts a laptop on the counter, the clerk scans
/// it, and this answers whether we sold it, when, at what warranty, and what has been done to it since.
/// Everything else in the serial module exists to keep this answer true.
/// </summary>
[RequiresPermission(FeatureCatalog.Serials, PermissionAction.View)]
public record GetSerialHistoryQuery(string SerialNumber) : IRequest<SerialHistoryDto>;

public class GetSerialHistoryQueryHandler : IRequestHandler<GetSerialHistoryQuery, SerialHistoryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public GetSerialHistoryQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SerialHistoryDto> Handle(
        GetSerialHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var normalised = request.SerialNumber.Trim().ToUpperInvariant();
        var now = _clock.UtcNow;

        var serial = await _db.Serials
            .AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Warehouse)
            .Include(s => s.Supplier)
            .FirstOrDefaultAsync(s => s.SerialNumber == normalised, cancellationToken)
            ?? throw new NotFoundException("Serial", normalised);

        var events = await _db.SerialEvents
            .AsNoTracking()
            .Where(e => e.SerialId == serial.Id)
            .OrderBy(e => e.At)
            .Select(e => new SerialEventDto(
                e.Type,
                e.Status,
                e.WarehouseId,
                null,
                e.ReferenceType,
                e.ReferenceId,
                e.ReferenceNumber,
                e.Notes,
                e.At))
            .ToListAsync(cancellationToken);

        var dto = new SerialDto(
            serial.Id,
            serial.SerialNumber,
            serial.ProductId,
            serial.Product.Name,
            serial.Product.Sku,
            serial.Status,
            serial.WarehouseId,
            serial.Warehouse?.Name,
            serial.PurchaseCost,
            serial.SupplierId,
            serial.Supplier?.Name,
            serial.WarrantyUntil,
            serial.IsUnderWarrantyAt(now),
            serial.SoldInvoiceLineId);

        return new SerialHistoryDto(dto, events);
    }
}
