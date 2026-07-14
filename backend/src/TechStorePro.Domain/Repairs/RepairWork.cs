using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Sales;

namespace TechStorePro.Domain.Repairs;

/// <summary>
/// What the technician found and what they propose to do about it (requirements §28).
///
/// It is a row per diagnosis rather than a column on the ticket because a machine that comes back off the
/// test bench gets diagnosed again, and overwriting the first finding would erase the reason the customer
/// was quoted what they were quoted.
/// </summary>
public class RepairDiagnosis : TenantEntity
{
    public Guid RepairTicketId { get; set; }
    public RepairTicket RepairTicket { get; set; } = null!;

    public Guid? TechnicianId { get; set; }

    public string Findings { get; set; } = null!;

    public string? RecommendedAction { get; set; }

    /// <summary>What the technician thinks the fix will cost the customer. Null on a warranty job.</summary>
    public decimal? EstimatedCost { get; set; }

    public DateTimeOffset DiagnosedAt { get; set; }
}

/// <summary>
/// A part fitted into the customer's machine.
///
/// <b>It left the shelf when it was consumed, not when the job was billed</b> (§45 D9). A screen fitted on
/// Tuesday is physically gone on Tuesday, whether or not the customer ever pays and whether or not anyone
/// raises an invoice. Deferring the stock movement to invoicing would leave the shop selling parts that
/// are already inside somebody's laptop.
///
/// So this row is written by a handler that has just posted <see cref="MovementType.RepairConsumption"/>
/// through <c>IStockLedger</c>, and <see cref="UnitCost"/> is what the ledger came back with.
/// </summary>
public class RepairPart : TenantEntity
{
    public Guid RepairTicketId { get; set; }
    public RepairTicket RepairTicket { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Which warehouse the part came off. Needed to put it back if the job is reversed.</summary>
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>Set when the part itself is serial-tracked — a replacement board with its own number.</summary>
    public Guid? SerialId { get; set; }
    public Serial? Serial { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// COGS — the warehouse's moving average at the instant the part left the shelf, snapshotted (§45 D1).
    /// The caller does not choose it; the ledger reports it.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>What the customer is charged for it, before tax. Ignored entirely when not chargeable.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// False on a warranty job, and false for a part the shop is eating for any other reason (goodwill, a
    /// technician's mistake).
    ///
    /// <b>It suppresses the charge, never the cost.</b> The part is gone from stock either way — see
    /// <see cref="RepairTicket.GrossProfit"/> for why that asymmetry is the point.
    /// </summary>
    public bool IsChargeable { get; set; } = true;

    /// <summary>
    /// The part was taken back out and returned to stock (a <see cref="MovementType.RepairReturn"/>).
    /// The row stays — it is the record that the part was fitted and then was not — but it stops counting.
    /// </summary>
    public bool IsReturned { get; set; }

    public DateTimeOffset? ReturnedAt { get; set; }

    public DateTimeOffset ConsumedAt { get; set; }

    public string? Notes { get; set; }

    public decimal CostTotal => IsReturned ? 0m : SalesMath.Round(Quantity * UnitCost);

    public decimal ChargeTotal =>
        IsReturned || !IsChargeable ? 0m : SalesMath.Round(Quantity * UnitPrice);
}

/// <summary>
/// The technician's time (requirements §28).
///
/// <b>There is no cost rate here, only a charge rate</b>, and the omission is deliberate. The shop pays
/// the technician's salary whether the bench is busy or empty; it is a payroll expense (§34), not a cost
/// this job caused. Inventing an hourly cost to apportion it would make a quiet week show a loss on every
/// ticket and tell the manager nothing they did not already know about the quiet week.
/// </summary>
public class RepairLabour : TenantEntity
{
    public Guid RepairTicketId { get; set; }
    public RepairTicket RepairTicket { get; set; } = null!;

    public Guid? TechnicianId { get; set; }

    public string Description { get; set; } = null!;

    public decimal Hours { get; set; }

    /// <summary>What the customer is charged per hour, before tax.</summary>
    public decimal HourlyRate { get; set; }

    /// <summary>False on a warranty job: the time was really spent, but the customer is not billed for it.</summary>
    public bool IsChargeable { get; set; } = true;

    public DateTimeOffset WorkedAt { get; set; }

    public decimal ChargeTotal => IsChargeable ? SalesMath.Round(Hours * HourlyRate) : 0m;
}

/// <summary>Where an outsourced job has got to (requirements §29).</summary>
public enum OutsourcingStatus : short
{
    /// <summary>At the vendor.</summary>
    Sent = 1,

    /// <summary>Back from the vendor, and now the shop's problem again.</summary>
    Returned = 2,

    /// <summary>Recalled without being done.</summary>
    Cancelled = 3
}

/// <summary>
/// The job, or part of it, sent out to a third party (requirements §29) — a board-level repair the shop
/// cannot do in-house.
///
/// <b>No stock moves.</b> The device belongs to the customer; sending it to a vendor does not make it the
/// vendor's, and it never was the shop's to move. What this records is a <em>cost</em>: what the vendor
/// charges lands on the ticket and comes straight off its margin.
/// </summary>
public class RepairOutsourcing : TenantEntity
{
    public Guid RepairTicketId { get; set; }
    public RepairTicket RepairTicket { get; set; } = null!;

    /// <summary>A supplier of type repair vendor (§P2). The shop owes them money like any other supplier.</summary>
    public Guid VendorSupplierId { get; set; }
    public Supplier VendorSupplier { get; set; } = null!;

    public OutsourcingStatus Status { get; set; } = OutsourcingStatus.Sent;

    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? ExpectedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }

    /// <summary>What the vendor charged, in the currency they charged it in.</summary>
    public decimal Cost { get; set; }

    public string CurrencyCode { get; set; } = "AED";

    /// <summary>
    /// The rate used to bring <see cref="Cost"/> into the company's base currency, snapshotted at the time —
    /// the same reason a supplier invoice snapshots its rate (P4). A repair vendor abroad bills in dollars;
    /// the margin on the job has to be in the money the shop keeps its books in.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    public string? Notes { get; set; }

    public decimal CostInBaseCurrency =>
        Status == OutsourcingStatus.Cancelled ? 0m : SalesMath.Round(Cost * ExchangeRate);

    public void Receive(decimal cost, DateTimeOffset at)
    {
        if (Status != OutsourcingStatus.Sent)
        {
            throw new DomainException($"An outsourced job that is {Status} is not out at a vendor.");
        }

        if (cost < 0)
        {
            throw new DomainException("A vendor's charge cannot be negative.");
        }

        Cost = cost;
        ReceivedAt = at;
        Status = OutsourcingStatus.Returned;
    }

    public void Cancel()
    {
        if (Status == OutsourcingStatus.Returned)
        {
            throw new DomainException(
                "The vendor has already done the work and charged for it. Cancelling the record would "
                + "drop a cost the shop has genuinely incurred.");
        }

        Status = OutsourcingStatus.Cancelled;
    }
}

/// <summary>
/// The link from a job to the bill raised for it.
///
/// <b>A repair is billed on an ordinary <see cref="SalesInvoice"/>, not on a document of its own</b>
/// (§45 D11). The alternative — a `repair_invoices` table — would need its own tax arithmetic, its own
/// payment allocations, its own credit notes and its own place in the receivables report, and every one of
/// those already exists and is proven. A repair bill is a bill; the customer does not care which department
/// raised it, and neither does their balance.
///
/// The invoice moves no stock (the parts left when they were fitted), so its lines carry the cost the
/// ledger already reported rather than asking it for a new one.
/// </summary>
public class RepairCharge : TenantEntity
{
    public Guid RepairTicketId { get; set; }
    public RepairTicket RepairTicket { get; set; } = null!;

    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public DateTimeOffset ChargedAt { get; set; }
}
