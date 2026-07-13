using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Inventory;

public enum TransferStatus : short
{
    Draft = 1,

    /// <summary>Left the source warehouse; has not arrived. The stock belongs to neither end.</summary>
    InTransit = 2,

    Received = 3,
    Cancelled = 4
}

/// <summary>
/// Stock moving between two warehouses (requirements §19).
///
/// <b>A transfer is two movements, not one</b>, and the gap between them is the point. Stock leaves
/// the source when the van is loaded and arrives at the destination when someone signs for it — which
/// may be days later, and may be for fewer units than were sent. Posting a single instantaneous
/// movement would make in-transit stock either sellable at both ends or sellable at neither, and would
/// leave a short delivery with nowhere to be recorded.
///
/// So: <see cref="Ship"/> posts <see cref="MovementType.TransferOut"/>, <see cref="Receive"/> posts
/// <see cref="MovementType.TransferIn"/> for what actually turned up, and the difference is a visible
/// shortfall rather than a silent loss.
/// </summary>
public class StockTransfer : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid FromWarehouseId { get; set; }
    public Warehouse FromWarehouse { get; set; } = null!;

    public Guid ToWarehouseId { get; set; }
    public Warehouse ToWarehouse { get; set; } = null!;

    /// <summary>The branch that raised the transfer — whose document number it took.</summary>
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public TransferStatus Status { get; set; } = TransferStatus.Draft;

    public DateTimeOffset? ShippedAt { get; set; }
    public Guid? ShippedBy { get; set; }

    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid? ReceivedBy { get; set; }

    public string? Notes { get; set; }

    public ICollection<StockTransferLine> Lines { get; set; } = [];

    /// <summary>Did anything go missing between the two warehouses?</summary>
    public bool HasShortfall => Lines.Any(l => l.ReceivedQuantity < l.Quantity);

    public void Validate()
    {
        if (FromWarehouseId == ToWarehouseId)
        {
            throw new DomainException("A transfer must move stock between two different warehouses.");
        }

        if (Lines.Count == 0)
        {
            throw new DomainException("A transfer must have at least one line.");
        }
    }

    public void Ship(DateTimeOffset at, Guid? by)
    {
        if (Status != TransferStatus.Draft)
        {
            throw new DomainException($"A transfer that is {Status} cannot be shipped.");
        }

        Status = TransferStatus.InTransit;
        ShippedAt = at;
        ShippedBy = by;
    }

    public void Receive(DateTimeOffset at, Guid? by)
    {
        if (Status != TransferStatus.InTransit)
        {
            throw new DomainException($"A transfer that is {Status} cannot be received.");
        }

        Status = TransferStatus.Received;
        ReceivedAt = at;
        ReceivedBy = by;
    }

    public void Cancel()
    {
        // Once the stock has left the source warehouse there is no cancelling it — it physically
        // exists somewhere. It has to be received (possibly short) or transferred back.
        if (Status != TransferStatus.Draft)
        {
            throw new DomainException(
                $"A transfer that is {Status} cannot be cancelled: the stock has already left. "
                + "Receive it and transfer it back.");
        }

        Status = TransferStatus.Cancelled;
    }
}

public class StockTransferLine : TenantEntity
{
    public Guid StockTransferId { get; set; }
    public StockTransfer StockTransfer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Set for a serial-tracked product: one line per machine.</summary>
    public Guid? SerialId { get; set; }
    public Serial? Serial { get; set; }

    /// <summary>What was sent.</summary>
    public decimal Quantity { get; set; }

    /// <summary>What arrived. Below <see cref="Quantity"/> is a shortfall, and it is meant to be visible.</summary>
    public decimal ReceivedQuantity { get; set; }

    /// <summary>The source warehouse's average cost at the moment of shipping — the cost that lands
    /// at the destination. A transfer must not create or destroy value.</summary>
    public decimal UnitCost { get; set; }

    public decimal ShortfallQuantity => Quantity - ReceivedQuantity;
}
