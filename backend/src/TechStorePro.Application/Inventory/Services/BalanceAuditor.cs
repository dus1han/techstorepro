using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Inventory.Queries;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Services;

/// <summary>
/// Proves the <c>stock_balances</c> cache against the <c>stock_movements</c> ledger.
///
/// <b>One implementation, two callers.</b> The <c>/inventory/balance-audit</c> endpoint and the nightly
/// reconciliation job both run this. If the job had its own copy of the arithmetic, it could pass while
/// the endpoint failed — and then "the nightly job is green" would mean nothing at all.
/// </summary>
public interface IBalanceAuditor
{
    /// <summary>
    /// Audits every balance visible in the current tenant scope. Read-only: it reports, it never
    /// repairs. A cache that disagrees with the ledger means something wrote stock outside
    /// <c>IStockLedger</c>, and silently overwriting the evidence would destroy the only trace of it.
    /// </summary>
    Task<BalanceAuditDto> AuditAsync(CancellationToken cancellationToken = default);
}

public class BalanceAuditor : IBalanceAuditor
{
    private readonly IApplicationDbContext _db;

    public BalanceAuditor(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BalanceAuditDto> AuditAsync(CancellationToken cancellationToken = default)
    {
        // Both sides are computed in the database. The ledger is the biggest table in the system and
        // pulling it into memory to sum it would turn the nightly audit into an outage.
        var checks = await _db.StockBalances
            .AsNoTracking()
            .Select(b => new
            {
                b.ProductId,
                ProductName = b.Product.Name,
                b.WarehouseId,
                WarehouseName = b.Warehouse.Name,
                b.Quantity,
                b.AverageCost,

                // A plain SUM of signed quantities. If the recompute had to ask a movement type for its
                // direction, it would be re-deriving the balance the same way the ledger wrote it, and a
                // shared bug would agree with itself and prove nothing.
                LedgerQuantity = _db.StockMovements
                    .Where(m => m.ProductId == b.ProductId && m.WarehouseId == b.WarehouseId)
                    .Sum(m => m.Quantity),

                // And a plain SUM of signed value. Under weighted average this *is* the closing stock
                // value: a receipt adds what it cost, and an issue removes what the stock was worth at
                // the moment it left (which is why the ledger stores the issue's unit cost rather than
                // recomputing it later). So the money is recomputed from first principles, in one SUM,
                // with no ordering and no business logic.
                //
                // It deliberately does NOT compare against StockMovement.AverageCostAfter. That column
                // is written *from* the balance — comparing the cache to a copy of itself would pass
                // happily for any corruption that was followed by another movement, because the next
                // movement would faithfully record the corrupted average as though it were correct.
                LedgerValue = _db.StockMovements
                    .Where(m => m.ProductId == b.ProductId && m.WarehouseId == b.WarehouseId)
                    .Sum(m => m.Quantity * m.UnitCost)
            })
            .ToListAsync(cancellationToken);

        var discrepancies = new List<BalanceDiscrepancyDto>();

        foreach (var c in checks)
        {
            var cachedValue = c.Quantity * c.AverageCost;

            var ledgerAverage = c.LedgerQuantity == 0
                ? 0m
                : Math.Round(c.LedgerValue / c.LedgerQuantity, 4, MidpointRounding.AwayFromZero);

            var quantityAgrees = c.Quantity == c.LedgerQuantity;

            // "Agree to the cent" — literally. The stored average is rounded to four places on every
            // receipt, so re-deriving it from the ledger can land a fraction of a fill away on a
            // warehouse with thousands of units. A cent of accumulated rounding is not a corrupted
            // cache; a cent is the threshold the requirement itself names.
            var costAgrees = Math.Abs(cachedValue - c.LedgerValue) <= 0.01m;

            if (quantityAgrees && costAgrees)
            {
                continue;
            }

            discrepancies.Add(new BalanceDiscrepancyDto(
                c.ProductId,
                c.ProductName,
                c.WarehouseId,
                c.WarehouseName,
                quantityAgrees ? DiscrepancyKind.Cost : DiscrepancyKind.Quantity,
                c.Quantity,
                c.LedgerQuantity,
                c.Quantity - c.LedgerQuantity,
                c.AverageCost,
                ledgerAverage,
                cachedValue - c.LedgerValue));
        }

        // Movements with no balance row at all. An audit that walks the cache and compares it to the
        // ledger is structurally blind to this case — there is no cached row to iterate onto — and the
        // stock would be invisible to every "can I sell this?" the system asks.
        var orphaned = await _db.StockMovements
            .AsNoTracking()
            .Where(m => !_db.StockBalances.Any(
                b => b.ProductId == m.ProductId && b.WarehouseId == m.WarehouseId))
            .GroupBy(m => new
            {
                m.ProductId,
                ProductName = m.Product.Name,
                m.WarehouseId,
                WarehouseName = m.Warehouse.Name
            })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.WarehouseId,
                g.Key.WarehouseName,
                Quantity = g.Sum(m => m.Quantity)
            })
            .ToListAsync(cancellationToken);

        discrepancies.AddRange(orphaned.Select(o => new BalanceDiscrepancyDto(
            o.ProductId,
            o.ProductName,
            o.WarehouseId,
            o.WarehouseName,
            DiscrepancyKind.MissingBalance,
            CachedQuantity: 0,
            o.Quantity,
            QuantityDifference: -o.Quantity,
            CachedAverageCost: 0,
            LedgerAverageCost: 0,
            ValueDifference: 0)));

        return new BalanceAuditDto(checks.Count, discrepancies.Count == 0, discrepancies);
    }
}
