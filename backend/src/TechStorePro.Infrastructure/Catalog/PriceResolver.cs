using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Catalog;

/// <inheritdoc cref="IPriceResolver"/>
public class PriceResolver : IPriceResolver
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public PriceResolver(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ResolvedPrice> ResolveAsync(
        Guid productId,
        Guid? customerId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var at = asOf ?? _clock.UtcNow;

        var product = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new NotFoundException("Product", productId);

        // The customer's tier, if they have one. A walk-in has none, and that is normal.
        Guid? tierId = null;

        if (customerId is { } id)
        {
            tierId = await _db.Customers
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => c.PriceTierId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (tierId is { } customerTier
            && await FindPriceAsync(customerTier, productId, at, cancellationToken) is { } tierPrice)
        {
            return tierPrice with { Source = $"Price list ({tierPrice.Source})" };
        }

        // Fall back to the default tier before falling back to the product. A shop that has set up a
        // "Retail" list expects a walk-in to be charged from it, not from whatever number happens to
        // sit on the product record.
        var defaultTierId = await _db.PriceTiers
            .AsNoTracking()
            .Where(t => t.IsDefault && t.IsActive)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultTierId is { } fallbackTier
            && fallbackTier != tierId
            && await FindPriceAsync(fallbackTier, productId, at, cancellationToken) is { } defaultPrice)
        {
            return defaultPrice with { Source = $"Default price list ({defaultPrice.Source})" };
        }

        return new ResolvedPrice(product.SellingPrice, null, "Product default price");
    }

    /// <summary>
    /// The price for this product on the tier's price list that is in force at <paramref name="at"/>.
    ///
    /// Overlapping lists are prevented on write, so at most one can be in force — but this orders by
    /// ValidFrom descending anyway, so that if bad data ever slips in the <em>newest</em> list wins
    /// rather than an arbitrary one.
    /// </summary>
    private async Task<ResolvedPrice?> FindPriceAsync(
        Guid tierId,
        Guid productId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var match = await _db.PriceListItems
            .AsNoTracking()
            .Where(i => i.ProductId == productId
                        && i.PriceList.PriceTierId == tierId
                        && i.PriceList.IsActive
                        && i.PriceList.ValidFrom <= at
                        && (i.PriceList.ValidTo == null || i.PriceList.ValidTo > at))
            .OrderByDescending(i => i.PriceList.ValidFrom)
            .Select(i => new { i.UnitPrice, i.MinimumPrice, ListName = i.PriceList.Name })
            .FirstOrDefaultAsync(cancellationToken);

        return match is null
            ? null
            : new ResolvedPrice(match.UnitPrice, match.MinimumPrice, match.ListName);
    }
}
