using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Purchasing;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Purchasing.Imports;

public record ImportChargeDto(
    Guid Id,
    ImportChargeType Type,
    string? Description,
    string? Vendor,
    decimal Amount,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal AmountBase,
    DateTimeOffset IncurredAt);

public record ImportShipmentDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    ImportShipmentStatus Status,
    string? TransportDocument,
    string? VesselOrFlight,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ExpectedAt,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? CostedAt,
    decimal TotalCharges,
    decimal UnabsorbedCost,
    int ReceiptCount,
    IReadOnlyCollection<ImportChargeDto> Charges);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.ImportShipments, PermissionAction.View)]
public record GetImportShipmentsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    ImportShipmentStatus? Status = null) : IRequest<PagedResult<ImportShipmentDto>>;

public class GetImportShipmentsQueryHandler
    : IRequestHandler<GetImportShipmentsQuery, PagedResult<ImportShipmentDto>>
{
    private readonly IApplicationDbContext _db;

    public GetImportShipmentsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ImportShipmentDto>> Handle(
        GetImportShipmentsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.ImportShipments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(s =>
                s.Number.ToLower().Contains(term)
                || (s.TransportDocument != null && s.TransportDocument.ToLower().Contains(term)));
        }

        if (request.Status is { } status)
        {
            query = query.Where(s => s.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new ImportShipmentDto(
                s.Id,
                s.Number,
                s.SupplierId,
                s.Supplier.Name,
                s.Status,
                s.TransportDocument,
                s.VesselOrFlight,
                s.ShippedAt,
                s.ExpectedAt,
                s.ArrivedAt,
                s.CostedAt,
                s.Charges.Sum(c => c.Amount * c.ExchangeRate),
                s.UnabsorbedCost,
                s.Receipts.Count,
                s.Charges.Select(c => new ImportChargeDto(
                    c.Id,
                    c.Type,
                    c.Description,
                    c.Vendor,
                    c.Amount,
                    c.CurrencyCode,
                    c.ExchangeRate,
                    c.Amount * c.ExchangeRate,
                    c.IncurredAt)).ToList()))
            .ToListAsync(cancellationToken);

        return new PagedResult<ImportShipmentDto>(items, total, page, pageSize);
    }
}

// --- Create -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.ImportShipments, PermissionAction.Create)]
public record CreateImportShipmentCommand(
    Guid SupplierId,
    Guid BranchId,
    string? TransportDocument = null,
    string? VesselOrFlight = null,
    string? PortOfLoading = null,
    string? PortOfDischarge = null,
    DateTimeOffset? ShippedAt = null,
    DateTimeOffset? ExpectedAt = null,
    string? Notes = null) : IRequest<Guid>;

public class CreateImportShipmentCommandValidator : AbstractValidator<CreateImportShipmentCommand>
{
    public CreateImportShipmentCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
    }
}

public class CreateImportShipmentCommandHandler : IRequestHandler<CreateImportShipmentCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;

    public CreateImportShipmentCommandHandler(IApplicationDbContext db, IDocumentNumberGenerator numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    public async Task<Guid> Handle(CreateImportShipmentCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        if (!await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId, cancellationToken))
        {
            throw new NotFoundException("Supplier", request.SupplierId);
        }

        var shipment = new ImportShipment
        {
            Number = await _numbers.NextAsync(DocumentType.ImportShipment, request.BranchId, cancellationToken),
            SupplierId = request.SupplierId,
            BranchId = request.BranchId,
            Status = ImportShipmentStatus.InTransit,
            TransportDocument = request.TransportDocument,
            VesselOrFlight = request.VesselOrFlight,
            PortOfLoading = request.PortOfLoading,
            PortOfDischarge = request.PortOfDischarge,
            ShippedAt = request.ShippedAt,
            ExpectedAt = request.ExpectedAt,
            Notes = request.Notes
        };

        _db.ImportShipments.Add(shipment);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return shipment.Id;
    }
}

// --- Add a charge -------------------------------------------------------------------------------

/// <summary>
/// Bills the container for something that is not the goods: the shipping line's freight, the insurer's
/// premium, the customs authority's duty, the clearing agent's fee.
///
/// These arrive <em>after</em> the goods, which is the entire reason landed cost is a separate step
/// from receiving. Charges may be added right up until the shipment is costed — and not after, because
/// by then the money is already inside the moving average and adding more would need a second
/// apportionment.
/// </summary>
[RequiresPermission(FeatureCatalog.ImportShipments, PermissionAction.Edit)]
public record AddImportChargeCommand(
    Guid ShipmentId,
    ImportChargeType Type,
    decimal Amount,
    string CurrencyCode = "AED",
    decimal ExchangeRate = 1m,
    string? Description = null,
    string? Vendor = null,
    string? Reference = null,
    DateTimeOffset? IncurredAt = null) : IRequest<Guid>;

public class AddImportChargeCommandValidator : AbstractValidator<AddImportChargeCommand>
{
    public AddImportChargeCommandValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
    }
}

public class AddImportChargeCommandHandler : IRequestHandler<AddImportChargeCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public AddImportChargeCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(AddImportChargeCommand request, CancellationToken cancellationToken)
    {
        var shipment = await _db.ImportShipments
            .FirstOrDefaultAsync(s => s.Id == request.ShipmentId, cancellationToken)
            ?? throw new NotFoundException("Import shipment", request.ShipmentId);

        if (shipment.Status == ImportShipmentStatus.Costed)
        {
            throw new ConflictException(
                "This shipment has already been costed: its charges are inside the moving average of "
                + "every product it carried. A charge added now would not be apportioned to anything.");
        }

        if (shipment.Status == ImportShipmentStatus.Cancelled)
        {
            throw new ConflictException("That shipment is cancelled.");
        }

        var charge = new ImportShipmentCharge
        {
            ImportShipmentId = shipment.Id,
            Type = request.Type,
            Amount = request.Amount,
            CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            Description = request.Description,
            Vendor = request.Vendor,
            Reference = request.Reference,
            IncurredAt = request.IncurredAt ?? _clock.UtcNow
        };

        charge.Validate();

        // Added through the DbSet ONLY. EF's relationship fixup puts it into shipment.Charges by
        // itself, and adding it there by hand as well would put the same instance in the collection
        // twice — so TotalChargesBase would count the freight twice and every unit in the container
        // would carry double the landed cost.
        _db.ImportShipmentCharges.Add(charge);

        await _db.SaveChangesAsync(cancellationToken);

        return charge.Id;
    }
}
