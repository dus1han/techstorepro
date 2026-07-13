using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Barcodes;

/// <summary>One label, as the renderer needs it. No entity, no database — just what gets printed.</summary>
public record LabelData(
    string Code,
    string ProductName,
    string? Sku,
    string? Price,
    string? SerialNumber);

public record LabelSheet(
    LabelTemplate Template,
    BarcodeSymbology Symbology,
    IReadOnlyList<LabelData> Labels);

/// <summary>
/// Renders labels to a printable PDF (requirements §17). The server renders; the browser prints.
///
/// It is a PDF and not HTML because thermal printers need exact page geometry — a 50 × 25 mm label
/// laid out for A4 comes out of the printer blank or shredded across two labels, and no amount of CSS
/// makes that reliable across the printers a shop actually owns.
/// </summary>
public interface ILabelRenderer
{
    byte[] Render(LabelSheet sheet);
}

// --- Print --------------------------------------------------------------------------------------

public record PrintLabelsResult(byte[] Pdf, string FileName, int LabelCount);

/// <summary>
/// Prints labels for products (from the item master) or for individual serials — the two sources
/// requirements §17 asks for, plus the GRN source that P4 will add.
///
/// <b>What is encoded is the barcode if the product has one, and the SKU if it does not.</b> That
/// choice matters: the manufacturer's barcode is what a scanner reads off the box it came in, so
/// re-encoding our SKU over it would mean the same physical box scans as two different things
/// depending on which sticker the scanner happened to hit.
/// </summary>
[RequiresPermission(FeatureCatalog.Barcodes, PermissionAction.Print)]
public record PrintLabelsCommand(
    IReadOnlyCollection<Guid> ProductIds,
    int Copies = 1,
    BarcodeSymbology Symbology = BarcodeSymbology.Code128,
    LabelTemplate Template = LabelTemplate.Thermal50x25,
    bool IncludePrice = true,
    bool IncludeProductName = true,
    IReadOnlyCollection<string>? SerialNumbers = null) : IRequest<PrintLabelsResult>;

public class PrintLabelsCommandValidator : AbstractValidator<PrintLabelsCommand>
{
    public PrintLabelsCommandValidator()
    {
        RuleFor(x => x.Copies).InclusiveBetween(1, 100);

        RuleFor(x => x)
            .Must(x => x.ProductIds.Count > 0 || (x.SerialNumbers?.Count ?? 0) > 0)
            .WithMessage("Nothing to print: give at least one product or serial number.");
    }
}

public class PrintLabelsCommandHandler : IRequestHandler<PrintLabelsCommand, PrintLabelsResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ILabelRenderer _renderer;
    private readonly IDateTime _clock;

    public PrintLabelsCommandHandler(IApplicationDbContext db, ILabelRenderer renderer, IDateTime clock)
    {
        _db = db;
        _renderer = renderer;
        _clock = clock;
    }

    public async Task<PrintLabelsResult> Handle(PrintLabelsCommand request, CancellationToken cancellationToken)
    {
        var labels = new List<LabelData>();

        if (request.ProductIds.Count > 0)
        {
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => request.ProductIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Sku, p.Barcode, p.SellingPrice })
                .ToListAsync(cancellationToken);

            var missing = request.ProductIds.Except(products.Select(p => p.Id)).ToList();

            if (missing.Count > 0)
            {
                throw new NotFoundException("Product", missing[0]);
            }

            foreach (var product in products)
            {
                for (var copy = 0; copy < request.Copies; copy++)
                {
                    labels.Add(new LabelData(
                        Code: product.Barcode ?? product.Sku,
                        ProductName: request.IncludeProductName ? product.Name : string.Empty,
                        Sku: product.Sku,
                        Price: request.IncludePrice ? product.SellingPrice.ToString("N2") : null,
                        SerialNumber: null));
                }
            }
        }

        if (request.SerialNumbers is { Count: > 0 })
        {
            var normalised = request.SerialNumbers
                .Select(s => s.Trim().ToUpperInvariant())
                .ToList();

            var serials = await _db.Serials
                .AsNoTracking()
                .Where(s => normalised.Contains(s.SerialNumber))
                .Select(s => new { s.SerialNumber, ProductName = s.Product.Name, s.Product.Sku, s.Product.SellingPrice })
                .ToListAsync(cancellationToken);

            var missing = normalised.Except(serials.Select(s => s.SerialNumber)).ToList();

            if (missing.Count > 0)
            {
                throw new NotFoundException("Serial", missing[0]);
            }

            foreach (var serial in serials)
            {
                for (var copy = 0; copy < request.Copies; copy++)
                {
                    // A serial label encodes the serial, not the product's barcode. It has to identify
                    // this machine — that is the entire reason the machine has a serial.
                    labels.Add(new LabelData(
                        Code: serial.SerialNumber,
                        ProductName: request.IncludeProductName ? serial.ProductName : string.Empty,
                        Sku: serial.Sku,
                        Price: request.IncludePrice ? serial.SellingPrice.ToString("N2") : null,
                        SerialNumber: serial.SerialNumber));
                }
            }
        }

        var job = new BarcodePrintJob
        {
            SourceType = request.SerialNumbers is { Count: > 0 } ? BarcodeSource.Serial : BarcodeSource.Product,
            SourceId = request.ProductIds.Count == 1 ? request.ProductIds.First() : null,
            Symbology = request.Symbology,
            Template = request.Template,
            LabelCount = labels.Count,
            IncludePrice = request.IncludePrice,
            IncludeProductName = request.IncludeProductName,
            PrintedAt = _clock.UtcNow
        };

        // Refuses a runaway batch. 200 products × 100 copies is 20,000 labels, an entire roll of paper,
        // and a PDF nobody meant to ask for.
        job.Validate();

        _db.BarcodePrintJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        var pdf = _renderer.Render(new LabelSheet(request.Template, request.Symbology, labels));

        return new PrintLabelsResult(pdf, $"labels-{job.PrintedAt:yyyyMMdd-HHmmss}.pdf", labels.Count);
    }
}
