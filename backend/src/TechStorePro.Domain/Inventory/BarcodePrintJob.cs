using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Inventory;

/// <summary>What is encoded in the stripes. Requirements §17 asks for barcodes and QR codes.</summary>
public enum BarcodeSymbology : short
{
    /// <summary>The retail default: alphanumeric, dense, and every scanner on the counter reads it.</summary>
    Code128 = 1,

    /// <summary>13 digits, no more, no less — a manufacturer's retail barcode.</summary>
    Ean13 = 2,

    /// <summary>Holds a serial number and a URL. What a phone camera reads.</summary>
    QrCode = 3
}

/// <summary>
/// The physical stationery. Geometry, not decoration: a label rendered for A4 and sent to a thermal
/// printer comes out unreadable or blank, so the template decides the page size and nothing else does.
/// </summary>
public enum LabelTemplate : short
{
    /// <summary>A continuous roll, one label per page. 50 × 25 mm — the common shelf-label size.</summary>
    Thermal50x25 = 1,

    /// <summary>A bigger roll label, room for a product name that is not truncated. 100 × 50 mm.</summary>
    Thermal100x50 = 2,

    /// <summary>A4 sheet, 65 labels (5 × 13). Printed on an office laser printer.</summary>
    A4Sheet65 = 3
}

/// <summary>
/// A record that labels were printed (requirements §17).
///
/// It exists to answer "were these already labelled?" — reprinting a batch of serial labels and
/// sticking a second, different barcode on a machine is a real and expensive mistake in a shop. It is
/// also the batch-printing unit: one job, one PDF, many labels.
/// </summary>
public class BarcodePrintJob : TenantEntity
{
    /// <summary>What the labels were printed for. §17: "from item master" and "from GRN".</summary>
    public BarcodeSource SourceType { get; set; }

    /// <summary>The product, or the goods receipt (P4). Null for an ad-hoc batch.</summary>
    public Guid? SourceId { get; set; }

    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.Code128;
    public LabelTemplate Template { get; set; } = LabelTemplate.Thermal50x25;

    /// <summary>How many labels the job produced, across every item in it.</summary>
    public int LabelCount { get; set; }

    public bool IncludePrice { get; set; } = true;
    public bool IncludeProductName { get; set; } = true;

    public DateTimeOffset PrintedAt { get; set; }

    public void Validate()
    {
        if (LabelCount is < 1 or > 5000)
        {
            // The ceiling is not arbitrary: a runaway "copies" field is how a thermal printer eats a
            // whole roll, and a 100,000-label PDF is how the API runs out of memory.
            throw new DomainException("A print job must produce between 1 and 5,000 labels.");
        }
    }
}

public enum BarcodeSource : short
{
    Product = 1,
    GoodsReceipt = 2,
    Serial = 3
}
