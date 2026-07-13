namespace TechStorePro.Application.Catalog.Services;

/// <summary>
/// The tax rate that applies to a product at a moment, and why.
///
/// <see cref="Source"/> is the same idea as <see cref="ResolvedPrice.Source"/>: "why was this line
/// taxed at 5%?" is a question an auditor asks, and "because the product points at the Standard VAT
/// rate, which was in force that day" is the answer.
/// </summary>
public record ResolvedTax(
    decimal Percent,
    string Source);

/// <summary>
/// Resolves the tax rate for a sales line (requirements §45 <b>D7</b>).
///
/// <b>No jurisdiction is hardcoded.</b> There is no "VAT" constant and no 5 anywhere in this codebase:
/// every company defines its own effective-dated <c>tax_rates</c>, and a shop in a country with no sales
/// tax simply configures none. That is why the rate is resolved rather than assumed — the alternative
/// would be a system that can only be sold in one country.
///
/// The result is <b>snapshotted onto the line</b>, never referenced. See <c>SalesInvoiceLine.TaxPercent</c>.
/// </summary>
public interface ITaxResolver
{
    /// <summary>
    /// Resolution order:
    /// <list type="number">
    /// <item>the rate the product points at, if it is in force at <paramref name="asOf"/>;</item>
    /// <item>the company's default rate in force at that moment;</item>
    /// <item>zero — no rate configured means no tax, which is a legitimate answer, not an error.</item>
    /// </list>
    /// A product pointing at a rate that has since been superseded falls back to the default rather than
    /// charging a rate the tax authority has retired.
    /// </summary>
    Task<ResolvedTax> ResolveAsync(
        Guid productId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);
}
