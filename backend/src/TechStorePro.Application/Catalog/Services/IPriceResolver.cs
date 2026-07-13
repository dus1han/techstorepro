namespace TechStorePro.Application.Catalog.Services;

/// <summary>
/// What one unit costs a given customer, at a given moment, and why.
///
/// <see cref="Source"/> exists so the answer is explainable. "Why is this laptop 3,400 for Omar and
/// 3,600 for a walk-in?" is a question the counter gets asked, and an ERP that cannot answer it
/// erodes trust in every other number it shows.
/// </summary>
public record ResolvedPrice(
    decimal UnitPrice,
    decimal? MinimumPrice,
    string Source);

/// <summary>
/// Resolves the selling price for a product and customer.
///
/// It exists in P2 rather than P5 because it is the whole point of price tiers: a price list nobody
/// consults is decoration. P5's invoice lines will call this and then <b>snapshot</b> the result onto
/// the line — the resolved price is an answer for a moment, not a permanent link.
/// </summary>
public interface IPriceResolver
{
    /// <summary>
    /// Resolution order:
    /// <list type="number">
    /// <item>the price list in force for the customer's tier, if it prices this product;</item>
    /// <item>the price list in force for the <em>default</em> tier;</item>
    /// <item>the product's own selling price.</item>
    /// </list>
    /// Never returns null: there is always a price, or the product could not be sold at all.
    /// </summary>
    Task<ResolvedPrice> ResolveAsync(
        Guid productId,
        Guid? customerId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);
}
