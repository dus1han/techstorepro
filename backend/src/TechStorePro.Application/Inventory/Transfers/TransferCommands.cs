using System.Linq.Expressions;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Transfers;

public record TransferLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    string? SerialNumber,
    decimal Quantity,
    decimal ReceivedQuantity,
    decimal ShortfallQuantity,
    decimal UnitCost);

public record TransferDto(
    Guid Id,
    string Number,
    Guid FromWarehouseId,
    string FromWarehouseName,
    Guid ToWarehouseId,
    string ToWarehouseName,
    TransferStatus Status,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ReceivedAt,
    bool HasShortfall,
    string? Notes,
    IReadOnlyCollection<TransferLineDto> Lines);

// --- List / get ---------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Transfers, PermissionAction.View)]
public record GetTransfersQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    TransferStatus? Status = null,
    Guid? WarehouseId = null) : IRequest<PagedResult<TransferDto>>;

public class GetTransfersQueryHandler : IRequestHandler<GetTransfersQuery, PagedResult<TransferDto>>
{
    private readonly IApplicationDbContext _db;

    public GetTransfersQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<TransferDto>> Handle(
        GetTransfersQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.StockTransfers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(t => t.Number.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        // Either end of the move: "show me this warehouse's transfers" means both what it sent and what
        // it is waiting for.
        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(t => t.FromWarehouseId == warehouseId || t.ToWarehouseId == warehouseId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(Project())
            .ToListAsync(cancellationToken);

        return new PagedResult<TransferDto>(items, total, page, pageSize);
    }

    internal static Expression<Func<StockTransfer, TransferDto>> Project() =>
        t => new TransferDto(
            t.Id,
            t.Number,
            t.FromWarehouseId,
            t.FromWarehouse.Name,
            t.ToWarehouseId,
            t.ToWarehouse.Name,
            t.Status,
            t.ShippedAt,
            t.ReceivedAt,
            t.Lines.Any(l => l.ReceivedQuantity < l.Quantity),
            t.Notes,
            t.Lines.Select(l => new TransferLineDto(
                l.Id,
                l.ProductId,
                l.Product.Name,
                l.Product.Sku,
                l.Serial != null ? l.Serial.SerialNumber : null,
                l.Quantity,
                l.ReceivedQuantity,
                l.Quantity - l.ReceivedQuantity,
                l.UnitCost)).ToList());
}

[RequiresPermission(FeatureCatalog.Transfers, PermissionAction.View)]
public record GetTransferQuery(Guid Id) : IRequest<TransferDto>;

public class GetTransferQueryHandler : IRequestHandler<GetTransferQuery, TransferDto>
{
    private readonly IApplicationDbContext _db;

    public GetTransferQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<TransferDto> Handle(GetTransferQuery request, CancellationToken cancellationToken) =>
        await _db.StockTransfers
            .AsNoTracking()
            .Where(t => t.Id == request.Id)
            .Select(GetTransfersQueryHandler.Project())
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Transfer", request.Id);
}

// --- Create (draft) -----------------------------------------------------------------------------

public record CreateTransferLine(
    Guid ProductId,
    decimal Quantity,
    IReadOnlyCollection<string>? SerialNumbers = null);

/// <summary>
/// Raises a transfer in <see cref="TransferStatus.Draft"/>. <b>Nothing moves yet.</b> Stock leaves the
/// source warehouse when the van is loaded (<see cref="ShipTransferCommand"/>), not when the paperwork
/// is typed — and a draft that is never shipped must not have made stock disappear in the meantime.
/// </summary>
[RequiresPermission(FeatureCatalog.Transfers, PermissionAction.Create)]
public record CreateTransferCommand(
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    Guid BranchId,
    IReadOnlyCollection<CreateTransferLine> Lines,
    string? Notes = null) : IRequest<Guid>;

public class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(x => x.FromWarehouseId).NotEmpty();
        RuleFor(x => x.ToWarehouseId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();

        RuleFor(x => x.ToWarehouseId)
            .NotEqual(x => x.FromWarehouseId)
            .WithMessage("A transfer must move stock between two different warehouses.");

        RuleFor(x => x.Lines).NotEmpty();

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}

public class CreateTransferCommandHandler : IRequestHandler<CreateTransferCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;

    public CreateTransferCommandHandler(IApplicationDbContext db, IDocumentNumberGenerator numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    public async Task<Guid> Handle(CreateTransferCommand request, CancellationToken cancellationToken)
    {
        // The transaction is here for the document number, not for the ledger: a draft posts nothing.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var transfer = new StockTransfer
        {
            Number = await _numbers.NextAsync(DocumentType.StockTransfer, request.BranchId, cancellationToken),
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            BranchId = request.BranchId,
            Status = TransferStatus.Draft,
            Notes = request.Notes
        };

        foreach (var line in request.Lines)
        {
            var serials = line.SerialNumbers?.ToList() ?? [];

            if (serials.Count > 0)
            {
                // A serial-tracked line becomes one line per machine, so that "what is on this transfer"
                // and "which machines left" are the same question.
                foreach (var serialNumber in serials)
                {
                    var normalised = serialNumber.Trim().ToUpperInvariant();

                    var serial = await _db.Serials
                        .FirstOrDefaultAsync(s => s.SerialNumber == normalised, cancellationToken)
                        ?? throw new NotFoundException("Serial", normalised);

                    transfer.Lines.Add(new StockTransferLine
                    {
                        StockTransferId = transfer.Id,
                        ProductId = line.ProductId,
                        SerialId = serial.Id,
                        Quantity = 1
                    });
                }
            }
            else
            {
                transfer.Lines.Add(new StockTransferLine
                {
                    StockTransferId = transfer.Id,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity
                });
            }
        }

        transfer.Validate();

        _db.StockTransfers.Add(transfer);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return transfer.Id;
    }
}

// --- Ship ---------------------------------------------------------------------------------------

/// <summary>
/// The van is loaded. Posts <see cref="MovementType.TransferOut"/> from the source, and the stock is
/// now in transit: owned by neither warehouse, sellable from neither, and visible as such. A
/// serial-tracked unit becomes <see cref="SerialStatus.InTransit"/>, which is what stops the shop it
/// left from selling a machine that is on a van.
/// </summary>
[RequiresPermission(FeatureCatalog.Transfers, PermissionAction.Edit)]
public record ShipTransferCommand(Guid Id) : IRequest;

public class ShipTransferCommandHandler : IRequestHandler<ShipTransferCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _currentUser;

    public ShipTransferCommandHandler(
        IApplicationDbContext db,
        IStockLedger ledger,
        IDateTime clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _ledger = ledger;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task Handle(ShipTransferCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var transfer = await _db.StockTransfers
            .Include(t => t.Lines)
            .ThenInclude(l => l.Serial)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Transfer", request.Id);

        var shippedAt = _clock.UtcNow;

        // Refuses a transfer that is already in transit or received. The state machine lives on the
        // entity so that a second click of "ship" cannot post the stock out twice.
        transfer.Ship(shippedAt, _currentUser.UserId);

        foreach (var line in transfer.Lines)
        {
            var result = await _ledger.PostAsync(
                new StockPosting(
                    WarehouseId: transfer.FromWarehouseId,
                    BranchId: transfer.BranchId,
                    ProductId: line.ProductId,
                    Type: MovementType.TransferOut,
                    Quantity: line.Quantity,
                    ReferenceType: StockReferenceType.StockTransfer,
                    ReferenceId: transfer.Id,
                    ReferenceNumber: transfer.Number,
                    SerialNumbers: line.Serial is null ? null : [line.Serial.SerialNumber],
                    OccurredAt: shippedAt),
                cancellationToken);

            // The source warehouse's average at the instant of shipping. This is the cost that will land
            // at the destination on receipt: a transfer moves stock, it does not create or destroy value,
            // and re-costing it at the destination's average would do exactly that.
            line.UnitCost = result.UnitCost;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

// --- Receive ------------------------------------------------------------------------------------

/// <param name="ReceivedQuantity">
/// What actually turned up, which may be less than what was sent. A shortfall is recorded, not
/// silently rounded away: the units are gone, somebody has to explain them, and the difference stays
/// visible on the transfer until they do.
/// </param>
public record ReceiveTransferLine(Guid LineId, decimal ReceivedQuantity);

[RequiresPermission(FeatureCatalog.Transfers, PermissionAction.Edit)]
public record ReceiveTransferCommand(
    Guid Id,
    IReadOnlyCollection<ReceiveTransferLine>? Lines = null) : IRequest;

public class ReceiveTransferCommandHandler : IRequestHandler<ReceiveTransferCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _currentUser;

    public ReceiveTransferCommandHandler(
        IApplicationDbContext db,
        IStockLedger ledger,
        IDateTime clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _ledger = ledger;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task Handle(ReceiveTransferCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var transfer = await _db.StockTransfers
            .Include(t => t.Lines)
            .ThenInclude(l => l.Serial)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Transfer", request.Id);

        var receivedAt = _clock.UtcNow;

        transfer.Receive(receivedAt, _currentUser.UserId);

        var declared = request.Lines?.ToDictionary(l => l.LineId, l => l.ReceivedQuantity);

        foreach (var line in transfer.Lines)
        {
            // No line list means "everything arrived", which is the overwhelmingly common case and the
            // one the receiving clerk should not have to re-type.
            var received = declared is null
                ? line.Quantity
                : declared.GetValueOrDefault(line.Id, 0m);

            if (received < 0 || received > line.Quantity)
            {
                throw new Domain.Exceptions.DomainException(
                    $"Cannot receive {received} of a line that shipped {line.Quantity}. "
                    + "More stock cannot arrive than left.");
            }

            line.ReceivedQuantity = received;

            if (received == 0)
            {
                // Nothing arrived. The stock is still out of the source warehouse — where it went is a
                // question for whoever loaded the van, and the shortfall on this transfer is the record
                // of it. Writing it back into the destination would be inventing stock.
                continue;
            }

            await _ledger.PostAsync(
                new StockPosting(
                    WarehouseId: transfer.ToWarehouseId,
                    BranchId: transfer.BranchId,
                    ProductId: line.ProductId,
                    Type: MovementType.TransferIn,
                    Quantity: received,
                    ReferenceType: StockReferenceType.StockTransfer,
                    ReferenceId: transfer.Id,
                    ReferenceNumber: transfer.Number,

                    // TransferIn carries its own cost — the source's average, captured at shipping —
                    // rather than taking the destination's. Otherwise moving stock between two shops
                    // would quietly revalue it, and a company could raise its own inventory value by
                    // shuttling a van back and forth.
                    UnitCost: line.UnitCost,
                    SerialNumbers: line.Serial is null ? null : [line.Serial.SerialNumber],
                    OccurredAt: receivedAt),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

// --- Cancel -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Transfers, PermissionAction.Delete)]
public record CancelTransferCommand(Guid Id) : IRequest;

public class CancelTransferCommandHandler : IRequestHandler<CancelTransferCommand>
{
    private readonly IApplicationDbContext _db;

    public CancelTransferCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(CancelTransferCommand request, CancellationToken cancellationToken)
    {
        var transfer = await _db.StockTransfers
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Transfer", request.Id);

        // Throws for anything already shipped: the stock physically left, and no status change brings
        // it back. It has to be received — short, if that is the truth — and transferred back.
        transfer.Cancel();

        await _db.SaveChangesAsync(cancellationToken);
    }
}
