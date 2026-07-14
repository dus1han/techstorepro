using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Inventory;

namespace TechStorePro.Domain.Repairs;

/// <summary>What the warranty is attached to, and therefore how it was found.</summary>
public enum WarrantySourceType : short
{
    /// <summary>The shop sold it. The source is the sales invoice line, reached from the serial.</summary>
    SalesInvoiceLine = 1,

    /// <summary>The shop bought it, and the supplier stands behind it. The source is the goods receipt.</summary>
    GoodsReceiptLine = 2,

    /// <summary>Registered by hand against a serial — a manufacturer's warranty the shop was told about.</summary>
    Serial = 3
}

/// <summary>
/// A promise that somebody will pay to fix this machine if it breaks (requirements §30).
///
/// <b>The shop's own warranty is not stored here, and that is not an oversight.</b> P5 already computes it
/// at the moment of sale and stamps it on the unit as <c>Serial.WarrantyUntil</c>, from the product's
/// <c>WarrantyMonths</c>. Copying it into a second table would create two answers to "is this machine still
/// under warranty?", and the day they disagreed the shop would believe whichever one it happened to read.
///
/// What this table holds is the warranties the system cannot derive: the <b>manufacturer's</b> and the
/// <b>supplier's</b>. Nobody can compute those — they are terms somebody was given on paper — so they are
/// recorded, and the repair intake checks both sources (see <c>WarrantyLookup</c>).
/// </summary>
public class Warranty : TenantEntity
{
    public RepairWarrantyType WarrantyType { get; set; }

    public WarrantySourceType SourceType { get; set; }

    /// <summary>The invoice line, receipt line or serial this was registered against.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>The unit it covers. Null for a warranty on a batch or an unserialised product.</summary>
    public Guid? SerialId { get; set; }
    public Serial? Serial { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>The serial as written on the machine, kept even when <see cref="SerialId"/> is null.</summary>
    public string? SerialNumber { get; set; }

    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }

    public string? Terms { get; set; }

    /// <summary>The claims made against it. A warranty is not used up by being claimed on.</summary>
    public ICollection<WarrantyClaim> Claims { get; set; } = [];

    public bool CoversAt(DateTimeOffset at)
    {
        var day = DateOnly.FromDateTime(at.UtcDateTime);
        return day >= StartsOn && day <= EndsOn;
    }

    public void Validate()
    {
        if (EndsOn < StartsOn)
        {
            throw new DomainException("A warranty cannot end before it starts.");
        }

        if (WarrantyType == RepairWarrantyType.None)
        {
            throw new DomainException("A warranty that covers nobody is not a warranty.");
        }
    }
}

public enum WarrantyClaimStatus : short
{
    /// <summary>Raised, and the repair it authorises is under way.</summary>
    Open = 1,

    /// <summary>Honoured — the machine was fixed and the customer paid nothing.</summary>
    Accepted = 2,

    /// <summary>
    /// Refused: the fault turned out not to be covered (liquid damage, an out-of-warranty part). The job
    /// carries on as a chargeable repair, so this is not the end of the ticket — only of the free ride.
    /// </summary>
    Rejected = 3
}

/// <summary>
/// A claim made on a warranty (requirements §30), and the audit trail of a free repair.
///
/// It exists so the shop can answer the question its margin depends on: <em>which products keep coming
/// back?</em> A warranty repair with no claim record is a cost with no cause, and a warranty claim without
/// its repair ticket is a cost with no evidence — so the two point at each other.
/// </summary>
public class WarrantyClaim : TenantEntity
{
    public Guid WarrantyId { get; set; }
    public Warranty Warranty { get; set; } = null!;

    /// <summary>
    /// The job raised to honour it. Null only for a claim recorded before the machine came in — the
    /// customer phoned ahead.
    /// </summary>
    public Guid? RepairTicketId { get; set; }
    public RepairTicket? RepairTicket { get; set; }

    public WarrantyClaimStatus Status { get; set; } = WarrantyClaimStatus.Open;

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>What happened. Mandatory when the claim is rejected — a refusal with no reason is a dispute.</summary>
    public string? Outcome { get; set; }

    public string? Notes { get; set; }

    public void Accept(DateTimeOffset at, string? outcome)
    {
        if (Status != WarrantyClaimStatus.Open)
        {
            throw new DomainException($"A claim that is {Status} has already been settled.");
        }

        Status = WarrantyClaimStatus.Accepted;
        ResolvedAt = at;
        Outcome = outcome;
    }

    public void Reject(DateTimeOffset at, string outcome)
    {
        if (Status != WarrantyClaimStatus.Open)
        {
            throw new DomainException($"A claim that is {Status} has already been settled.");
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            throw new DomainException(
                "Rejecting a warranty claim needs a reason. The customer is about to be charged for a "
                + "repair they believed was free, and 'because we said so' is how that becomes a dispute.");
        }

        Status = WarrantyClaimStatus.Rejected;
        ResolvedAt = at;
        Outcome = outcome;
    }
}
