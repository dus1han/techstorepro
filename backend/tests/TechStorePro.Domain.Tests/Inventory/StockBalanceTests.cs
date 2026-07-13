using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Inventory;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Inventory;

/// <summary>
/// The balance is the arithmetic every other module trusts. These tests pin the two things that lose
/// money when they are wrong: the moving average, and the subtraction that prevents overselling.
/// </summary>
public class StockBalanceTests
{
    private static readonly ICostingStrategy Costing = new WeightedAverageCosting();

    private static StockBalance Balance(decimal quantity = 0, decimal averageCost = 0, decimal reserved = 0) =>
        new()
        {
            ProductId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            Quantity = quantity,
            AverageCost = averageCost,
            ReservedQuantity = reserved
        };

    // --- The moving average -------------------------------------------------------------------

    [Fact]
    public void The_first_receipt_sets_the_average_to_what_was_paid()
    {
        var balance = Balance();

        var cost = balance.ApplyInbound(MovementType.Receipt, 10, 100m, Costing);

        cost.Should().Be(100m);
        balance.Quantity.Should().Be(10);
        balance.AverageCost.Should().Be(100m);
    }

    [Fact]
    public void A_second_receipt_at_a_different_price_moves_the_average_between_the_two()
    {
        // 10 @ 100 then 10 @ 200 → 3,000 over 20 units → 150. The number a shop would compute on paper.
        var balance = Balance(quantity: 10, averageCost: 100m);

        balance.ApplyInbound(MovementType.Receipt, 10, 200m, Costing);

        balance.Quantity.Should().Be(20);
        balance.AverageCost.Should().Be(150m);
        balance.TotalValue.Should().Be(3_000m);
    }

    [Fact]
    public void The_average_is_weighted_by_quantity_not_by_a_plain_mean_of_the_prices()
    {
        // The bug this catches: averaging 100 and 200 to 150 while holding 90 units at 100 and 10 at 200.
        // Weighted correctly it is 110 — a plain mean would overstate the stock's value by 36%.
        var balance = Balance(quantity: 90, averageCost: 100m);

        balance.ApplyInbound(MovementType.Receipt, 10, 200m, Costing);

        balance.AverageCost.Should().Be(110m);
    }

    [Fact]
    public void Issuing_stock_leaves_the_average_exactly_where_it_was()
    {
        // A weighted average only moves when stock comes IN at a different price. If a sale could move
        // it, the cost of goods sold would depend on the order the sales happened to be keyed in.
        var balance = Balance(quantity: 20, averageCost: 150m);

        var cogs = balance.ApplyOutbound(MovementType.Sale, 5);

        cogs.Should().Be(150m);
        balance.AverageCost.Should().Be(150m);
        balance.Quantity.Should().Be(15);
    }

    [Fact]
    public void An_inbound_with_no_cost_leaves_the_average_untouched()
    {
        // A count surplus or a customer return is valued at what this warehouse already believes the
        // product is worth: quantity rose and total value rose in step, so the per-unit average did not.
        var balance = Balance(quantity: 10, averageCost: 100m);

        var cost = balance.ApplyInbound(MovementType.SaleReturn, 2, null, Costing);

        cost.Should().Be(100m);
        balance.AverageCost.Should().Be(100m);
        balance.Quantity.Should().Be(12);
    }

    [Theory]
    [InlineData(MovementType.Receipt)]
    [InlineData(MovementType.OpeningBalance)]
    [InlineData(MovementType.AdjustmentIn)]
    [InlineData(MovementType.TransferIn)]
    public void A_movement_that_carries_its_own_value_must_be_told_what_it_cost(MovementType type)
    {
        // Defaulting these to the existing average would make a receipt at a new price change nothing
        // at all — the moving average would never move.
        var balance = Balance(quantity: 10, averageCost: 100m);

        var act = () => balance.ApplyInbound(type, 5, null, Costing);

        act.Should().Throw<DomainException>().WithMessage("*must carry a unit cost*");
    }

    [Fact]
    public void The_average_is_rounded_to_four_places_to_match_the_column()
    {
        // 1 @ 10 + 2 @ 10.005 → 30.01 / 3 → 10.003333… The database column is numeric(18,4); rounding
        // here rather than letting Postgres truncate keeps the value the code reasons about identical
        // to the one stored.
        var balance = Balance(quantity: 1, averageCost: 10m);

        balance.ApplyInbound(MovementType.Receipt, 2, 10.005m, Costing);

        balance.AverageCost.Should().Be(10.0033m);
    }

    [Fact]
    public void Receiving_into_a_negative_balance_takes_the_incoming_price_as_the_new_average()
    {
        // Averaging against a negative on-hand produces a nonsense cost. The incoming price is the only
        // real number we have, so it becomes the average outright.
        var balance = Balance(quantity: -5, averageCost: 100m);

        balance.ApplyInbound(MovementType.Receipt, 10, 80m, Costing);

        balance.AverageCost.Should().Be(80m);
    }

    [Fact]
    public void A_negative_unit_cost_is_refused()
    {
        var balance = Balance();

        var act = () => balance.ApplyInbound(MovementType.Receipt, 1, -1m, Costing);

        act.Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    // --- Overselling --------------------------------------------------------------------------

    [Fact]
    public void Selling_more_than_is_available_is_refused()
    {
        var balance = Balance(quantity: 3, averageCost: 100m);

        var act = () => balance.ApplyOutbound(MovementType.Sale, 4);

        act.Should().Throw<InsufficientStockException>()
            .Which.Available.Should().Be(3);
    }

    [Fact]
    public void Reserved_stock_cannot_be_sold_to_somebody_else()
    {
        // Five on the shelf, four promised to a quote. The fifth is the only one still for sale — this
        // single assertion is what "prevent overselling" (requirements §20) actually means.
        var balance = Balance(quantity: 5, averageCost: 100m, reserved: 4);

        balance.AvailableQuantity.Should().Be(1);

        var act = () => balance.ApplyOutbound(MovementType.Sale, 2);

        act.Should().Throw<InsufficientStockException>()
            .Which.Available.Should().Be(1);
    }

    [Fact]
    public void A_sale_may_consume_the_reservation_it_holds()
    {
        // Without the allowance, delivering the four units you reserved would fail its own availability
        // check: the reservation would be competing with the sale that made it.
        var balance = Balance(quantity: 5, averageCost: 100m, reserved: 4);

        var act = () => balance.ApplyOutbound(MovementType.Sale, 4, allowanceFromReservation: 4);

        act.Should().NotThrow();
        balance.Quantity.Should().Be(1);
    }

    [Fact]
    public void A_reservation_allowance_does_not_let_a_sale_exceed_what_is_physically_there()
    {
        // The allowance unlocks reserved units; it does not conjure new ones.
        var balance = Balance(quantity: 5, averageCost: 100m, reserved: 4);

        var act = () => balance.ApplyOutbound(MovementType.Sale, 6, allowanceFromReservation: 4);

        act.Should().Throw<InsufficientStockException>();
    }

    [Fact]
    public void Stock_cannot_be_promised_twice()
    {
        var balance = Balance(quantity: 5, averageCost: 100m, reserved: 4);

        var act = () => balance.Reserve(2);

        act.Should().Throw<InsufficientStockException>()
            .Which.Available.Should().Be(1);
    }

    [Fact]
    public void Reserving_holds_stock_without_moving_it()
    {
        var balance = Balance(quantity: 5, averageCost: 100m);

        balance.Reserve(3);

        balance.Quantity.Should().Be(5);          // still on the shelf
        balance.ReservedQuantity.Should().Be(3);
        balance.AvailableQuantity.Should().Be(2); // but no longer sellable
    }

    [Fact]
    public void Releasing_more_than_was_reserved_is_refused()
    {
        // It would drive reserved_quantity negative and quietly make available stock that nobody has.
        var balance = Balance(quantity: 5, averageCost: 100m, reserved: 2);

        var act = () => balance.ReleaseReservation(3);

        act.Should().Throw<DomainException>().WithMessage("*only 2 are reserved*");
    }

    [Fact]
    public void Releasing_a_reservation_puts_the_stock_back_on_sale()
    {
        var balance = Balance(quantity: 5, averageCost: 100m, reserved: 3);

        balance.ReleaseReservation(3);

        balance.AvailableQuantity.Should().Be(5);
    }

    // --- Direction and magnitude --------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_movement_must_have_a_positive_magnitude(decimal quantity)
    {
        // A negative magnitude is a caller trying to decide the direction for itself. MovementType is
        // the only thing allowed to — otherwise an "outbound" of −5 would raise the warehouse.
        var balance = Balance(quantity: 10, averageCost: 100m);

        var inbound = () => balance.ApplyInbound(MovementType.Receipt, quantity, 10m, Costing);
        var outbound = () => balance.ApplyOutbound(MovementType.Sale, quantity);

        inbound.Should().Throw<DomainException>().WithMessage("*greater than zero*");
        outbound.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void An_outbound_type_cannot_be_applied_as_an_inbound()
    {
        var balance = Balance(quantity: 10, averageCost: 100m);

        var act = () => balance.ApplyInbound(MovementType.Sale, 1, 10m, Costing);

        act.Should().Throw<DomainException>().WithMessage("*not an inbound*");
    }

    [Fact]
    public void An_inbound_type_cannot_be_applied_as_an_outbound()
    {
        var balance = Balance(quantity: 10, averageCost: 100m);

        var act = () => balance.ApplyOutbound(MovementType.Receipt, 1);

        act.Should().Throw<DomainException>().WithMessage("*not an outbound*");
    }

    [Fact]
    public void Every_movement_type_has_a_direction()
    {
        // A new type added without a direction throws at the point of use, which could be a sale on a
        // Friday night. Enumerating them here means it fails in CI instead.
        foreach (var type in Enum.GetValues<MovementType>())
        {
            var act = () => type.Direction();

            act.Should().NotThrow($"{type} must declare whether it raises, lowers, or does not move stock");
        }
    }

    [Fact]
    public void Exactly_one_movement_type_moves_money_without_moving_units()
    {
        // Revaluation is the sole exception to "every movement moves stock", and it is worth pinning
        // as a fact rather than leaving as a convention: it is the landed cost of an import folded
        // into goods received weeks earlier, and a second type sneaking in with direction zero would
        // silently acquire the same power to change the valuation without a unit changing hands.
        var zeroDirection = Enum.GetValues<MovementType>()
            .Where(t => t.Direction() == 0)
            .ToList();

        zeroDirection.Should().Equal(MovementType.Revaluation);
    }

    [Fact]
    public void Inbound_outbound_and_revaluation_are_exhaustive_and_exclusive()
    {
        // Callers branch on these. If a type were somehow none of the three — or two of them — the
        // branch that handles it would be whichever the author happened to write first.
        foreach (var type in Enum.GetValues<MovementType>())
        {
            var matches = new[] { type.IsInbound(), type.IsOutbound(), type.IsRevaluation() }
                .Count(x => x);

            matches.Should().Be(1, $"{type} must be exactly one of inbound, outbound or revaluation");
        }
    }

    // --- Revaluation: money without units -------------------------------------------------------

    [Fact]
    public void Landed_cost_raises_the_average_without_creating_a_single_unit()
    {
        // The container was unpacked in March and the clearing agent invoiced in April. The freight
        // belongs to those ten laptops, and they are still on the shelf.
        var balance = Balance(quantity: 10, averageCost: 1_000m);

        var average = balance.ApplyRevaluation(2_000m);

        average.Should().Be(1_200m);
        balance.Quantity.Should().Be(10, "freight does not conjure laptops");
        balance.TotalValue.Should().Be(12_000m);
    }

    [Fact]
    public void Landed_cost_with_nothing_left_to_carry_it_is_refused()
    {
        // The shipment sold out before the clearing invoice arrived. There is no stock for the cost to
        // attach to — spreading it over an empty balance would divide by zero, and spreading it over
        // the *next* shipment's units would charge one container's freight to another's goods.
        var balance = Balance(quantity: 0, averageCost: 1_000m);

        var act = () => balance.ApplyRevaluation(2_000m);

        act.Should().Throw<DomainException>().WithMessage("*no stock left to carry this cost*");
    }

    [Fact]
    public void A_revaluation_of_zero_is_refused()
    {
        var balance = Balance(quantity: 10, averageCost: 1_000m);

        var act = () => balance.ApplyRevaluation(0m);

        act.Should().Throw<DomainException>().WithMessage("*revalues nothing*");
    }

    [Fact]
    public void Stock_cannot_be_revalued_to_less_than_nothing()
    {
        // A credit note larger than the stock's whole value means something upstream is wrong. A
        // negative average would poison the COGS of every future sale of the product.
        var balance = Balance(quantity: 10, averageCost: 100m);

        var act = () => balance.ApplyRevaluation(-2_000m);

        act.Should().Throw<DomainException>().WithMessage("*less than nothing*");
    }

    [Fact]
    public void A_credit_note_can_lower_the_average()
    {
        // The freight was over-billed and the agent refunded some of it. That has to come back out of
        // the stock's value, or the shop keeps selling at a cost it never actually paid.
        var balance = Balance(quantity: 10, averageCost: 1_200m);

        balance.ApplyRevaluation(-1_000m).Should().Be(1_100m);
    }
}
