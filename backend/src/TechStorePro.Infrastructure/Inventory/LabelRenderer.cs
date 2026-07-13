using TechStorePro.Application.Inventory.Barcodes;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace TechStorePro.Infrastructure.Inventory;

/// <summary>
/// Renders barcode and QR labels to a PDF (requirements §17).
///
/// The barcodes are drawn as <b>SVG, not bitmaps</b>. A rasterised barcode is the classic way to make
/// labels that scan on the developer's laser printer and fail on the shop's 203-dpi thermal printer:
/// the bars land between dots, the printer rounds them inconsistently, and every fifth label is
/// unreadable. Vector bars are rounded by the printer at its own resolution and stay legible.
///
/// It also keeps the server free of a native image dependency, which is what usually breaks a .NET PDF
/// pipeline the first time it runs on Linux in CI rather than on a developer's Windows box.
/// </summary>
public class LabelRenderer : ILabelRenderer
{
    public byte[] Render(LabelSheet sheet)
    {
        if (sheet.Labels.Count == 0)
        {
            throw new DomainException("There is nothing to print.");
        }

        return sheet.Template == LabelTemplate.A4Sheet65
            ? RenderSheet(sheet)
            : RenderRoll(sheet);
    }

    /// <summary>
    /// A continuous roll: one label per page, the page exactly the size of the label. This is what makes
    /// a thermal printer feed correctly — it advances one page, cuts, and stops.
    /// </summary>
    private byte[] RenderRoll(LabelSheet sheet)
    {
        var (width, height) = sheet.Template switch
        {
            LabelTemplate.Thermal50x25 => (50f, 25f),
            LabelTemplate.Thermal100x50 => (100f, 50f),
            _ => throw new DomainException($"{sheet.Template} is not a roll template.")
        };

        return Document.Create(container =>
        {
            foreach (var label in sheet.Labels)
            {
                container.Page(page =>
                {
                    page.Size(width, height, Unit.Millimetre);
                    page.Margin(1.5f, Unit.Millimetre);
                    page.DefaultTextStyle(text => text.FontSize(6));

                    page.Content().Element(element => Compose(element, label, sheet.Symbology, compact: true));
                });
            }
        }).GeneratePdf();
    }

    /// <summary>
    /// An A4 sticker sheet, 5 across × 13 down = 65 labels. Printed on the office laser printer when
    /// there is no thermal printer, which in a small shop is most of the time.
    /// </summary>
    private byte[] RenderSheet(LabelSheet sheet)
    {
        const int columns = 5;
        const int rows = 13;
        const int perPage = columns * rows;

        var pages = sheet.Labels
            .Select((label, index) => (label, index))
            .GroupBy(x => x.index / perPage)
            .Select(g => g.Select(x => x.label).ToList())
            .ToList();

        return Document.Create(container =>
        {
            foreach (var pageLabels in pages)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(8, Unit.Millimetre);
                    page.DefaultTextStyle(text => text.FontSize(6));

                    page.Content().Column(column =>
                    {
                        column.Spacing(2);

                        foreach (var rowLabels in Chunk(pageLabels, columns))
                        {
                            column.Item().Row(row =>
                            {
                                row.Spacing(2);

                                foreach (var label in rowLabels)
                                {
                                    row.RelativeItem().Border(0.25f).Padding(2)
                                        .Height(20, Unit.Millimetre)
                                        .Element(element => Compose(element, label, sheet.Symbology, compact: true));
                                }

                                // A short last row must not stretch its labels across the whole page: the
                                // stickers are pre-cut, and a barcode printed over the gap between two of
                                // them is a barcode that never scans.
                                for (var empty = rowLabels.Count; empty < columns; empty++)
                                {
                                    row.RelativeItem();
                                }
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    private static IEnumerable<List<LabelData>> Chunk(IReadOnlyList<LabelData> labels, int size)
    {
        for (var start = 0; start < labels.Count; start += size)
        {
            yield return labels.Skip(start).Take(size).ToList();
        }
    }

    private void Compose(IContainer container, LabelData label, BarcodeSymbology symbology, bool compact)
    {
        container.Column(column =>
        {
            column.Spacing(compact ? 1 : 2);

            if (!string.IsNullOrWhiteSpace(label.ProductName))
            {
                // One line, clipped. A product name that wraps onto three lines pushes the barcode off
                // a 25 mm label, and a label with no barcode on it is a blank sticker.
                column.Item().Text(label.ProductName)
                    .FontSize(compact ? 5.5f : 8)
                    .SemiBold()
                    .ClampLines(1);
            }

            column.Item().AlignCenter().Height(compact ? 8 : 14, Unit.Millimetre)
                .Svg(BarcodeSvg(label.Code, symbology));

            column.Item().AlignCenter().Text(label.Code).FontSize(compact ? 5 : 7);

            if (!string.IsNullOrWhiteSpace(label.Price))
            {
                column.Item().AlignCenter().Text(label.Price).FontSize(compact ? 7 : 10).Bold();
            }
        });
    }

    /// <summary>
    /// Encodes the code as an SVG barcode.
    ///
    /// EAN-13 is <b>not</b> silently substituted when the code does not fit it: an EAN-13 encoder given
    /// eleven digits will happily pad or truncate, and the result is a barcode that scans as a
    /// <em>different product</em>. Failing loudly is the only safe behaviour — a wrong barcode on a box
    /// is worse than no barcode on a box.
    /// </summary>
    private static string BarcodeSvg(string code, BarcodeSymbology symbology)
    {
        var format = symbology switch
        {
            BarcodeSymbology.Code128 => BarcodeFormat.CODE_128,
            BarcodeSymbology.Ean13 => BarcodeFormat.EAN_13,
            BarcodeSymbology.QrCode => BarcodeFormat.QR_CODE,
            _ => BarcodeFormat.CODE_128
        };

        if (symbology == BarcodeSymbology.Ean13 && (code.Length != 13 || !code.All(char.IsDigit)))
        {
            throw new DomainException(
                $"'{code}' cannot be printed as EAN-13: that symbology is exactly 13 digits. "
                + "Use Code 128, which encodes any code, or correct the product's barcode.");
        }

        var writer = new BarcodeWriterSvg
        {
            Format = format,
            Options = new EncodingOptions
            {
                // The pixel dimensions are the SVG's aspect ratio, not its printed size — QuestPDF
                // scales the vector into the box the label gives it. Squarer for QR, wide for a barcode.
                Width = symbology == BarcodeSymbology.QrCode ? 100 : 220,
                Height = symbology == BarcodeSymbology.QrCode ? 100 : 60,
                Margin = 0,
                PureBarcode = true
            }
        };

        return writer.Write(code).Content;
    }
}
