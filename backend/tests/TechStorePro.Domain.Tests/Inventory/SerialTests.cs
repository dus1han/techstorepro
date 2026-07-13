using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Inventory;
using FluentAssertions;
using Xunit;

namespace TechStorePro.Domain.Tests.Inventory;

/// <summary>
/// The serial state machine is what stops the same laptop being sold twice. Quantities alone cannot:
/// two sales of one unit would each decrement a balance that looked fine, and the shelf would disagree
/// with the ledger only once somebody went looking.
/// </summary>
public class SerialTests
{
    private static Serial Serial(SerialStatus status, Guid? warehouseId = null) =>
        new()
        {
            SerialNumber = "SN-0001",
            ProductId = Guid.NewGuid(),
            Status = status,
            WarehouseId = warehouseId
        };

    [Fact]
    public void A_unit_in_stock_can_be_sold()
    {
        var serial = Serial(SerialStatus.InStock, Guid.NewGuid());

        serial.TransitionTo(SerialStatus.Sold, null);

        serial.Status.Should().Be(SerialStatus.Sold);
        serial.WarehouseId.Should().BeNull("a sold unit is in a customer's hands, not in a warehouse");
    }

    [Fact]
    public void The_same_unit_cannot_be_sold_twice()
    {
        // The single most important assertion in the module.
        var serial = Serial(SerialStatus.Sold);

        var act = () => serial.TransitionTo(SerialStatus.Sold, null);

        act.Should().Throw<DomainException>().WithMessage("*is Sold and cannot become Sold*");
    }

    [Fact]
    public void A_reserved_unit_cannot_be_sold_out_from_under_the_reservation()
    {
        // Reserved → Sold *is* allowed: that is the reservation being honoured. What is not allowed is
        // shipping it somewhere else — a transfer would move stock somebody has already been promised.
        var serial = Serial(SerialStatus.Reserved, Guid.NewGuid());

        var act = () => serial.TransitionTo(SerialStatus.InTransit, null);

        act.Should().Throw<DomainException>().WithMessage("*is Reserved and cannot become InTransit*");
    }

    [Fact]
    public void A_sold_unit_never_silently_returns_to_the_shelf()
    {
        // It comes back as Returned, and a human decides whether it is resaleable. Straight back to
        // InStock would put a customer's used machine on sale as new.
        var serial = Serial(SerialStatus.Sold);

        var act = () => serial.TransitionTo(SerialStatus.InStock, Guid.NewGuid());

        act.Should().Throw<DomainException>();

        serial.TransitionTo(SerialStatus.Returned, Guid.NewGuid());
        serial.Status.Should().Be(SerialStatus.Returned);

        // And from Returned, somebody may put it back.
        serial.TransitionTo(SerialStatus.InStock, Guid.NewGuid());
        serial.Status.Should().Be(SerialStatus.InStock);
    }

    [Fact]
    public void A_unit_in_transit_belongs_to_no_warehouse()
    {
        var serial = Serial(SerialStatus.InStock, Guid.NewGuid());

        serial.TransitionTo(SerialStatus.InTransit, null);

        serial.WarehouseId.Should().BeNull("owned by neither warehouse, so it cannot be sold from both");
    }

    [Theory]
    [InlineData(SerialStatus.Scrapped)]
    [InlineData(SerialStatus.ReturnedToSupplier)]
    public void A_written_off_unit_can_never_come_back(SerialStatus terminal)
    {
        // A shop that could un-scrap a unit could conjure stock from a write-off. That is precisely the
        // fraud a serial ledger exists to make impossible, so both terminal states are dead ends.
        var serial = Serial(terminal);

        foreach (var next in Enum.GetValues<SerialStatus>())
        {
            var act = () => serial.TransitionTo(next, Guid.NewGuid());

            act.Should().Throw<DomainException>($"{terminal} is terminal and must not become {next}");
        }
    }

    [Fact]
    public void A_warranty_claim_takes_a_sold_unit_into_repair()
    {
        // This is the P6 path, and it is why Sold → InRepair must be legal.
        var serial = Serial(SerialStatus.Sold);

        var act = () => serial.TransitionTo(SerialStatus.InRepair, Guid.NewGuid());

        act.Should().NotThrow();
    }

    [Fact]
    public void Warranty_is_open_until_the_date_and_not_after_it()
    {
        var at = new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
        var serial = Serial(SerialStatus.Sold);
        serial.WarrantyUntil = at;

        serial.IsUnderWarrantyAt(at.AddDays(-1)).Should().BeTrue();
        serial.IsUnderWarrantyAt(at).Should().BeFalse("the warranty expires on the date, not after it");
        serial.IsUnderWarrantyAt(at.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void A_unit_with_no_warranty_is_never_under_warranty()
    {
        var serial = Serial(SerialStatus.Sold);
        serial.WarrantyUntil = null;

        serial.IsUnderWarrantyAt(DateTimeOffset.UnixEpoch).Should().BeFalse();
    }
}
