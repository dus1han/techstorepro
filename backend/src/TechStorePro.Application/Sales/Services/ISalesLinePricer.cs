namespace TechStorePro.Application.Sales.Services;

/// <summary>
/// One priced sales line, ready to be written onto a document.
/// </summary>
/// <param name="RequiresApproval">
/// True when the line has been discounted below the floor on its price list
/// (<c>PriceListItem.MinimumPrice</c>) — the "discount limits / manager approval" of requirements §32.
/// The pricer <em>reports</em> this; it does not decide what to do about it, because a quotation may
/// legitimately be drafted below the floor and only needs the signature before it is invoiced.
/// </param>
public record PricedLine(
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxPercent,
    string PriceSource,
    decimal NetTotal,
    decimal? MinimumPrice,
    bool RequiresApproval);

/// <summary>
/// Turns "this product, for this customer, at this quantity" into money.
///
/// It exists so that a quotation, an order and an invoice cannot price the same line three different
/// ways. Every sales handler goes through here, and what it returns is <b>snapshotted</b> onto the
/// document — the price and the tax rate are answers for a moment, not links to rows that will change.
/// </summary>
public interface ISalesLinePricer
{
    /// <param name="unitPriceOverride">
    /// What the salesperson actually typed. Null means "whatever the price list says" — the normal case.
    /// An override is not a discount and is not treated as one: it is still checked against the floor,
    /// because typing 1,000 into the box is exactly as much of a giveaway as discounting to 1,000.
    /// </param>
    Task<PricedLine> PriceAsync(
        Guid productId,
        Guid? customerId,
        decimal quantity,
        decimal? unitPriceOverride = null,
        decimal discountPercent = 0m,
        decimal discountAmount = 0m,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);
}
