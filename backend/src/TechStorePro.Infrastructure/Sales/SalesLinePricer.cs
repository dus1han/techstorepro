using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Sales;

namespace TechStorePro.Infrastructure.Sales;

/// <inheritdoc cref="ISalesLinePricer"/>
public class SalesLinePricer : ISalesLinePricer
{
    private readonly IPriceResolver _prices;
    private readonly ITaxResolver _taxes;

    public SalesLinePricer(IPriceResolver prices, ITaxResolver taxes)
    {
        _prices = prices;
        _taxes = taxes;
    }

    public async Task<PricedLine> PriceAsync(
        Guid productId,
        Guid? customerId,
        decimal quantity,
        decimal? unitPriceOverride = null,
        decimal discountPercent = 0m,
        decimal discountAmount = 0m,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _prices.ResolveAsync(productId, customerId, asOf, cancellationToken);
        var tax = await _taxes.ResolveAsync(productId, asOf, cancellationToken);

        var unitPrice = unitPriceOverride ?? resolved.UnitPrice;

        var net = SalesMath.Net(quantity, unitPrice, discountPercent, discountAmount);

        // The floor is a price per unit, so compare per unit — not the line total, which would let a
        // large quantity hide a giveaway inside a big number.
        var effectiveUnitPrice = quantity > 0 ? net / quantity : 0m;

        var requiresApproval = resolved.MinimumPrice is { } floor && effectiveUnitPrice < floor;

        var source = unitPriceOverride is null
            ? resolved.Source
            : $"Manual price (list said {resolved.UnitPrice:0.##} — {resolved.Source})";

        return new PricedLine(
            UnitPrice: unitPrice,
            DiscountPercent: discountPercent,
            DiscountAmount: discountAmount,
            TaxPercent: tax.Percent,
            PriceSource: source,
            NetTotal: net,
            MinimumPrice: resolved.MinimumPrice,
            RequiresApproval: requiresApproval);
    }
}
