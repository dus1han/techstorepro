using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Inventory;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Inventory;

/// <summary>
/// A transfer is two movements with a gap between them, and the gap is the point: stock that has left
/// one warehouse and not arrived at the other belongs to neither.
/// </summary>
public class StockTransferTests
{
    private static StockTransfer Transfer(TransferStatus status = TransferStatus.Draft) =>
        new()
        {
            Number = "TRF-2026-00001",
            FromWarehouseId = Guid.NewGuid(),
            ToWarehouseId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Status = status,
            Lines = [new StockTransferLine { ProductId = Guid.NewGuid(), Quantity = 5 }]
        };

    [Fact]
    public void A_transfer_to_the_same_warehouse_is_refused()
    {
        var warehouse = Guid.NewGuid();
        var transfer = Transfer();
        transfer.FromWarehouseId = warehouse;
        transfer.ToWarehouseId = warehouse;

        var act = transfer.Validate;

        act.Should().Throw<DomainException>().WithMessage("*two different warehouses*");
    }

    [Fact]
    public void A_transfer_with_no_lines_moves_nothing()
    {
        var transfer = Transfer();
        transfer.Lines.Clear();

        var act = transfer.Validate;

        act.Should().Throw<DomainException>().WithMessage("*at least one line*");
    }

    [Fact]
    public void Stock_cannot_be_shipped_twice()
    {
        // Shipping an in-transit transfer again would post a second TransferOut and drain the source
        // warehouse for stock that already left it.
        var transfer = Transfer(TransferStatus.InTransit);

        var act = () => transfer.Ship(DateTimeOffset.UnixEpoch, null);

        act.Should().Throw<DomainException>().WithMessage("*InTransit cannot be shipped*");
    }

    [Fact]
    public void A_transfer_cannot_be_received_before_it_has_shipped()
    {
        // Otherwise a TransferIn would land at the destination with no matching TransferOut — stock
        // created out of nothing.
        var transfer = Transfer(TransferStatus.Draft);

        var act = () => transfer.Receive(DateTimeOffset.UnixEpoch, null);

        act.Should().Throw<DomainException>().WithMessage("*Draft cannot be received*");
    }

    [Fact]
    public void A_transfer_that_has_left_cannot_be_cancelled()
    {
        // The stock physically exists somewhere. Cancelling would leave a TransferOut with no TransferIn
        // and the units would simply vanish from the company's books.
        var transfer = Transfer(TransferStatus.InTransit);

        var act = transfer.Cancel;

        act.Should().Throw<DomainException>().WithMessage("*already left*");
    }

    [Fact]
    public void A_draft_transfer_can_be_cancelled()
    {
        var transfer = Transfer();

        transfer.Cancel();

        transfer.Status.Should().Be(TransferStatus.Cancelled);
    }

    [Fact]
    public void The_happy_path_is_draft_then_in_transit_then_received()
    {
        var transfer = Transfer();
        var shipped = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

        transfer.Ship(shipped, null);
        transfer.Status.Should().Be(TransferStatus.InTransit);
        transfer.ShippedAt.Should().Be(shipped);

        transfer.Receive(shipped.AddDays(2), null);
        transfer.Status.Should().Be(TransferStatus.Received);
    }

    [Fact]
    public void A_short_delivery_is_visible_rather_than_silently_lost()
    {
        // Five went on the van, four came off it. The missing unit is a fact somebody must answer for,
        // so it is reported rather than quietly written off.
        var transfer = Transfer();
        transfer.Lines = [new StockTransferLine { ProductId = Guid.NewGuid(), Quantity = 5, ReceivedQuantity = 4 }];

        transfer.HasShortfall.Should().BeTrue();
        transfer.Lines.Single().ShortfallQuantity.Should().Be(1);
    }

    [Fact]
    public void A_complete_delivery_has_no_shortfall()
    {
        var transfer = Transfer();
        transfer.Lines = [new StockTransferLine { ProductId = Guid.NewGuid(), Quantity = 5, ReceivedQuantity = 5 }];

        transfer.HasShortfall.Should().BeFalse();
    }
}

/// <summary>
/// Approving a count authorises a write-off. It is the one place in the module where stock can be
/// created or destroyed at will, which is why it has its own permission and its own guards.
/// </summary>
public class StockCountTests
{
    private static StockCount Count(StockCountStatus status = StockCountStatus.Counting, params StockCountLine[] lines) =>
        new()
        {
            Number = "CNT-2026-00001",
            WarehouseId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Status = status,
            Lines = lines.Length > 0
                ? [.. lines]
                : [new StockCountLine { ProductId = Guid.NewGuid(), SystemQuantity = 10, CountedQuantity = 10, UnitCost = 100m }]
        };

    [Fact]
    public void A_count_with_no_lines_counts_nothing()
    {
        var count = Count();
        count.Lines.Clear();

        var act = () => count.SubmitForApproval(DateTimeOffset.UnixEpoch);

        act.Should().Throw<DomainException>().WithMessage("*counts nothing*");
    }

    [Fact]
    public void A_count_cannot_be_approved_before_it_is_submitted()
    {
        var count = Count(StockCountStatus.Counting);

        var act = () => count.Approve(null, DateTimeOffset.UnixEpoch, null);

        act.Should().Throw<DomainException>().WithMessage("*Counting cannot be approved*");
    }

    [Fact]
    public void A_count_with_variances_cannot_be_approved_without_the_adjustment_that_posts_them()
    {
        // The worst possible outcome: the count says "approved", the ledger never moved, and the shop
        // believes it has reconciled when it has not.
        var count = Count(
            StockCountStatus.PendingApproval,
            new StockCountLine { ProductId = Guid.NewGuid(), SystemQuantity = 10, CountedQuantity = 8, UnitCost = 100m });

        var act = () => count.Approve(Guid.NewGuid(), DateTimeOffset.UnixEpoch, adjustmentId: null);

        act.Should().Throw<DomainException>().WithMessage("*without the adjustment*");
    }

    [Fact]
    public void A_count_with_no_variance_needs_no_adjustment()
    {
        // The shelf agreed with the ledger. There is nothing to post, and forcing an empty adjustment
        // document would put noise in the write-off report.
        var count = Count(StockCountStatus.PendingApproval);

        var act = () => count.Approve(Guid.NewGuid(), DateTimeOffset.UnixEpoch, adjustmentId: null);

        act.Should().NotThrow();
        count.Status.Should().Be(StockCountStatus.Approved);
    }

    [Fact]
    public void Approving_a_count_with_variances_records_the_adjustment_it_raised()
    {
        var adjustment = Guid.NewGuid();
        var count = Count(
            StockCountStatus.PendingApproval,
            new StockCountLine { ProductId = Guid.NewGuid(), SystemQuantity = 10, CountedQuantity = 8, UnitCost = 100m });

        count.Approve(Guid.NewGuid(), DateTimeOffset.UnixEpoch, adjustment);

        count.StockAdjustmentId.Should().Be(adjustment);
    }

    [Fact]
    public void An_approved_count_cannot_be_cancelled()
    {
        // It has already moved stock. Cancelling the document would not un-move it.
        var count = Count(StockCountStatus.Approved);

        var act = count.Cancel;

        act.Should().Throw<DomainException>().WithMessage("*already moved stock*");
    }

    [Fact]
    public void Variance_is_the_shelf_minus_the_system()
    {
        var missing = new StockCountLine { SystemQuantity = 10, CountedQuantity = 8, UnitCost = 100m };
        var surplus = new StockCountLine { SystemQuantity = 10, CountedQuantity = 12, UnitCost = 100m };

        missing.Variance.Should().Be(-2, "two units are missing");
        missing.VarianceValue.Should().Be(-200m);

        surplus.Variance.Should().Be(2, "two more than the system thought");
        surplus.VarianceValue.Should().Be(200m);
    }

    [Fact]
    public void Only_the_lines_that_disagree_post_anything()
    {
        var count = Count(
            StockCountStatus.PendingApproval,
            new StockCountLine { ProductId = Guid.NewGuid(), SystemQuantity = 10, CountedQuantity = 10, UnitCost = 100m },
            new StockCountLine { ProductId = Guid.NewGuid(), SystemQuantity = 5, CountedQuantity = 3, UnitCost = 50m });

        count.Variances.Should().HaveCount(1);
        count.NetVarianceValue.Should().Be(-100m, "two units at 50 went missing; the agreeing line posts nothing");
    }
}

/// <summary>
/// An adjustment posts immediately — there is no approval step to hide behind, so the mandatory reason
/// and the permission to create one are the entire control.
/// </summary>
public class StockAdjustmentTests
{
    private static StockAdjustment Adjustment(string explanation = "Water damage in the stockroom") =>
        new()
        {
            Number = "ADJ-2026-00001",
            WarehouseId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Reason = AdjustmentReason.Damaged,
            Explanation = explanation,
            Lines = [new StockAdjustmentLine { ProductId = Guid.NewGuid(), Quantity = -2, UnitCost = 100m }]
        };

    [Fact]
    public void An_adjustment_must_say_why()
    {
        // Stock does not vanish for no reason. A blank explanation is how a write-off report becomes
        // a list of numbers nobody can act on.
        var adjustment = Adjustment(explanation: "   ");

        var act = adjustment.Validate;

        act.Should().Throw<DomainException>().WithMessage("*stock does not vanish for no reason*");
    }

    [Fact]
    public void An_adjustment_line_of_zero_adjusts_nothing()
    {
        var adjustment = Adjustment();
        adjustment.Lines = [new StockAdjustmentLine { ProductId = Guid.NewGuid(), Quantity = 0, UnitCost = 100m }];

        var act = adjustment.Validate;

        act.Should().Throw<DomainException>().WithMessage("*adjusts nothing*");
    }

    [Fact]
    public void An_adjustment_with_no_lines_is_refused()
    {
        var adjustment = Adjustment();
        adjustment.Lines.Clear();

        var act = adjustment.Validate;

        act.Should().Throw<DomainException>().WithMessage("*at least one line*");
    }

    [Fact]
    public void One_document_can_write_stock_on_and_off_at_once()
    {
        // A count that found three of one product and lost two of another is one event. Splitting it
        // into two documents would lose that.
        var adjustment = Adjustment();
        adjustment.Lines =
        [
            new StockAdjustmentLine { ProductId = Guid.NewGuid(), Quantity = 3, UnitCost = 50m },
            new StockAdjustmentLine { ProductId = Guid.NewGuid(), Quantity = -2, UnitCost = 100m }
        ];

        adjustment.Validate();

        adjustment.NetValue.Should().Be(-50m, "150 written on, 200 written off");
        adjustment.Lines.First().IsWriteOn.Should().BeTrue();
        adjustment.Lines.Last().IsWriteOn.Should().BeFalse();
    }
}

/// <summary>
/// A reservation is a promise. The counter on the balance is the fast answer to "can I sell this?";
/// these rows are the honest one, and the two must agree.
/// </summary>
public class StockReservationTests
{
    private static StockReservation Reservation(decimal quantity = 5, ReservationStatus status = ReservationStatus.Active) =>
        new()
        {
            WarehouseId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Quantity = quantity,
            Status = status,
            ReferenceType = StockReferenceType.Invoice,
            ReservedAt = new DateTimeOffset(2026, 7, 13, 9, 0, 0, TimeSpan.Zero)
        };

    [Fact]
    public void A_partial_delivery_leaves_the_rest_of_the_promise_standing()
    {
        var reservation = Reservation(quantity: 5);

        reservation.Fulfil(2);

        reservation.Status.Should().Be(ReservationStatus.Active);
        reservation.OutstandingQuantity.Should().Be(3, "three units are still promised and still off the shelf");
    }

    [Fact]
    public void Fulfilling_the_last_unit_closes_the_reservation()
    {
        var reservation = Reservation(quantity: 5);

        reservation.Fulfil(5);

        reservation.Status.Should().Be(ReservationStatus.Fulfilled);
        reservation.OutstandingQuantity.Should().Be(0, "a closed reservation holds nothing off the shelf");
    }

    [Fact]
    public void A_reservation_cannot_be_over_fulfilled()
    {
        // Delivering six units against a promise of five would release stock that was never reserved,
        // driving the balance's reserved_quantity out of step with these rows.
        var reservation = Reservation(quantity: 5);

        var act = () => reservation.Fulfil(6);

        act.Should().Throw<DomainException>().WithMessage("*Cannot fulfil 6*");
    }

    [Fact]
    public void A_released_reservation_cannot_be_fulfilled()
    {
        var reservation = Reservation(status: ReservationStatus.Released);

        var act = () => reservation.Fulfil(1);

        act.Should().Throw<DomainException>().WithMessage("*Released and cannot be fulfilled*");
    }

    [Fact]
    public void A_reservation_cannot_be_released_twice()
    {
        // The second release would give the balance its stock back a second time, making available
        // units that nobody has.
        var reservation = Reservation();
        reservation.Release(DateTimeOffset.UnixEpoch);

        var act = () => reservation.Release(DateTimeOffset.UnixEpoch);

        act.Should().Throw<DomainException>().WithMessage("*already Released*");
    }

    [Fact]
    public void An_expired_release_is_recorded_as_expiry_not_as_a_decision()
    {
        // "Nobody released it and its time ran out" is a different fact from "the customer walked away",
        // and a reservations report that confused them would hide the abandoned quotes.
        var reservation = Reservation();

        reservation.Release(DateTimeOffset.UnixEpoch, expired: true);

        reservation.Status.Should().Be(ReservationStatus.Expired);
    }

    [Fact]
    public void Only_an_active_reservation_holds_stock_off_the_shelf()
    {
        Reservation(status: ReservationStatus.Released).OutstandingQuantity.Should().Be(0);
        Reservation(status: ReservationStatus.Expired).OutstandingQuantity.Should().Be(0);
        Reservation(status: ReservationStatus.Fulfilled).OutstandingQuantity.Should().Be(0);
        Reservation(status: ReservationStatus.Active).OutstandingQuantity.Should().Be(5);
    }

    [Fact]
    public void A_reservation_expires_on_its_deadline_and_not_before()
    {
        var deadline = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
        var reservation = Reservation();
        reservation.ExpiresAt = deadline;

        reservation.HasExpiredAt(deadline.AddSeconds(-1)).Should().BeFalse();
        reservation.HasExpiredAt(deadline).Should().BeTrue();
    }

    [Fact]
    public void A_reservation_with_no_deadline_never_expires_on_its_own()
    {
        // Which is exactly why the sweep cannot be the only safety net, and why setting a deadline is
        // strongly preferred: this one is held until a human gives it back.
        var reservation = Reservation();
        reservation.ExpiresAt = null;

        reservation.HasExpiredAt(DateTimeOffset.MaxValue).Should().BeFalse();
    }
}

/// <summary>A label run that prints nothing, or prints ten thousand labels by a typo, helps nobody.</summary>
public class BarcodePrintJobTests
{
    private static BarcodePrintJob Job(int labelCount) =>
        new()
        {
            SourceType = BarcodeSource.Product,
            SourceId = Guid.NewGuid(),
            LabelCount = labelCount,
            Symbology = BarcodeSymbology.Code128,
            Template = LabelTemplate.Thermal50x25
        };

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public void A_print_run_outside_the_sane_range_is_refused(int labelCount)
    {
        var act = Job(labelCount).Validate;

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5000)]
    public void The_bounds_themselves_are_allowed(int labelCount)
    {
        var act = Job(labelCount).Validate;

        act.Should().NotThrow();
    }
}
