using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Purchasing;

/// <summary>How a charge is spread across the lines of a shipment (requirements §26, decision D6).</summary>
public enum ApportionmentBasis : short
{
    /// <summary>
    /// In proportion to line value. <b>The decided default (D6).</b> It is what customs duty genuinely
    /// follows — duty is levied on value — and it needs no data the system does not already hold.
    /// </summary>
    ByValue = 1,

    /// <summary>
    /// In proportion to line weight. Truest for freight, because weight is what the shipping line
    /// actually charges for. <b>Not selectable yet:</b> it requires a weight on every product, which
    /// the catalogue does not store. The enum member exists so that adding it later is a new case
    /// rather than a rewrite of every call site — see <see cref="LandedCostApportionment"/>.
    /// </summary>
    ByWeight = 2,

    /// <summary>
    /// Evenly per unit. Loads the same freight onto a cable as onto a laptop, which makes the margin
    /// on cheap, high-count items nonsense. Rejected as a default in D6; kept because a shop shipping
    /// one uniform product may legitimately want it.
    /// </summary>
    ByQuantity = 3
}

/// <summary>One line of a shipment, as the apportionment sees it: an id, a quantity and a value.</summary>
public record ApportionableLine(Guid LineId, decimal Quantity, decimal LineValue);

/// <summary>What one line ended up carrying.</summary>
public record ApportionedCharge(Guid LineId, decimal Amount);

/// <summary>
/// Spreads the charges of an import — freight, insurance, customs, clearing — across its lines
/// (requirements §26).
///
/// <b>This is the highest-leverage arithmetic in the purchasing module, and it is pure on purpose.</b>
/// Costing is weighted average (D1), so a landed-cost error does not merely misprice the container: it
/// feeds the moving average and spreads to <em>every existing unit</em> of that product, where it never
/// washes out. Being a pure function of numbers, it can be tested against the worked examples the
/// business gave — which is exactly what development-plan.md asked for before a line of it was written.
///
/// <b>Every fils is accounted for.</b> Proportional shares do not divide evenly, and a naïve
/// implementation quietly loses the remainder — a shipment charged AED 1,000 whose lines sum to 999.99,
/// forever, on every import. So the shares are rounded down and the remainder is handed to the largest
/// line, which is both the least distorting place to put it and a rule someone can check by hand.
/// </summary>
public static class LandedCostApportionment
{
    /// <summary>
    /// Splits <paramref name="chargeTotal"/> across <paramref name="lines"/>.
    ///
    /// The returned amounts sum to <paramref name="chargeTotal"/> <b>exactly</b>. That is the postcondition
    /// worth remembering: the shop paid a number, and inventory must absorb that number, to the fils.
    /// </summary>
    public static IReadOnlyList<ApportionedCharge> Apportion(
        IReadOnlyCollection<ApportionableLine> lines,
        decimal chargeTotal,
        ApportionmentBasis basis = ApportionmentBasis.ByValue)
    {
        if (lines.Count == 0)
        {
            throw new DomainException("A shipment with no lines has nothing to apportion its costs over.");
        }

        if (chargeTotal < 0)
        {
            throw new DomainException("A landed-cost charge cannot be negative. Record a credit instead.");
        }

        if (basis == ApportionmentBasis.ByWeight)
        {
            // Refused rather than silently falling back to value. A caller who asked for weight and got
            // value would be told the cost was apportioned the way they asked, and it would not have been.
            throw new DomainException(
                "Apportionment by weight needs a weight on every product, and the catalogue does not "
                + "store one. Use ByValue (the default) or ByQuantity.");
        }

        var weights = lines.ToDictionary(
            l => l.LineId,
            l => basis switch
            {
                ApportionmentBasis.ByValue => l.LineValue,
                ApportionmentBasis.ByQuantity => l.Quantity,
                _ => throw new DomainException($"Unknown apportionment basis {basis}.")
            });

        var total = weights.Values.Sum();

        if (total <= 0)
        {
            // Every line is worth nothing (or counts nothing), so there is no ratio to divide by. Free
            // samples in a paid container are the real case, and the honest split is an even one —
            // somebody still paid the freight.
            return SplitEvenly(lines, chargeTotal);
        }

        var results = new List<ApportionedCharge>(lines.Count);
        var allocated = 0m;

        foreach (var line in lines)
        {
            // Truncated to the fils, deliberately: rounding each share to nearest would let the shares
            // sum to *more* than the charge, and inventory would absorb money the shop never spent.
            var share = Math.Round(chargeTotal * weights[line.LineId] / total, 2, MidpointRounding.ToZero);

            results.Add(new ApportionedCharge(line.LineId, share));
            allocated += share;
        }

        var remainder = chargeTotal - allocated;

        if (remainder != 0)
        {
            // To the largest line. It distorts that line's unit cost the least in percentage terms, and
            // "the odd fils goes on the biggest line" is a rule an accountant can verify by hand — which
            // matters more than mathematical elegance when someone is reconciling a container.
            var largest = lines
                .OrderByDescending(l => weights[l.LineId])
                .ThenBy(l => l.LineId)
                .First();

            var index = results.FindIndex(r => r.LineId == largest.LineId);
            results[index] = results[index] with { Amount = results[index].Amount + remainder };
        }

        return results;
    }

    private static List<ApportionedCharge> SplitEvenly(
        IReadOnlyCollection<ApportionableLine> lines,
        decimal chargeTotal)
    {
        var each = Math.Round(chargeTotal / lines.Count, 2, MidpointRounding.ToZero);

        var results = lines
            .Select(l => new ApportionedCharge(l.LineId, each))
            .ToList();

        var remainder = chargeTotal - (each * lines.Count);

        if (remainder != 0 && results.Count > 0)
        {
            results[0] = results[0] with { Amount = results[0].Amount + remainder };
        }

        return results;
    }
}
