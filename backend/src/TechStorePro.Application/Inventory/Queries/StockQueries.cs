using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Queries;

public record StockBalanceDto(
    Guid ProductId,
    string ProductName,
    string Sku,
    string? Barcode,
    Guid WarehouseId,
    string WarehouseName,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal AverageCost,
    decimal TotalValue,
    decimal ReorderLevel,
    bool IsBelowReorderLevel);

// --- Stock on hand ------------------------------------------------------------------------------

/// <summary>
/// What is on the shelf right now. Reads <c>stock_balances</c>, the cache — not the ledger — because
/// this is the query the POS runs on every keystroke and summing a million movements to answer it
/// would be absurd. The cache is proven against the ledger nightly; see <c>GetBalanceAuditQuery</c>.
/// </summary>
[RequiresPermission(FeatureCatalog.Stock, PermissionAction.View)]
public record GetStockQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? WarehouseId = null,
    Guid? ProductId = null,
    Guid? CategoryId = null,
    bool? LowStock = null,
    bool? InStockOnly = null,
    string? SortBy = null,
    string? SortDir = null) : IRequest<PagedResult<StockBalanceDto>>;

public class GetStockQueryHandler : IRequestHandler<GetStockQuery, PagedResult<StockBalanceDto>>
{
    private readonly IApplicationDbContext _db;

    public GetStockQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<StockBalanceDto>> Handle(
        GetStockQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.StockBalances.AsNoTracking();

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(b => b.WarehouseId == warehouseId);
        }

        if (request.ProductId is { } productId)
        {
            query = query.Where(b => b.ProductId == productId);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(b => b.Product.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();

            query = query.Where(b =>
                b.Product.Name.ToLower().Contains(term)
                || b.Product.Sku.ToLower().Contains(term)
                || (b.Product.Barcode != null && b.Product.Barcode.ToLower().Contains(term)));
        }

        if (request.InStockOnly == true)
        {
            query = query.Where(b => b.Quantity != 0);
        }

        // The low-stock report of requirements §36. Compared against the balance in *this* warehouse,
        // not the company-wide total: a reorder level means "reorder when this shelf runs low", and a
        // shop with plenty in the main warehouse and none on the counter still needs to move stock.
        if (request.LowStock == true)
        {
            query = query.Where(b => b.Quantity <= b.Product.ReorderLevel);
        }

        var total = await query.CountAsync(cancellationToken);

        query = Sort(query, request.SortBy, request.SortDir);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new StockBalanceDto(
                b.ProductId,
                b.Product.Name,
                b.Product.Sku,
                b.Product.Barcode,
                b.WarehouseId,
                b.Warehouse.Name,
                b.Quantity,
                b.ReservedQuantity,
                b.Quantity - b.ReservedQuantity,
                b.AverageCost,
                b.Quantity * b.AverageCost,
                b.Product.ReorderLevel,
                b.Quantity <= b.Product.ReorderLevel))
            .ToListAsync(cancellationToken);

        return new PagedResult<StockBalanceDto>(items, total, page, pageSize);
    }

    private static IQueryable<StockBalance> Sort(IQueryable<StockBalance> query, string? sortBy, string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return (sortBy?.ToLowerInvariant()) switch
        {
            "quantity" => descending ? query.OrderByDescending(b => b.Quantity) : query.OrderBy(b => b.Quantity),
            "available" => descending
                ? query.OrderByDescending(b => b.Quantity - b.ReservedQuantity)
                : query.OrderBy(b => b.Quantity - b.ReservedQuantity),
            "value" => descending
                ? query.OrderByDescending(b => b.Quantity * b.AverageCost)
                : query.OrderBy(b => b.Quantity * b.AverageCost),
            "warehouse" => descending
                ? query.OrderByDescending(b => b.Warehouse.Name)
                : query.OrderBy(b => b.Warehouse.Name),
            _ => descending ? query.OrderByDescending(b => b.Product.Name) : query.OrderBy(b => b.Product.Name)
        };
    }
}

// --- The stock card -----------------------------------------------------------------------------

public record StockMovementDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    MovementType Type,
    Guid ProductId,
    string ProductName,
    string Sku,
    Guid WarehouseId,
    string WarehouseName,
    string? SerialNumber,
    decimal Quantity,
    decimal UnitCost,
    decimal Value,
    decimal BalanceAfter,
    decimal AverageCostAfter,
    StockReferenceType ReferenceType,
    Guid? ReferenceId,
    string? ReferenceNumber,
    string? Notes);

/// <summary>
/// The ledger itself: every movement of one product, in order. This is the screen someone opens when
/// the balance looks wrong, so it shows the running balance and the running average after each row —
/// the point at which the two diverged from expectation is the bug.
/// </summary>
[RequiresPermission(FeatureCatalog.StockMovements, PermissionAction.View)]
public record GetStockMovementsQuery(
    int Page = 1,
    int PageSize = 50,
    Guid? ProductId = null,
    Guid? WarehouseId = null,
    Guid? SerialId = null,
    MovementType? Type = null,
    StockReferenceType? ReferenceType = null,
    Guid? ReferenceId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<PagedResult<StockMovementDto>>;

public class GetStockMovementsQueryHandler
    : IRequestHandler<GetStockMovementsQuery, PagedResult<StockMovementDto>>
{
    private readonly IApplicationDbContext _db;

    public GetStockMovementsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<StockMovementDto>> Handle(
        GetStockMovementsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.StockMovements.AsNoTracking();

        if (request.ProductId is { } productId)
        {
            query = query.Where(m => m.ProductId == productId);
        }

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(m => m.WarehouseId == warehouseId);
        }

        if (request.SerialId is { } serialId)
        {
            query = query.Where(m => m.SerialId == serialId);
        }

        if (request.Type is { } type)
        {
            query = query.Where(m => m.Type == type);
        }

        if (request.ReferenceType is { } referenceType)
        {
            query = query.Where(m => m.ReferenceType == referenceType);
        }

        if (request.ReferenceId is { } referenceId)
        {
            query = query.Where(m => m.ReferenceId == referenceId);
        }

        if (request.From is { } from)
        {
            query = query.Where(m => m.OccurredAt >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(m => m.OccurredAt <= to);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);

        var items = await query
            // Newest first, and the id breaks the tie: several movements of one document share an
            // occurred_at to the microsecond, and an unstable sort would shuffle them between pages.
            .OrderByDescending(m => m.OccurredAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new StockMovementDto(
                m.Id,
                m.OccurredAt,
                m.Type,
                m.ProductId,
                m.Product.Name,
                m.Product.Sku,
                m.WarehouseId,
                m.Warehouse.Name,
                m.Serial != null ? m.Serial.SerialNumber : null,
                m.Quantity,
                m.UnitCost,
                m.Quantity * m.UnitCost,
                m.BalanceAfter,
                m.AverageCostAfter,
                m.ReferenceType,
                m.ReferenceId,
                m.ReferenceNumber,
                m.Notes))
            .ToListAsync(cancellationToken);

        return new PagedResult<StockMovementDto>(items, total, page, pageSize);
    }
}

// --- Historical stock ---------------------------------------------------------------------------

public record HistoricalStockLineDto(
    Guid ProductId,
    string ProductName,
    string Sku,
    decimal OpeningQuantity,
    decimal Purchases,
    decimal Sales,
    decimal TransfersIn,
    decimal TransfersOut,
    decimal Adjustments,
    decimal Repairs,
    decimal ClosingQuantity,
    decimal ClosingValue);

public record HistoricalStockDto(
    DateTimeOffset From,
    DateTimeOffset To,
    Guid? WarehouseId,
    IReadOnlyCollection<HistoricalStockLineDto> Lines);

/// <summary>
/// "What did we have on a previous date, and how did it get there?" (requirements §19).
///
/// <b>Replayed from the ledger, not read from a snapshot table.</b> There is no month-end close in this
/// system and no frozen period; a receipt entered late, backdated to the day the van actually arrived,
/// must change what last Tuesday's stock report says — because it changes what was on the shelf last
/// Tuesday. A snapshot would have baked in the wrong answer and never revisited it.
///
/// The buckets are exactly the movement types grouped, which is why <see cref="MovementType"/> is
/// finer-grained than "in" and "out": this report is what that granularity is for.
/// </summary>
[RequiresPermission(FeatureCatalog.Stock, PermissionAction.View)]
public record GetHistoricalStockQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    Guid? WarehouseId = null,
    Guid? ProductId = null,
    Guid? CategoryId = null) : IRequest<HistoricalStockDto>;

public class GetHistoricalStockQueryHandler : IRequestHandler<GetHistoricalStockQuery, HistoricalStockDto>
{
    private readonly IApplicationDbContext _db;

    public GetHistoricalStockQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<HistoricalStockDto> Handle(
        GetHistoricalStockQuery request,
        CancellationToken cancellationToken)
    {
        var movements = _db.StockMovements.AsNoTracking().Where(m => m.OccurredAt <= request.To);

        if (request.WarehouseId is { } warehouseId)
        {
            movements = movements.Where(m => m.WarehouseId == warehouseId);
        }

        if (request.ProductId is { } productId)
        {
            movements = movements.Where(m => m.ProductId == productId);
        }

        if (request.CategoryId is { } categoryId)
        {
            movements = movements.Where(m => m.Product.CategoryId == categoryId);
        }

        var from = request.From;

        // Grouped by (product, warehouse), not by product alone, because the moving average is a
        // per-warehouse number: the same laptop can carry a different cost in the shop than in the main
        // store, and averaging the two averages would be arithmetic nonsense. The buckets are summed
        // back up to the product afterwards.
        //
        // The buckets are conditional sums rather than filtered sub-aggregates so that the whole report
        // is one SUM(CASE WHEN …) pass over the index, in the database. Pulling the ledger into memory
        // to bucket it in C# would stream the largest table in the system across the wire.
        var buckets = await movements
            .GroupBy(m => new { m.ProductId, m.Product.Name, m.Product.Sku, m.WarehouseId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Name,
                g.Key.Sku,
                g.Key.WarehouseId,

                // Everything before the window opened, whatever type it was: opening stock is simply
                // "the closing balance of all of history up to that point".
                Opening = g.Sum(m => m.OccurredAt < from ? m.Quantity : 0m),

                Purchases = g.Sum(m => m.OccurredAt >= from
                    && (m.Type == MovementType.Receipt || m.Type == MovementType.OpeningBalance)
                    ? m.Quantity : 0m),

                Sales = g.Sum(m => m.OccurredAt >= from
                    && (m.Type == MovementType.Sale || m.Type == MovementType.SaleReturn)
                    ? m.Quantity : 0m),

                TransfersIn = g.Sum(m => m.OccurredAt >= from && m.Type == MovementType.TransferIn
                    ? m.Quantity : 0m),

                TransfersOut = g.Sum(m => m.OccurredAt >= from && m.Type == MovementType.TransferOut
                    ? m.Quantity : 0m),

                Adjustments = g.Sum(m => m.OccurredAt >= from
                    && (m.Type == MovementType.AdjustmentIn || m.Type == MovementType.AdjustmentOut
                        || m.Type == MovementType.CountAdjustmentIn || m.Type == MovementType.CountAdjustmentOut
                        || m.Type == MovementType.PurchaseReturn)
                    ? m.Quantity : 0m),

                Repairs = g.Sum(m => m.OccurredAt >= from
                    && (m.Type == MovementType.RepairConsumption || m.Type == MovementType.RepairReturn)
                    ? m.Quantity : 0m),

                // The whole history up to `to`. Because quantities are stored signed, closing stock is
                // a plain SUM — no business logic, which is exactly what an audit of the cache needs.
                Closing = g.Sum(m => m.Quantity)
            })
            .ToListAsync(cancellationToken);

        var closingAverages = await ClosingAveragesAsync(movements, cancellationToken);

        var lines = buckets
            .GroupBy(b => new { b.ProductId, b.Name, b.Sku })
            .Select(g => new HistoricalStockLineDto(
                g.Key.ProductId,
                g.Key.Name,
                g.Key.Sku,
                g.Sum(b => b.Opening),
                g.Sum(b => b.Purchases),
                g.Sum(b => b.Sales),
                g.Sum(b => b.TransfersIn),
                g.Sum(b => b.TransfersOut),
                g.Sum(b => b.Adjustments),
                g.Sum(b => b.Repairs),
                g.Sum(b => b.Closing),
                g.Sum(b => b.Closing * closingAverages.GetValueOrDefault((b.ProductId, b.WarehouseId), 0m))))
            .OrderBy(l => l.ProductName)
            .ToList();

        return new HistoricalStockDto(request.From, request.To, request.WarehouseId, lines);
    }

    /// <summary>
    /// The moving average as at the last movement of each (product, warehouse) in the window — what the
    /// stock was worth <em>then</em>, not what today's average would value it at. Valuing a historical
    /// report at today's cost is the classic way to make a stock report that never ties back to the
    /// accounts.
    ///
    /// Expressed as "the movement with no later movement" rather than an ordered take, because a
    /// per-group ordered projection is not something EF can translate — it would silently become a
    /// client-side evaluation of the entire ledger.
    /// </summary>
    private static async Task<Dictionary<(Guid ProductId, Guid WarehouseId), decimal>> ClosingAveragesAsync(
        IQueryable<StockMovement> movements,
        CancellationToken cancellationToken)
    {
        var last = await movements
            .Where(m => !movements.Any(later =>
                later.ProductId == m.ProductId
                && later.WarehouseId == m.WarehouseId
                && (later.OccurredAt > m.OccurredAt
                    // Several movements of one document share an occurred_at to the microsecond. The id
                    // breaks the tie deterministically; without it "the last movement" is whichever the
                    // planner happened to return.
                    || (later.OccurredAt == m.OccurredAt && later.Id > m.Id))))
            .Select(m => new { m.ProductId, m.WarehouseId, m.AverageCostAfter })
            .ToListAsync(cancellationToken);

        return last.ToDictionary(m => (m.ProductId, m.WarehouseId), m => m.AverageCostAfter);
    }
}

// --- Valuation ----------------------------------------------------------------------------------

public record StockValuationLineDto(
    Guid WarehouseId,
    string WarehouseName,
    int ProductCount,
    decimal TotalQuantity,
    decimal TotalValue);

public record StockValuationDto(decimal TotalValue, IReadOnlyCollection<StockValuationLineDto> ByWarehouse);

/// <summary>
/// What the stock is worth, per warehouse (requirements §35). At weighted-average cost — the same
/// number a sale books as COGS, so the valuation and the margin report cannot disagree.
/// </summary>
[RequiresPermission(FeatureCatalog.Stock, PermissionAction.View)]
public record GetStockValuationQuery(Guid? WarehouseId = null) : IRequest<StockValuationDto>;

public class GetStockValuationQueryHandler : IRequestHandler<GetStockValuationQuery, StockValuationDto>
{
    private readonly IApplicationDbContext _db;

    public GetStockValuationQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<StockValuationDto> Handle(
        GetStockValuationQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.StockBalances.AsNoTracking().Where(b => b.Quantity != 0);

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(b => b.WarehouseId == warehouseId);
        }

        var byWarehouse = await query
            .GroupBy(b => new { b.WarehouseId, b.Warehouse.Name })
            .Select(g => new StockValuationLineDto(
                g.Key.WarehouseId,
                g.Key.Name,
                g.Count(),
                g.Sum(b => b.Quantity),
                g.Sum(b => b.Quantity * b.AverageCost)))
            .OrderBy(l => l.WarehouseName)
            .ToListAsync(cancellationToken);

        return new StockValuationDto(byWarehouse.Sum(l => l.TotalValue), byWarehouse);
    }
}

// --- Proving the cache --------------------------------------------------------------------------

/// <summary>What kind of lie the cache is telling.</summary>
public enum DiscrepancyKind : short
{
    /// <summary>The cached quantity is not the sum of the movements.</summary>
    Quantity = 1,

    /// <summary>The quantity agrees but the money does not: the cached average is not the one the
    /// ledger last recorded. Every sale from this warehouse is booking the wrong COGS.</summary>
    Cost = 2,

    /// <summary>The ledger has movements for a product/warehouse that has no balance row at all. The
    /// stock is invisible to every "can I sell this?" question the system asks.</summary>
    MissingBalance = 3
}

public record BalanceDiscrepancyDto(
    Guid ProductId,
    string ProductName,
    Guid WarehouseId,
    string WarehouseName,
    DiscrepancyKind Kind,
    decimal CachedQuantity,
    decimal LedgerQuantity,
    decimal QuantityDifference,
    decimal CachedAverageCost,
    decimal LedgerAverageCost,
    decimal ValueDifference);

public record BalanceAuditDto(
    int BalancesChecked,
    bool Agrees,
    IReadOnlyCollection<BalanceDiscrepancyDto> Discrepancies);

/// <summary>
/// Recomputes every balance from the ledger and reports any that disagree (architecture.md §4.5).
///
/// <b>The ledger is the source of truth and the cache must be able to prove itself.</b> If this ever
/// returns a discrepancy, the balance table is wrong and something wrote stock outside
/// <c>IStockLedger</c> — which is exactly the failure the whole module is built to make impossible.
/// It is exposed as a query so that the nightly reconciliation job, and a suspicious operator, can
/// both run it — through <see cref="IBalanceAuditor"/>, which is the single implementation of the
/// arithmetic. A job with its own copy could pass while this endpoint failed, and then "the nightly
/// job is green" would mean nothing at all.
///
/// <para><b>It checks the money, not only the units.</b> A cache whose quantity is right and whose
/// average cost is wrong is arguably worse than one that is visibly short: nothing looks broken, and
/// every sale from that warehouse quietly books the wrong COGS into the P&amp;L. "Agree to the cent"
/// is the requirement (development-plan.md P3), so both sides are recomputed from the ledger as plain
/// sums — quantity from the signed quantities, value from the signed quantity × the unit cost the
/// movement was actually valued at.</para>
/// </summary>
[RequiresPermission(FeatureCatalog.Stock, PermissionAction.View)]
public record GetBalanceAuditQuery : IRequest<BalanceAuditDto>;

public class GetBalanceAuditQueryHandler : IRequestHandler<GetBalanceAuditQuery, BalanceAuditDto>
{
    private readonly IBalanceAuditor _auditor;

    public GetBalanceAuditQueryHandler(IBalanceAuditor auditor)
    {
        _auditor = auditor;
    }

    public Task<BalanceAuditDto> Handle(GetBalanceAuditQuery request, CancellationToken cancellationToken) =>
        _auditor.AuditAsync(cancellationToken);
}
