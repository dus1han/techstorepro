using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Reservations;

public record ReservationDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    Guid WarehouseId,
    string WarehouseName,
    string? SerialNumber,
    decimal Quantity,
    decimal FulfilledQuantity,
    decimal OutstandingQuantity,
    ReservationStatus Status,
    StockReferenceType ReferenceType,
    Guid? ReferenceId,
    string? ReferenceNumber,
    DateTimeOffset ReservedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ReleasedAt,
    string? Notes);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Reservations, PermissionAction.View)]
public record GetReservationsQuery(
    int Page = 1,
    int PageSize = 25,
    Guid? ProductId = null,
    Guid? WarehouseId = null,
    ReservationStatus? Status = null,
    bool? ActiveOnly = null) : IRequest<PagedResult<ReservationDto>>;

public class GetReservationsQueryHandler : IRequestHandler<GetReservationsQuery, PagedResult<ReservationDto>>
{
    private readonly IApplicationDbContext _db;

    public GetReservationsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ReservationDto>> Handle(
        GetReservationsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.StockReservations.AsNoTracking();

        if (request.ProductId is { } productId)
        {
            query = query.Where(r => r.ProductId == productId);
        }

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(r => r.WarehouseId == warehouseId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        if (request.ActiveOnly == true)
        {
            query = query.Where(r => r.Status == ReservationStatus.Active);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderByDescending(r => r.ReservedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReservationDto(
                r.Id,
                r.ProductId,
                r.Product.Name,
                r.Product.Sku,
                r.WarehouseId,
                r.Warehouse.Name,
                r.Serial != null ? r.Serial.SerialNumber : null,
                r.Quantity,
                r.FulfilledQuantity,
                r.Status == ReservationStatus.Active ? r.Quantity - r.FulfilledQuantity : 0m,
                r.Status,
                r.ReferenceType,
                r.ReferenceId,
                r.ReferenceNumber,
                r.ReservedAt,
                r.ExpiresAt,
                r.ReleasedAt,
                r.Notes))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReservationDto>(items, total, page, pageSize);
    }
}

// --- Reserve ------------------------------------------------------------------------------------

/// <summary>
/// Holds stock for a quote or an order (requirements §20).
///
/// P5 will call the ledger's <c>ReserveAsync</c> directly when a quote is raised. This command exists
/// so that the reservation mechanism is usable — and testable — before sales exists, and so that a
/// counter clerk can hold a machine for a customer who is coming back at five.
/// </summary>
[RequiresPermission(FeatureCatalog.Reservations, PermissionAction.Create)]
public record ReserveStockCommand(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    DateTimeOffset? ExpiresAt = null,
    string? SerialNumber = null,
    string? ReferenceNumber = null,
    string? Notes = null) : IRequest<Guid>;

public class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(x => DateTimeOffset.UtcNow)
            .When(x => x.ExpiresAt.HasValue)
            .WithMessage("A reservation cannot expire in the past.");
    }
}

public class ReserveStockCommandHandler : IRequestHandler<ReserveStockCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;

    public ReserveStockCommandHandler(IApplicationDbContext db, IStockLedger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task<Guid> Handle(ReserveStockCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        Guid? serialId = null;

        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            var normalised = request.SerialNumber.Trim().ToUpperInvariant();

            var serial = await _db.Serials
                .FirstOrDefaultAsync(s => s.SerialNumber == normalised, cancellationToken)
                ?? throw new Common.Exceptions.NotFoundException("Serial", normalised);

            serialId = serial.Id;
        }

        // Throws InsufficientStockException (422) if the units are already promised to someone else.
        // The check happens under the balance row's lock, inside this transaction — which is the only
        // way two clerks reserving the last unit at the same instant cannot both succeed.
        var reservation = await _ledger.ReserveAsync(
            request.WarehouseId,
            request.ProductId,
            request.Quantity,
            StockReferenceType.Delivery,
            referenceId: null,
            request.ReferenceNumber,
            request.ExpiresAt,
            serialId,
            request.Notes,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return reservation.Id;
    }
}

// --- Release ------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Reservations, PermissionAction.Delete)]
public record ReleaseReservationCommand(Guid Id) : IRequest;

public class ReleaseReservationCommandHandler : IRequestHandler<ReleaseReservationCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;

    public ReleaseReservationCommandHandler(IApplicationDbContext db, IStockLedger ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task Handle(ReleaseReservationCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        await _ledger.ReleaseAsync(request.Id, expired: false, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}

// --- Expire the forgotten ones ------------------------------------------------------------------

/// <summary>
/// Releases every reservation whose deadline has passed.
///
/// <b>Without this, the module leaks stock.</b> A quote that reserved the last laptop and was then
/// forgotten holds that laptop off the shelf forever: it is physically there, the balance says one, and
/// availability says zero, and nobody can explain why. The sweep is what makes a reservation a promise
/// rather than a slow leak.
///
/// Exposed as a command so that a scheduled job and an impatient manager can both run it.
/// </summary>
[RequiresPermission(FeatureCatalog.Reservations, PermissionAction.Delete)]
public record ExpireReservationsCommand : IRequest<int>;

public class ExpireReservationsCommandHandler : IRequestHandler<ExpireReservationsCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDateTime _clock;

    public ExpireReservationsCommandHandler(IApplicationDbContext db, IStockLedger ledger, IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<int> Handle(ExpireReservationsCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var expired = await _db.StockReservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Active
                && r.ExpiresAt != null
                && r.ExpiresAt <= now)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        foreach (var id in expired)
        {
            await _ledger.ReleaseAsync(id, expired: true, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return expired.Count;
    }
}
