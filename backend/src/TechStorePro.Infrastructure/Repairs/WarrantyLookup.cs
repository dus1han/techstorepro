using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Repairs.Services;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Infrastructure.Repairs;

/// <inheritdoc cref="IWarrantyLookup"/>
public class WarrantyLookup : IWarrantyLookup
{
    private readonly IApplicationDbContext _db;

    public WarrantyLookup(IApplicationDbContext db) => _db = db;

    public async Task<WarrantyCover> FindAsync(
        string? serialNumber,
        Guid? productId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var trimmed = serialNumber?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            // No serial, no cover. A warranty is a promise about a specific machine, and "a laptop of this
            // model" is not one — honouring it on quantity alone would let one customer claim on another's.
            return new WarrantyCover(
                null, productId, null, RepairWarrantyType.None, null, null,
                "No serial number given, so no warranty can be matched to this device.");
        }

        var serial = await _db.Serials
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SerialNumber == trimmed, cancellationToken);

        var day = DateOnly.FromDateTime(at.UtcDateTime);

        // A registered manufacturer's or supplier's warranty. Checked first, and preferred where both
        // apply: if the manufacturer will pay for the board, the shop should not.
        var registered = await _db.Warranties
            .AsNoTracking()
            .Include(w => w.Product)
            .Where(w => w.SerialNumber == trimmed || (serial != null && w.SerialId == serial.Id))
            .Where(w => w.StartsOn <= day && w.EndsOn >= day)
            .OrderBy(w => w.WarrantyType == RepairWarrantyType.Shop ? 1 : 0)
            .ThenByDescending(w => w.EndsOn)
            .FirstOrDefaultAsync(cancellationToken);

        if (registered is not null)
        {
            var until = new DateTimeOffset(registered.EndsOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            return new WarrantyCover(
                serial?.Id,
                registered.ProductId,
                serial?.SoldInvoiceLineId,
                registered.WarrantyType,
                registered.Id,
                until,
                $"{Describe(registered.WarrantyType)} warranty on {registered.Product.Name}, "
                + $"registered until {registered.EndsOn:dd MMM yyyy}.");
        }

        if (serial is null)
        {
            return new WarrantyCover(
                null, productId, null, RepairWarrantyType.None, null, null,
                $"Serial {trimmed} is not one this shop has sold or registered, so there is no warranty on file.");
        }

        // The shop's own warranty, which is not stored anywhere: P5 computed it at the moment of sale from
        // the product's WarrantyMonths and stamped it on the unit. This is that stamp being read back.
        if (serial.IsUnderWarrantyAt(at))
        {
            var invoiceNumber = serial.SoldInvoiceLineId is { } lineId
                ? await _db.SalesInvoiceLines
                    .AsNoTracking()
                    .Where(l => l.Id == lineId)
                    .Select(l => l.SalesInvoice.Number)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            var soldOn = invoiceNumber is null ? "" : $", sold on invoice {invoiceNumber}";

            return new WarrantyCover(
                serial.Id,
                serial.ProductId,
                serial.SoldInvoiceLineId,
                RepairWarrantyType.Shop,
                null,
                serial.WarrantyUntil,
                $"Shop warranty{soldOn}, expires {serial.WarrantyUntil:dd MMM yyyy}.");
        }

        var expired = serial.WarrantyUntil is { } expiredOn
            ? $" The shop warranty expired on {expiredOn:dd MMM yyyy}."
            : " No shop warranty was sold with it.";

        return new WarrantyCover(
            serial.Id,
            serial.ProductId,
            serial.SoldInvoiceLineId,
            RepairWarrantyType.None,
            null,
            serial.WarrantyUntil,
            $"Serial {trimmed} is on file but is not under warranty today.{expired}");
    }

    private static string Describe(RepairWarrantyType type) => type switch
    {
        RepairWarrantyType.Manufacturer => "Manufacturer",
        RepairWarrantyType.Supplier => "Supplier",
        RepairWarrantyType.Shop => "Shop",
        _ => "No"
    };
}
