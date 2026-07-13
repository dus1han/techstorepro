using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Purchasing;

public enum ImportShipmentStatus : short
{
    /// <summary>On the water, or still being assembled as a document.</summary>
    InTransit = 1,

    /// <summary>Landed. Goods receipts have posted against it; charges may still be arriving.</summary>
    Arrived = 2,

    /// <summary>Charges apportioned and folded into inventory. The container's true cost is known.</summary>
    Costed = 3,

    Cancelled = 4
}

/// <summary>What kind of money a charge is (requirements §26: freight, insurance, customs, clearing).</summary>
public enum ImportChargeType : short
{
    Freight = 1,
    Insurance = 2,
    Customs = 3,
    Clearing = 4,
    Handling = 5,
    Other = 6
}

/// <summary>
/// A container of goods coming in from overseas (requirements §26), and the charges that make its
/// contents cost more than the supplier's invoice says.
///
/// <b>The hard truth this models: goods and their true cost do not arrive together.</b> The container
/// is unpacked and on the shelf in March; the clearing agent invoices in April. The shop cannot refuse
/// to book stock it can see, so the receipt posts at the goods price — and the freight, duty and
/// clearing are folded in afterwards, as a revaluation that raises the weighted average without
/// inventing a unit.
///
/// That is what <see cref="Costed"/> means, and why it is a separate step rather than something the
/// receipt does. Anything else would either delay stock that physically exists or invent a cost that
/// nobody has yet been billed.
/// </summary>
public class ImportShipment : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public ImportShipmentStatus Status { get; set; } = ImportShipmentStatus.InTransit;

    /// <summary>Bill of lading / airway bill. What the shipping line and the customs broker both quote.</summary>
    public string? TransportDocument { get; set; }

    public string? VesselOrFlight { get; set; }
    public string? PortOfLoading { get; set; }
    public string? PortOfDischarge { get; set; }

    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? ExpectedAt { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }

    public DateTimeOffset? CostedAt { get; set; }
    public Guid? CostedBy { get; set; }

    /// <summary>
    /// Charges the shop could not fold into stock, in base currency.
    ///
    /// It is not zero when the container sold out before its clearing invoice arrived: there is no
    /// stock left for the cost to attach to. That money is a real expense and it has to go somewhere —
    /// but there is no general ledger (§45 D3), so it is <b>recorded here, visibly</b>, and P7 will pick
    /// it up as a cost of sales. Silently dropping it would overstate margin; silently smearing it over
    /// the next container's goods would charge one shipment's freight to another's.
    /// </summary>
    public decimal UnabsorbedCost { get; set; }

    public string? Notes { get; set; }

    public ICollection<ImportShipmentCharge> Charges { get; set; } = [];

    /// <summary>The receipts that brought this container's goods into stock.</summary>
    public ICollection<GoodsReceipt> Receipts { get; set; } = [];

    /// <summary>Every charge, in base currency. This is the number that gets apportioned.</summary>
    public decimal TotalChargesBase => Charges.Sum(c => c.AmountBase);

    public void Arrive(DateTimeOffset at)
    {
        if (Status is not (ImportShipmentStatus.InTransit or ImportShipmentStatus.Arrived))
        {
            throw new DomainException($"A shipment that is {Status} cannot be marked as arrived.");
        }

        Status = ImportShipmentStatus.Arrived;
        ArrivedAt = at;
    }

    /// <summary>
    /// Marks the container costed. The caller must have posted the revaluations in the same
    /// transaction — this refuses to say "costed" without them, because a shipment that claims its
    /// landed cost reached inventory when it did not is the worst outcome available: the stock reports
    /// look settled and every margin computed from them is wrong.
    /// </summary>
    public void MarkCosted(Guid? by, DateTimeOffset at, decimal unabsorbed)
    {
        if (Status == ImportShipmentStatus.Costed)
        {
            throw new DomainException(
                "This shipment has already been costed. Folding its charges into stock twice would "
                + "double the freight on every unit. Raise a further charge instead.");
        }

        if (Status != ImportShipmentStatus.Arrived)
        {
            throw new DomainException(
                $"A shipment that is {Status} cannot be costed — the goods have not been received yet, "
                + "so there is nothing for the charges to attach to.");
        }

        if (Charges.Count == 0)
        {
            throw new DomainException("This shipment has no charges to apportion.");
        }

        Status = ImportShipmentStatus.Costed;
        CostedAt = at;
        CostedBy = by;
        UnabsorbedCost = unabsorbed;
    }

    public void Cancel()
    {
        if (Status == ImportShipmentStatus.Costed)
        {
            throw new DomainException(
                "This shipment's costs are already in inventory and cannot be cancelled away. "
                + "Reverse them with a credit charge.");
        }

        Status = ImportShipmentStatus.Cancelled;
    }
}

/// <summary>
/// One invoice from somebody other than the supplier of the goods: the shipping line, the insurer, the
/// customs authority, the clearing agent.
/// </summary>
public class ImportShipmentCharge : TenantEntity
{
    public Guid ImportShipmentId { get; set; }
    public ImportShipment ImportShipment { get; set; } = null!;

    public ImportChargeType Type { get; set; }

    public string? Description { get; set; }

    /// <summary>Who billed it — the shipping line, not the supplier of the goods.</summary>
    public string? Vendor { get; set; }

    public string? Reference { get; set; }

    /// <summary>As invoiced, in whatever currency the charge came in.</summary>
    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "AED";

    /// <summary>The rate on the day this charge was incurred.</summary>
    public decimal ExchangeRate { get; set; } = 1m;

    public DateTimeOffset IncurredAt { get; set; }

    /// <summary>
    /// The charge in the company's own money. Everything is apportioned in base currency: a container
    /// whose freight is billed in USD and whose duty is billed in AED cannot be added up in any other.
    /// </summary>
    public decimal AmountBase => Amount * ExchangeRate;

    public void Validate()
    {
        if (Amount < 0)
        {
            throw new DomainException(
                "A charge cannot be negative. To reverse a charge that was over-billed, record a "
                + "credit — the sign belongs to the charge type, not to the amount.");
        }

        if (ExchangeRate <= 0)
        {
            throw new DomainException("An exchange rate must be greater than zero.");
        }
    }
}
