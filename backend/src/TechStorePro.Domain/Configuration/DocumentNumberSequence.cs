using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Configuration;

/// <summary>
/// Document types that carry a company-visible number. Stored as smallint.
/// Later phases append their own; the numbering screen renders whatever is here.
/// </summary>
public enum DocumentType : short
{
    Quotation = 1,
    SalesOrder = 2,
    Invoice = 3,
    CreditNote = 4,
    DebitNote = 5,
    Payment = 6,
    PurchaseOrder = 7,
    GoodsReceipt = 8,
    SupplierPayment = 9,
    StockTransfer = 10,
    StockAdjustment = 11,
    StockCount = 12,
    RepairTicket = 13,
    Expense = 14,
    ImportShipment = 15
}

/// <summary>
/// A gapless counter for one document type, in one branch, in one year.
///
/// Keyed by branch because requirements §5 lists document numbering under <em>Branch details</em>.
/// The row is locked (<c>SELECT … FOR UPDATE</c>) inside the same transaction as the document being
/// inserted, so a rolled-back transaction gives the number back instead of burning it — "gapless"
/// is a claim an auditor will actually test.
/// </summary>
public class DocumentNumberSequence : TenantEntity
{
    public Guid BranchId { get; set; }
    public Branch? Branch { get; set; }

    public DocumentType DocumentType { get; set; }

    /// <summary>The year the sequence belongs to, or 0 if <see cref="ResetsAnnually"/> is false.</summary>
    public int Year { get; set; }

    /// <summary>e.g. "INV". Configurable, per requirements §11 ("Numbering rules").</summary>
    public string Prefix { get; set; } = null!;

    /// <summary>The number the next document will take.</summary>
    public long NextNumber { get; set; } = 1;

    /// <summary>Zero-padding width: 5 renders 42 as "00042".</summary>
    public int Padding { get; set; } = 5;

    public bool ResetsAnnually { get; set; } = true;

    /// <summary>
    /// Takes the next number and advances the counter. The caller must already hold the row lock —
    /// this method is pure arithmetic and cannot enforce that itself.
    /// </summary>
    public string Take()
    {
        if (NextNumber < 1)
        {
            throw new DomainException("Document number sequence is corrupt: next number is below 1.");
        }

        var number = NextNumber;
        NextNumber++;

        return Format(number);
    }

    /// <summary>INV-2026-00042, or INV-00042 when the sequence does not reset annually.</summary>
    public string Format(long number) =>
        ResetsAnnually
            ? $"{Prefix}-{Year:D4}-{number.ToString().PadLeft(Padding, '0')}"
            : $"{Prefix}-{number.ToString().PadLeft(Padding, '0')}";
}
