using TechStorePro.Domain.Repairs;

namespace TechStorePro.Application.Repairs.Services;

/// <summary>
/// What the shop knows about the machine on the counter.
/// </summary>
/// <param name="SerialId">
/// The unit, if the shop can find it. Null means "we have never seen this serial", which is a perfectly
/// ordinary answer — a customer may bring in a laptop they bought elsewhere.
/// </param>
/// <param name="SoldInvoiceLineId">
/// The invoice line that sold it. <b>This is the back-edge into Sales</b> (architecture.md §2), reached by
/// following <c>Serial.SoldInvoiceLineId</c> — the link P5 binds at delivery precisely so that this lookup
/// can exist two years later.
/// </param>
/// <param name="WarrantyType">Who pays. <see cref="RepairWarrantyType.None"/> means the customer does.</param>
/// <param name="WarrantyId">
/// The registered warranty being relied on, when it is a manufacturer's or a supplier's. Null for the
/// shop's own, which is derived from the sale rather than registered — see <see cref="Warranty"/>.
/// </param>
/// <param name="CoveredUntil">When the cover runs out. Null when there is none.</param>
/// <param name="Explanation">
/// Why the answer is what it is, in words a person can repeat to a customer. "Shop warranty, sold on
/// invoice INV-00042, expires 14 Aug 2027" ends an argument; a bare boolean starts one.
/// </param>
public record WarrantyCover(
    Guid? SerialId,
    Guid? ProductId,
    Guid? SoldInvoiceLineId,
    RepairWarrantyType WarrantyType,
    Guid? WarrantyId,
    DateTimeOffset? CoveredUntil,
    string Explanation)
{
    public bool IsCovered => WarrantyType != RepairWarrantyType.None;
}

/// <summary>
/// Answers the only question that matters at intake: <b>is this repair free, and if so, who is paying?</b>
///
/// It checks two sources, because the system knows about warranties in two entirely different ways:
///
/// <list type="number">
/// <item><b>The shop's own warranty is derived, not stored.</b> P5 stamps <c>Serial.WarrantyUntil</c> at
///   the moment of sale, from the product's <c>WarrantyMonths</c>. Nothing needs to register it.</item>
/// <item><b>A manufacturer's or supplier's warranty is registered</b> (<see cref="Warranty"/>). Nobody can
///   compute it — it is a term somebody was given on paper.</item>
/// </list>
///
/// Where both cover the machine, <b>the manufacturer's or supplier's wins</b>, and that is not an
/// arbitrary tie-break: if someone else will pay for the board, the shop should not be eating it out of
/// its own warranty provision. Getting this backwards costs real money and nobody would ever notice.
/// </summary>
public interface IWarrantyLookup
{
    Task<WarrantyCover> FindAsync(
        string? serialNumber,
        Guid? productId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}
