using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Purchasing;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Purchasing;

/// <summary>
/// The landed-cost arithmetic, pinned to the worked example the business agreed (requirements §45 D6).
///
/// Development-plan.md asked for exactly this and in exactly this order: worked examples from the
/// business, <em>turned into tests, before the code</em>. The reason is that costing is weighted
/// average, so an error here does not merely misprice one container — it feeds the moving average and
/// spreads to every existing unit of the product, where it never washes out.
/// </summary>
public class LandedCostTests
{
    private static readonly Guid Laptops = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Cables = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>The D6 shipment: 10 laptops at 1,000 and 100 cables at 50, carrying AED 3,000 of charges.</summary>
    private static readonly ApportionableLine[] TheShipment =
    [
        new(Laptops, Quantity: 10, LineValue: 10_000m),
        new(Cables, Quantity: 100, LineValue: 5_000m)
    ];

    [Fact]
    public void The_agreed_worked_example()
    {
        // AED 3,000 over line values of 10,000 and 5,000.
        //   laptops: 3000 × 10000/15000 = 2,000  → +200.00 a unit → they land at 1,200
        //   cables:  3000 ×  5000/15000 = 1,000  → + 10.00 a unit → they land at    60
        var result = LandedCostApportionment.Apportion(TheShipment, 3_000m);

        result.Single(r => r.LineId == Laptops).Amount.Should().Be(2_000m);
        result.Single(r => r.LineId == Cables).Amount.Should().Be(1_000m);

        // And the unit costs the business signed off on.
        (1_000m + (2_000m / 10)).Should().Be(1_200m);
        (50m + (1_000m / 100)).Should().Be(60m);
    }

    [Fact]
    public void Every_fils_of_the_charge_reaches_inventory()
    {
        // The postcondition that matters. The shop paid a number; inventory must absorb that number.
        // Proportional shares almost never divide evenly, and the naïve version quietly loses the
        // remainder — a 1,000 charge whose lines sum to 999.99, on every single import, forever.
        var awkward = new ApportionableLine[]
        {
            new(Guid.NewGuid(), 1, 333.33m),
            new(Guid.NewGuid(), 1, 333.33m),
            new(Guid.NewGuid(), 1, 333.34m)
        };

        var result = LandedCostApportionment.Apportion(awkward, 1_000m);

        result.Sum(r => r.Amount).Should().Be(1_000m);
    }

    [Fact]
    public void The_remainder_goes_to_the_largest_line()
    {
        // Not to the first line, and not spread as a fraction of a fils nobody can represent. The
        // largest line is where the odd fils distorts the unit cost least — and "it goes on the
        // biggest line" is a rule an accountant can check by hand while reconciling a container.
        var lines = new ApportionableLine[]
        {
            new(Cables, 1, 1m),
            new(Laptops, 1, 2m)
        };

        // 10 / 3 does not divide: shares truncate to 3.33 and 6.66, leaving 0.01.
        var result = LandedCostApportionment.Apportion(lines, 10m);

        result.Single(r => r.LineId == Cables).Amount.Should().Be(3.33m);
        result.Single(r => r.LineId == Laptops).Amount.Should().Be(6.67m, "the odd fils lands on the larger line");
        result.Sum(r => r.Amount).Should().Be(10m);
    }

    [Fact]
    public void Shares_never_sum_to_more_than_the_charge()
    {
        // Each share is truncated rather than rounded to nearest. Rounding up would let inventory
        // absorb money the shop never actually spent — which is worse than losing a fils, because it
        // inflates the value of stock and every margin computed from it.
        var lines = new ApportionableLine[]
        {
            new(Guid.NewGuid(), 1, 1m),
            new(Guid.NewGuid(), 1, 1m),
            new(Guid.NewGuid(), 1, 1m)
        };

        var result = LandedCostApportionment.Apportion(lines, 1m);

        result.Sum(r => r.Amount).Should().Be(1m);
        result.Count(r => r.Amount == 0.33m).Should().Be(2);
        result.Count(r => r.Amount == 0.34m).Should().Be(1);
    }

    [Fact]
    public void By_quantity_loads_the_same_freight_onto_a_cable_as_a_laptop()
    {
        // Kept because a shop shipping one uniform product may want it — and shown here so the reason
        // it is not the default is visible: the cables end up carrying 2,727 of the 3,000.
        var result = LandedCostApportionment.Apportion(
            TheShipment,
            3_000m,
            ApportionmentBasis.ByQuantity);

        result.Single(r => r.LineId == Laptops).Amount.Should().Be(272.72m);
        result.Single(r => r.LineId == Cables).Amount.Should().Be(2_727.28m);
        result.Sum(r => r.Amount).Should().Be(3_000m);

        // A 50-dirham cable would land at 77.27. More than a third of its cost would be freight.
        (50m + (2_727.28m / 100)).Should().BeApproximately(77.27m, 0.01m);
    }

    [Fact]
    public void By_weight_is_refused_rather_than_silently_falling_back_to_value()
    {
        // It needs a weight on every product and the catalogue stores none. A caller who asked for
        // weight and quietly got value would be told the cost was apportioned the way they asked, and
        // it would not have been.
        var act = () => LandedCostApportionment.Apportion(TheShipment, 3_000m, ApportionmentBasis.ByWeight);

        act.Should().Throw<DomainException>().WithMessage("*needs a weight on every product*");
    }

    [Fact]
    public void A_container_of_free_samples_still_splits_its_freight()
    {
        // Every line is worth nothing, so there is no ratio to divide by — but somebody still paid the
        // shipping company. An even split is the honest answer; a divide-by-zero is not.
        var freebies = new ApportionableLine[]
        {
            new(Laptops, 1, 0m),
            new(Cables, 1, 0m)
        };

        var result = LandedCostApportionment.Apportion(freebies, 100m);

        result.Sum(r => r.Amount).Should().Be(100m);
        result.Should().OnlyContain(r => r.Amount == 50m);
    }

    [Fact]
    public void A_shipment_with_no_lines_has_nothing_to_apportion_over()
    {
        var act = () => LandedCostApportionment.Apportion([], 100m);

        act.Should().Throw<DomainException>().WithMessage("*no lines*");
    }

    [Fact]
    public void A_negative_charge_is_refused()
    {
        var act = () => LandedCostApportionment.Apportion(TheShipment, -1m);

        act.Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public void A_charge_of_nothing_apportions_nothing()
    {
        var result = LandedCostApportionment.Apportion(TheShipment, 0m);

        result.Sum(r => r.Amount).Should().Be(0m);
    }
}
