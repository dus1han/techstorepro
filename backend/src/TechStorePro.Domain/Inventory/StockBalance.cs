using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Inventory;

/// <summary>
/// What one product is worth, and how much of it there is, in one warehouse.
///
/// A <b>cache</b> of <see cref="StockMovement"/>, not a second source of truth. It exists because
/// "can I sell this?" is asked on every POS keystroke and summing a million-row ledger to answer it
/// would be absurd. It is written in the <em>same transaction</em> as the movement that changes it,
/// under a row lock, and a nightly job recomputes it from the ledger and must agree to the cent.
///
/// Like the ledger, it is not soft-deletable: a balance of zero is a fact, not a retired row.
/// </summary>
public class StockBalance : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Physically on the shelf, including anything reserved.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Promised to a quote or an unfulfilled order and therefore not sellable to anyone else
    /// (requirements §20). It is still on the shelf, so it is <em>not</em> subtracted from
    /// <see cref="Quantity"/> — it is subtracted from what may be promised again.
    /// </summary>
    public decimal ReservedQuantity { get; set; }

    /// <summary>Weighted moving average (architecture.md §4.6). The cost a sale from here books as COGS.</summary>
    public decimal AverageCost { get; set; }

    /// <summary>
    /// <b>This subtraction is what "prevent overselling" means.</b> Everything else in the reservation
    /// story is bookkeeping around this one expression.
    /// </summary>
    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    public decimal TotalValue => Quantity * AverageCost;

    /// <summary>
    /// Applies an inbound movement and returns the unit cost it was actually valued at.
    ///
    /// A cost that is supplied raises the moving average. A cost that is absent — legal only for the
    /// movement types that do not require one, see <see cref="MovementTypes.RequiresUnitCost"/> — values
    /// the stock at what this warehouse already believes the product is worth, leaving the average
    /// untouched. That is correct rather than lazy: quantity rose and total value rose in the same
    /// proportion, so the average per unit did not move.
    /// </summary>
    public decimal ApplyInbound(MovementType type, decimal quantity, decimal? unitCost, ICostingStrategy costing)
    {
        if (!type.IsInbound())
        {
            throw new DomainException($"{type} is not an inbound movement.");
        }

        RequirePositive(quantity);

        if (unitCost is null)
        {
            if (type.RequiresUnitCost())
            {
                throw new DomainException(
                    $"A {type} movement must carry a unit cost: it is what raises the moving average.");
            }

            Quantity += quantity;
            return AverageCost;
        }

        if (unitCost < 0)
        {
            throw new DomainException("A unit cost cannot be negative.");
        }

        AverageCost = costing.NewAverageCost(Quantity, AverageCost, quantity, unitCost.Value);
        Quantity += quantity;

        return unitCost.Value;
    }

    /// <summary>
    /// Applies an outbound movement and returns the cost it was valued at — the current average, which
    /// is the COGS a sale line snapshots.
    ///
    /// <para><b>The average is deliberately not touched.</b> Issuing stock at the average leaves the
    /// average where it was; that is the whole point of the method. A weighted average only moves when
    /// stock comes <em>in</em> at a different price.</para>
    ///
    /// <paramref name="allowanceFromReservation"/> is quantity this movement is consuming against a
    /// reservation it already holds. Without it, delivering the two units you reserved would fail its
    /// own availability check — the reservation would be competing with the sale that made it.
    /// </summary>
    public decimal ApplyOutbound(MovementType type, decimal quantity, decimal allowanceFromReservation = 0)
    {
        if (!type.IsOutbound())
        {
            throw new DomainException($"{type} is not an outbound movement.");
        }

        RequirePositive(quantity);

        var sellable = AvailableQuantity + allowanceFromReservation;

        if (quantity > sellable)
        {
            throw new InsufficientStockException(ProductId, WarehouseId, quantity, sellable);
        }

        Quantity -= quantity;

        return AverageCost;
    }

    /// <summary>Requirements §20: promise stock to a quote or an order without moving it.</summary>
    public void Reserve(decimal quantity)
    {
        RequirePositive(quantity);

        if (quantity > AvailableQuantity)
        {
            throw new InsufficientStockException(ProductId, WarehouseId, quantity, AvailableQuantity);
        }

        ReservedQuantity += quantity;
    }

    /// <summary>Releases a reservation — the quote expired, the order was cancelled, or it was delivered.</summary>
    public void ReleaseReservation(decimal quantity)
    {
        RequirePositive(quantity);

        if (quantity > ReservedQuantity)
        {
            // Releasing more than was ever reserved would drive reserved_quantity negative and quietly
            // make stock available that nobody has. Better to fail: the caller's bookkeeping is wrong.
            throw new DomainException(
                $"Cannot release {quantity} units: only {ReservedQuantity} are reserved.");
        }

        ReservedQuantity -= quantity;
    }

    private static void RequirePositive(decimal quantity)
    {
        if (quantity <= 0)
        {
            // A movement of zero is not a movement, and a negative magnitude is a caller trying to
            // decide the direction for itself — MovementType.Direction() is the only thing allowed to.
            throw new DomainException("A stock quantity must be greater than zero.");
        }
    }
}

/// <summary>
/// How stock is valued. Weighted average is the only implementation shipped (architecture.md §4.6,
/// requirements §45 D1) — the interface exists so that FIFO remains a second implementation rather
/// than a rewrite, but no FIFO cost-layer table is built and none is planned.
/// </summary>
public interface ICostingStrategy
{
    decimal NewAverageCost(decimal quantityOnHand, decimal averageCost, decimal quantityIn, decimal unitCostIn);
}

/// <summary>
/// <c>new_average = (qty_on_hand × old_average + qty_in × landed_unit_cost) ÷ (qty_on_hand + qty_in)</c>
///
/// The cost that raises the average must be the <b>landed</b> cost, not the invoice cost, or every
/// import silently under-values stock — and because the average is moving, that error spreads to all
/// existing units of the product, not just the ones that arrived. P4 owes this method a landed figure.
/// </summary>
public class WeightedAverageCosting : ICostingStrategy
{
    public decimal NewAverageCost(decimal quantityOnHand, decimal averageCost, decimal quantityIn, decimal unitCostIn)
    {
        if (quantityIn <= 0)
        {
            throw new DomainException("Incoming quantity must be positive to recost stock.");
        }

        // Stock can be negative if the business allows overselling and later reconciles. Averaging
        // against a negative on-hand would produce a nonsense cost (and can divide by zero), so the
        // incoming cost simply becomes the new average: it is the only real price we know.
        if (quantityOnHand <= 0)
        {
            return unitCostIn;
        }

        var totalValue = (quantityOnHand * averageCost) + (quantityIn * unitCostIn);
        var totalQuantity = quantityOnHand + quantityIn;

        // Four decimal places, matching numeric(18,4) in the database. Rounding here rather than
        // letting Postgres truncate keeps the value the code reasons about identical to the one stored.
        return Math.Round(totalValue / totalQuantity, 4, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// There is not enough stock to do what was asked. Surfaces as <b>422</b>, not 400: the request is
/// well-formed and would have been valid a minute ago — the world disagrees with it, not the schema
/// (api-design.md §4).
/// </summary>
public class InsufficientStockException : DomainException
{
    public InsufficientStockException(Guid productId, Guid warehouseId, decimal requested, decimal available)
        : base($"Not enough stock: {requested} requested, {available} available.")
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Requested = requested;
        Available = available;
    }

    public Guid ProductId { get; }
    public Guid WarehouseId { get; }
    public decimal Requested { get; }
    public decimal Available { get; }
}
