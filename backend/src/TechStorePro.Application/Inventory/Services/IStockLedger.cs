using TechStorePro.Domain.Inventory;

namespace TechStorePro.Application.Inventory.Services;

/// <summary>
/// One stock change, as the caller describes it. The ledger decides the sign, the cost and the
/// serial transitions — the caller only says what happened.
/// </summary>
/// <param name="Quantity">
/// A positive magnitude, always. The direction comes from <paramref name="Type"/>; a caller that
/// could pass a negative quantity to an outbound movement is a caller that can invert a warehouse.
/// </param>
/// <param name="UnitCost">
/// Required for a movement that carries its own cost (a receipt, an opening balance, a write-on) and
/// ignored otherwise: everything else is valued at the warehouse's current average.
/// </param>
/// <param name="SerialNumbers">
/// Required for a serial-tracked product, and its count must equal <paramref name="Quantity"/>.
/// Every serial becomes its own one-unit movement, because that is what makes a warranty claim
/// answerable two years later.
/// </param>
/// <param name="ReservationId">
/// The reservation this movement is consuming, if any. Without it, delivering the two units you
/// reserved would fail its own availability check — the reservation would be competing with the sale
/// that made it.
/// </param>
/// <param name="OccurredAt">
/// When the stock physically moved, which is not always now: a receipt can be backdated to the day
/// the van arrived. Historical stock replays on this. Defaults to the current time.
/// </param>
public record StockPosting(
    Guid WarehouseId,
    Guid BranchId,
    Guid ProductId,
    MovementType Type,
    decimal Quantity,
    StockReferenceType ReferenceType,
    Guid? ReferenceId = null,
    string? ReferenceNumber = null,
    decimal? UnitCost = null,
    IReadOnlyCollection<string>? SerialNumbers = null,
    Guid? ReservationId = null,
    DateTimeOffset? OccurredAt = null,
    string? Notes = null);

/// <param name="UnitCost">
/// What the movement was actually valued at — the incoming cost on a purchase, the warehouse average
/// on an issue. A sale snapshots this onto its invoice line as COGS; it must not be recomputed later,
/// because the average will have moved by then.
/// </param>
public record StockPostingResult(
    IReadOnlyList<StockMovement> Movements,
    StockBalance Balance,
    decimal UnitCost);

/// <summary>
/// <b>The single door into the stock ledger.</b> Nothing else in the system may write
/// <c>stock_movements</c>, <c>stock_balances</c> or a serial's status — not purchasing, not sales, not
/// repairs (architecture.md §4.5).
///
/// That is not a style preference. The invariants below only hold if there is exactly one place that
/// enforces them:
///
/// <list type="bullet">
/// <item>the movement and the balance it changes are written in the <b>same transaction</b>, so the
///   cache can never be one commit ahead of the ledger;</item>
/// <item>the balance row is <b>locked</b> before it is read, so two concurrent sales of the last unit
///   cannot both pass their availability check;</item>
/// <item>the weighted average is recomputed <b>inside that lock</b>, so a receipt and a sale racing
///   each other cannot interleave into a cost that never existed;</item>
/// <item>a serial-tracked unit is moved by exactly one movement, so the same laptop cannot be sold
///   twice.</item>
/// </list>
///
/// Every method here <b>requires an ambient transaction</b> and throws without one. That is deliberate:
/// a stock movement is never the only thing a business operation does — a delivery also updates an
/// order, a receipt also updates a GRN — and a ledger that quietly committed on its own would leave
/// stock moved and the document that moved it rolled back.
/// </summary>
public interface IStockLedger
{
    /// <summary>
    /// Appends to the ledger and updates the balance, under lock, in the caller's transaction.
    /// Throws <see cref="InsufficientStockException"/> (422) if an outbound movement would oversell.
    /// </summary>
    Task<StockPostingResult> PostAsync(StockPosting posting, CancellationToken cancellationToken = default);

    /// <summary>
    /// Promises stock without moving it (requirements §20). Raises <c>reserved_quantity</c>, which is
    /// what comes off <see cref="AvailableAsync"/> — this, and nothing else, is "prevent overselling".
    /// </summary>
    Task<StockReservation> ReserveAsync(
        Guid warehouseId,
        Guid productId,
        decimal quantity,
        StockReferenceType referenceType,
        Guid? referenceId,
        string? referenceNumber,
        DateTimeOffset? expiresAt,
        Guid? serialId = null,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>Gives a reservation back. Idempotent in effect: releasing a released reservation throws.</summary>
    Task ReleaseAsync(Guid reservationId, bool expired = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>quantity − reserved_quantity</c>. Zero if the product has never been in this warehouse —
    /// "no balance row" and "a balance of nothing" are the same answer to a caller.
    /// </summary>
    Task<decimal> AvailableAsync(Guid warehouseId, Guid productId, CancellationToken cancellationToken = default);
}
