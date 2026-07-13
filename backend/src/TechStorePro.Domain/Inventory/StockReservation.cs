using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Inventory;

public enum ReservationStatus : short
{
    Active = 1,

    /// <summary>Given back deliberately — the quote was lost, the order cancelled.</summary>
    Released = 2,

    /// <summary>The stock actually left: a delivery consumed the reservation that promised it.</summary>
    Fulfilled = 3,

    /// <summary>Nobody released it and its time ran out.</summary>
    Expired = 4
}

/// <summary>
/// A promise that a quantity of stock is not sellable to anyone else (requirements §20).
///
/// Reservations are a <em>document</em>, not just a number on the balance, because "why is this
/// reserved and who by?" is the first question asked when a customer is told their item is
/// unavailable while it is visibly on the shelf. The counter on
/// <see cref="StockBalance.ReservedQuantity"/> is the fast answer; these rows are the honest one, and
/// the two must sum to each other.
///
/// <b>Expiry matters.</b> A quote that reserved the last unit and was then forgotten would keep that
/// unit off the shelf forever. Reservations therefore carry a deadline and are swept.
/// </summary>
public class StockReservation : TenantEntity
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Set for a serial-tracked product: the specific machine that was promised.</summary>
    public Guid? SerialId { get; set; }
    public Serial? Serial { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>How much of <see cref="Quantity"/> has actually shipped. Partial deliveries are normal.</summary>
    public decimal FulfilledQuantity { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Active;

    /// <summary>The quote or sales order that made the promise (P5).</summary>
    public StockReferenceType ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }

    public DateTimeOffset ReservedAt { get; set; }

    /// <summary>Null = held until someone releases it. Setting it is strongly preferred; see the class remarks.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>Still held against the balance. This is the number that comes off available stock.</summary>
    public decimal OutstandingQuantity =>
        Status == ReservationStatus.Active ? Quantity - FulfilledQuantity : 0;

    public bool HasExpiredAt(DateTimeOffset at) =>
        Status == ReservationStatus.Active && ExpiresAt is { } expiry && expiry <= at;

    /// <summary>Consumes part (or all) of the promise as stock actually leaves.</summary>
    public void Fulfil(decimal quantity)
    {
        if (Status != ReservationStatus.Active)
        {
            throw new DomainException($"Reservation is {Status} and cannot be fulfilled.");
        }

        if (quantity <= 0 || quantity > OutstandingQuantity)
        {
            throw new DomainException(
                $"Cannot fulfil {quantity} units against a reservation with {OutstandingQuantity} outstanding.");
        }

        FulfilledQuantity += quantity;

        if (FulfilledQuantity >= Quantity)
        {
            Status = ReservationStatus.Fulfilled;
        }
    }

    public void Release(DateTimeOffset at, bool expired = false)
    {
        if (Status != ReservationStatus.Active)
        {
            throw new DomainException($"Reservation is already {Status}.");
        }

        Status = expired ? ReservationStatus.Expired : ReservationStatus.Released;
        ReleasedAt = at;
    }
}
