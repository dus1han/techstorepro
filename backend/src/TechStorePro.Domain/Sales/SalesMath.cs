namespace TechStorePro.Domain.Sales;

/// <summary>
/// The arithmetic of a sales line, written once (requirements §45 <b>D7</b>).
///
/// <b>Prices are tax-exclusive.</b> The stored unit price is what the customer is charged before tax;
/// tax is computed on the discounted net and added. Every sales document — quotation, order, invoice,
/// credit note — computes its money through here, so a shop can never be shown a quote that totals one
/// way and an invoice for the same lines that totals another.
///
/// The order of operations is the part that matters and the part that is easy to get wrong: discount
/// comes off <em>before</em> tax. Taxing the gross and then discounting would charge the customer tax on
/// money they never paid.
/// </summary>
public static class SalesMath
{
    /// <summary>
    /// Rounded at the line, not at the total. Two decimals is what the customer is actually charged, and
    /// a total assembled from unrounded lines can disagree with the sum of the lines as printed —
    /// the classic "invoice is off by one fils" that nobody can explain.
    /// </summary>
    public static decimal Round(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal Gross(decimal quantity, decimal unitPrice) =>
        Round(quantity * unitPrice);

    /// <summary>
    /// Percentage first, then the fixed amount. Both are supported because §32 offers both, and a line
    /// may carry a tier discount (a percentage) and a haggled amount off (a figure) at the same time.
    /// Never below zero: a discount that exceeds the line would owe the customer money for buying.
    /// </summary>
    public static decimal Net(decimal quantity, decimal unitPrice, decimal discountPercent, decimal discountAmount)
    {
        var gross = Gross(quantity, unitPrice);
        var afterPercent = gross - Round(gross * discountPercent / 100m);

        return Math.Max(0m, Round(afterPercent - discountAmount));
    }

    /// <summary>Tax on the discounted net — never on the gross.</summary>
    public static decimal Tax(decimal net, decimal taxPercent) =>
        Round(net * taxPercent / 100m);
}
