using TechStorePro.Application.Catalog.Services;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Catalog;

/// <inheritdoc cref="ITaxResolver"/>
public class TaxResolver : ITaxResolver
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public TaxResolver(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ResolvedTax> ResolveAsync(
        Guid productId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var at = asOf ?? _clock.UtcNow;

        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.TaxRateId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Product", productId);

        if (product.TaxRateId is { } rateId)
        {
            var onProduct = await _db.TaxRates
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == rateId, cancellationToken);

            // IsInForceAt, not merely "exists": a rate that was superseded last year is still on the
            // product, and charging it today would bill the customer at a rate the tax authority has
            // retired. Falling through to the default is the safe answer, and the Source says so.
            if (onProduct is not null && onProduct.IsInForceAt(at))
            {
                return new ResolvedTax(onProduct.Percent, $"Product tax rate ({onProduct.Name})");
            }
        }

        var fallback = await _db.TaxRates
            .AsNoTracking()
            .Where(r => r.IsDefault && r.IsActive && r.ValidFrom <= at
                        && (r.ValidTo == null || r.ValidTo > at))
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (fallback is not null)
        {
            return new ResolvedTax(fallback.Percent, $"Default tax rate ({fallback.Name})");
        }

        // No rate configured at all. That is not a misconfiguration to shout about: a company in a
        // jurisdiction with no sales tax configures none, and every line is legitimately taxed at zero.
        return new ResolvedTax(0m, "No tax rate in force");
    }
}
