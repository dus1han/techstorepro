using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;

namespace TechStorePro.Domain.Repairs;

/// <summary>
/// The workshop workflow (requirements §28), enforced as a state machine rather than a free-text column.
///
/// The order matters and the gate is <see cref="AwaitingApproval"/>: a shop that fits AED 400 of parts
/// into a machine before the customer agreed to the price is a shop that eats the parts when they say no.
/// </summary>
public enum RepairTicketStatus : short
{
    /// <summary>The device is on the counter and the fault is written down. Nothing has been touched yet.</summary>
    Received = 1,

    Diagnosing = 2,

    /// <summary>The estimate is with the customer. <b>Nothing may be consumed in this state.</b></summary>
    AwaitingApproval = 3,

    /// <summary>The customer said yes (or it is a warranty job, which they have nothing to say yes to).</summary>
    InRepair = 4,

    Testing = 5,

    /// <summary>Fixed, tested, and waiting on the shelf for the customer to come and get it.</summary>
    Ready = 6,

    /// <summary>Back in the customer's hands.</summary>
    Delivered = 7,

    /// <summary>
    /// Abandoned — most often because the customer declined the estimate. The device goes back
    /// unrepaired, which is why this is distinct from <see cref="Delivered"/>: one is a job that
    /// ended in a fix and one is a job that ended in a shrug, and a pending-repairs report that
    /// confused them would overstate what the workshop achieved.
    /// </summary>
    Cancelled = 8
}

/// <summary>Who is standing behind the repair, and therefore who pays for it (requirements §30).</summary>
public enum RepairWarrantyType : short
{
    /// <summary>Nobody. The customer pays, and this is the ordinary case.</summary>
    None = 0,

    /// <summary>The shop's own warranty, sold with the machine. <b>The shop pays.</b></summary>
    Shop = 1,

    /// <summary>The manufacturer's. The shop may recover the cost, but not through this module.</summary>
    Manufacturer = 2,

    /// <summary>The supplier's, on a machine the shop imported.</summary>
    Supplier = 3
}

/// <summary>
/// A job sheet (requirements §28) — one device, one fault, one workshop job.
///
/// <b>The device is not stock, and this is the thing to hold on to about repairs.</b> A customer's laptop
/// sitting on the workshop bench is not inventory: the shop does not own it, cannot sell it, and must not
/// value it. So intake writes no stock movement. What <em>does</em> move is the parts fitted into it, and
/// those leave the shelf through <c>IStockLedger</c> like everything else (architecture.md §4.5).
///
/// The one exception is a serial the shop itself sold: it is tracked, and it comes back as
/// <see cref="SerialStatus.InRepair"/> — but it is still the customer's machine, and it is still not stock.
/// </summary>
public class RepairTicket : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>
    /// The product, when the device is one the shop knows. Null for a machine it has never sold and does
    /// not stock — a customer may bring in anything, and refusing the job because the model is not in the
    /// catalogue would turn the catalogue into a gatekeeper it was never meant to be.
    /// </summary>
    public Guid? DeviceProductId { get; set; }
    public Product? DeviceProduct { get; set; }

    /// <summary>Free text, always — it is what is engraved on the machine, not a foreign key.</summary>
    public string? DeviceSerialNumber { get; set; }

    /// <summary>
    /// Set only when the serial on the counter is one the shop sold and can find. This is the back-edge
    /// into Sales (architecture.md §2), and it is what makes <see cref="WarrantyInvoiceLineId"/> answerable.
    /// </summary>
    public Guid? DeviceSerialId { get; set; }
    public Serial? DeviceSerial { get; set; }

    /// <summary>What the customer says is wrong. Their words, not the technician's.</summary>
    public string ReportedFault { get; set; } = null!;

    /// <summary>The charger, the bag, the box. Written down at intake so the argument at collection cannot happen.</summary>
    public string? Accessories { get; set; }

    /// <summary>Scratches, cracks, a missing key — recorded before the shop touches it, for the same reason.</summary>
    public string? ConditionNotes { get; set; }

    public RepairTicketStatus Status { get; set; } = RepairTicketStatus.Received;

    /// <summary>
    /// What the shop told the customer it would cost — the number they approved. It is not the number they
    /// are billed: the bill is the sum of what was actually chargeable (see <see cref="ChargeableTotal"/>).
    /// Keeping both is what lets a manager ask why a job came in 300 over its estimate.
    /// </summary>
    public decimal? EstimatedCost { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Null on a warranty job: there was no price, so there was nobody to agree to it.</summary>
    public Guid? ApprovedBy { get; set; }

    public RepairWarrantyType WarrantyType { get; set; } = RepairWarrantyType.None;

    /// <summary>
    /// The invoice line that sold this machine — found at intake by walking
    /// <c>Serial.SoldInvoiceLineId</c>. <b>This is the whole point of P5 binding the serial at delivery</b>:
    /// two years later, a laptop on the counter can be traced to the sale that put it there, and the shop
    /// can say whether it owes a free repair.
    /// </summary>
    public Guid? WarrantyInvoiceLineId { get; set; }
    public SalesInvoiceLine? WarrantyInvoiceLine { get; set; }

    public Guid? TechnicianId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>What the customer was told. A pending-repairs report sorts on it.</summary>
    public DateTimeOffset? PromisedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>Why the job was abandoned. Mandatory on <see cref="Cancel"/>.</summary>
    public string? CancelledReason { get; set; }

    public ICollection<RepairDiagnosis> Diagnoses { get; set; } = [];
    public ICollection<RepairPart> Parts { get; set; } = [];
    public ICollection<RepairLabour> Labour { get; set; } = [];
    public ICollection<RepairOutsourcing> Outsourcings { get; set; } = [];
    public ICollection<RepairStatusChange> StatusHistory { get; set; } = [];

    /// <summary>The bills raised against this job. Usually one; never required.</summary>
    public ICollection<RepairCharge> Charges { get; set; } = [];

    /// <summary>A job the shop is paying for itself, whoever's warranty it is under.</summary>
    public bool IsWarranty => WarrantyType != RepairWarrantyType.None;

    public bool IsClosed => Status is RepairTicketStatus.Delivered or RepairTicketStatus.Cancelled;

    // --- The money (requirements §35, "repair profitability") ---

    /// <summary>
    /// What the parts cost the shop — the moving average at the moment they left the shelf, snapshotted
    /// for the same reason a delivery snapshots COGS: the average keeps moving, and recomputing it later
    /// would restate the profit on every job the workshop has ever done.
    /// </summary>
    public decimal PartsCost => Parts.Sum(p => p.CostTotal);

    /// <summary>What the shop paid a third party to do the work (§29). A real cost of this job.</summary>
    public decimal OutsourcingCost => Outsourcings.Sum(o => o.CostInBaseCurrency);

    /// <summary>
    /// <b>Labour has no cost side, deliberately.</b> The technician's wage is a payroll expense (§34, P7),
    /// not a cost of any one job — the shop pays it whether the bench is busy or idle. Apportioning a
    /// salary across job sheets would invent a number the business never agreed to, and it would make a
    /// quiet week look like a loss on every ticket.
    /// </summary>
    public decimal TotalCost => PartsCost + OutsourcingCost;

    /// <summary>What the customer is billed — chargeable lines only. Zero on a warranty job.</summary>
    public decimal ChargeableTotal =>
        Parts.Where(p => p.IsChargeable).Sum(p => p.ChargeTotal)
        + Labour.Where(l => l.IsChargeable).Sum(l => l.ChargeTotal);

    /// <summary>
    /// Revenue minus what the job actually consumed.
    ///
    /// <b>On a warranty job this is negative, and it is supposed to be.</b> The parts still left the shelf
    /// and the vendor still charged for the board; only the customer's bill is zero. A warranty repair that
    /// booked no cost would make warranty look free, and the shop would never learn which product line is
    /// eating it (§45 D10).
    /// </summary>
    public decimal GrossProfit => ChargeableTotal - TotalCost;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ReportedFault))
        {
            throw new DomainException("A repair ticket needs the fault the customer reported.");
        }
    }

    /// <summary>
    /// Records a transition and the trail of it. Every status change in this module goes through here —
    /// a state machine whose history is written by whoever remembers to write it is a state machine with
    /// no history.
    ///
    /// <b>It returns the row rather than only appending it, and the caller must add it to the DbSet.</b>
    /// That is not ceremony: <c>BaseEntity</c> assigns the key in its initialiser, so a row discovered by
    /// EF through this collection has a key already set — and EF reads a set key as "this row exists",
    /// tracks it as <c>Modified</c>, and issues an UPDATE against a row that was never inserted. The whole
    /// history would vanish, loudly on a good day and silently on a bad one. Hence the codebase's rule,
    /// which this obeys: <em>through the DbSet, and only the DbSet</em>.
    /// </summary>
    private RepairStatusChange MoveTo(RepairTicketStatus to, Guid? by, DateTimeOffset at, string? notes = null)
    {
        var change = new RepairStatusChange
        {
            RepairTicketId = Id,
            FromStatus = Status,
            ToStatus = to,
            ChangedBy = by,
            ChangedAt = at,
            Notes = notes
        };

        StatusHistory.Add(change);
        Status = to;

        return change;
    }

    public RepairStatusChange BeginDiagnosis(Guid? technicianId, DateTimeOffset at)
    {
        if (Status != RepairTicketStatus.Received)
        {
            throw new DomainException($"A job that is {Status} is past diagnosis.");
        }

        TechnicianId ??= technicianId;

        return MoveTo(RepairTicketStatus.Diagnosing, technicianId, at);
    }

    /// <summary>
    /// The technician has found the fault and priced the fix.
    ///
    /// <b>A warranty job goes straight to the bench.</b> The approval step exists to get the customer to
    /// agree to a price; on a job they are not being charged for there is no price, so there is nothing to
    /// agree to. Parking a free repair in <see cref="RepairTicketStatus.AwaitingApproval"/> would leave it
    /// waiting for a decision nobody was ever going to be asked to make.
    /// </summary>
    public RepairStatusChange RecordDiagnosis(decimal? estimatedCost, DateTimeOffset at, Guid? technicianId)
    {
        if (Status != RepairTicketStatus.Diagnosing)
        {
            throw new DomainException($"A job that is {Status} is not being diagnosed.");
        }

        EstimatedCost = estimatedCost;

        return IsWarranty
            ? MoveTo(RepairTicketStatus.InRepair, technicianId, at, "Under warranty — no estimate to approve.")
            : MoveTo(RepairTicketStatus.AwaitingApproval, technicianId, at);
    }

    /// <summary>The customer has agreed to the estimate. This is what unlocks the parts store.</summary>
    public RepairStatusChange ApproveByCustomer(Guid? by, DateTimeOffset at)
    {
        if (Status != RepairTicketStatus.AwaitingApproval)
        {
            throw new DomainException($"A job that is {Status} is not waiting for the customer.");
        }

        ApprovedAt = at;
        ApprovedBy = by;

        return MoveTo(RepairTicketStatus.InRepair, by, at);
    }

    /// <summary>
    /// The customer looked at the estimate and said no. The device goes back untouched, so the job is
    /// <see cref="RepairTicketStatus.Cancelled"/> and not <see cref="RepairTicketStatus.Delivered"/>.
    /// </summary>
    public RepairStatusChange DeclineByCustomer(string reason, Guid? by, DateTimeOffset at)
    {
        if (Status != RepairTicketStatus.AwaitingApproval)
        {
            throw new DomainException($"A job that is {Status} is not waiting for the customer.");
        }

        return Cancel(reason, by, at);
    }

    /// <summary>
    /// Whether a part or an hour may be booked to this job right now.
    ///
    /// Testing is included on purpose: a machine that fails its test bench needs another part, and forcing
    /// the technician to reopen the job to fit it would be a workflow people route around by never moving
    /// the job to Testing in the first place.
    /// </summary>
    public void EnsureWorkAllowed()
    {
        if (Status is RepairTicketStatus.InRepair or RepairTicketStatus.Testing)
        {
            return;
        }

        if (Status is RepairTicketStatus.AwaitingApproval)
        {
            throw new DomainException(
                "The customer has not approved the estimate, so nothing may be fitted to this machine yet. "
                + "Parts consumed against a job the customer then declines are parts the shop has paid for "
                + "and cannot bill.");
        }

        throw new DomainException($"A job that is {Status} cannot have work booked to it.");
    }

    public RepairStatusChange BeginTesting(Guid? by, DateTimeOffset at)
    {
        if (Status != RepairTicketStatus.InRepair)
        {
            throw new DomainException($"A job that is {Status} is not on the bench.");
        }

        return MoveTo(RepairTicketStatus.Testing, by, at);
    }

    public RepairStatusChange MarkReady(Guid? by, DateTimeOffset at)
    {
        if (Status is not (RepairTicketStatus.Testing or RepairTicketStatus.InRepair))
        {
            throw new DomainException($"A job that is {Status} is not ready for collection.");
        }

        return MoveTo(RepairTicketStatus.Ready, by, at);
    }

    /// <summary>
    /// The customer collects the machine.
    ///
    /// It does not require the bill to be paid, and that is deliberate: a shop that would not hand back a
    /// customer's own laptop until an invoice cleared would be holding it hostage. The debt is on the
    /// customer's balance, which is where a debt belongs.
    /// </summary>
    public RepairStatusChange Deliver(Guid? by, DateTimeOffset at, string? notes = null)
    {
        if (Status != RepairTicketStatus.Ready)
        {
            throw new DomainException(
                $"A job that is {Status} has not been tested and put on the collection shelf. "
                + "Mark it ready first.");
        }

        DeliveredAt = at;

        return MoveTo(RepairTicketStatus.Delivered, by, at, notes);
    }

    public RepairStatusChange Cancel(string reason, Guid? by, DateTimeOffset at)
    {
        if (IsClosed)
        {
            throw new DomainException($"A job that is {Status} is already closed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cancelling a repair needs a reason. A job that ended for no recorded reason is a job nobody can answer for.");
        }

        if (Parts.Any(p => !p.IsReturned))
        {
            throw new DomainException(
                "Parts have already been fitted to this machine, so the job cannot simply be cancelled. "
                + "Return them to stock first, or finish the job and bill it.");
        }

        CancelledReason = reason;

        return MoveTo(RepairTicketStatus.Cancelled, by, at, reason);
    }
}

/// <summary>
/// Every transition, with who and when (architecture.md §3.9). Written by the entity itself rather than
/// by the handlers, so no code path can move a job and forget to say so.
///
/// Not soft-deletable and never updated: it is evidence, and evidence you can edit is not evidence — the
/// same reasoning that keeps <c>stock_movements</c> append-only.
/// </summary>
public class RepairStatusChange : TenantEntity
{
    public Guid RepairTicketId { get; set; }
    public RepairTicket RepairTicket { get; set; } = null!;

    public RepairTicketStatus FromStatus { get; set; }
    public RepairTicketStatus ToStatus { get; set; }

    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }

    public string? Notes { get; set; }
}
