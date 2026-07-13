using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using System.Linq.Expressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Adjustments;

public record AdjustmentLineDto(
    Guid ProductId,
    string ProductName,
    string Sku,
    string? SerialNumber,
    decimal Quantity,
    decimal UnitCost,
    decimal Value,
    string? Notes);

public record AdjustmentDto(
    Guid Id,
    string Number,
    Guid WarehouseId,
    string WarehouseName,
    AdjustmentReason Reason,
    string Explanation,
    DateTimeOffset AdjustedAt,
    decimal NetValue,
    Guid? StockCountId,
    IReadOnlyCollection<AdjustmentLineDto> Lines);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Adjustments, PermissionAction.View)]
public record GetAdjustmentsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? WarehouseId = null,
    AdjustmentReason? Reason = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IRequest<PagedResult<AdjustmentDto>>;

public class GetAdjustmentsQueryHandler : IRequestHandler<GetAdjustmentsQuery, PagedResult<AdjustmentDto>>
{
    private readonly IApplicationDbContext _db;

    public GetAdjustmentsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AdjustmentDto>> Handle(
        GetAdjustmentsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.StockAdjustments.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(a => a.Number.ToLower().Contains(term) || a.Explanation.ToLower().Contains(term));
        }

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(a => a.WarehouseId == warehouseId);
        }

        if (request.Reason is { } reason)
        {
            query = query.Where(a => a.Reason == reason);
        }

        if (request.From is { } from)
        {
            query = query.Where(a => a.AdjustedAt >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(a => a.AdjustedAt <= to);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderByDescending(a => a.AdjustedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(Project())
            .ToListAsync(cancellationToken);

        return new PagedResult<AdjustmentDto>(items, total, page, pageSize);
    }

    internal static Expression<Func<StockAdjustment, AdjustmentDto>> Project() =>
        a => new AdjustmentDto(
            a.Id,
            a.Number,
            a.WarehouseId,
            a.Warehouse.Name,
            a.Reason,
            a.Explanation,
            a.AdjustedAt,
            a.Lines.Sum(l => l.Quantity * l.UnitCost),
            a.StockCountId,
            a.Lines.Select(l => new AdjustmentLineDto(
                l.ProductId,
                l.Product.Name,
                l.Product.Sku,
                l.Serial != null ? l.Serial.SerialNumber : null,
                l.Quantity,
                l.UnitCost,
                l.Quantity * l.UnitCost,
                l.Notes)).ToList());
}

// --- Get one ------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Adjustments, PermissionAction.View)]
public record GetAdjustmentQuery(Guid Id) : IRequest<AdjustmentDto>;

public class GetAdjustmentQueryHandler : IRequestHandler<GetAdjustmentQuery, AdjustmentDto>
{
    private readonly IApplicationDbContext _db;

    public GetAdjustmentQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AdjustmentDto> Handle(GetAdjustmentQuery request, CancellationToken cancellationToken) =>
        await _db.StockAdjustments
            .AsNoTracking()
            .Where(a => a.Id == request.Id)
            .Select(GetAdjustmentsQueryHandler.Project())
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Adjustment", request.Id);
}

// --- Create -------------------------------------------------------------------------------------

/// <param name="Quantity">
/// Signed: positive writes stock on, negative writes it off. One document can do both — a shelf that
/// was three over on one product and two short on another is one event, and splitting it into two
/// documents would lose that it was one event.
/// </param>
/// <param name="UnitCost">
/// Required on a write-on, ignored on a write-off. Stock coming on has to be valued at *something*, and
/// that something raises the moving average — an opening-stock adjustment entered at the wrong cost
/// mis-values every future sale of the product. Stock going off is valued at what the warehouse already
/// thinks it is worth: you lose what the stock was worth, not what you wish it had been worth.
/// </param>
public record CreateAdjustmentLine(
    Guid ProductId,
    decimal Quantity,
    decimal? UnitCost = null,
    IReadOnlyCollection<string>? SerialNumbers = null,
    string? Notes = null);

[RequiresPermission(FeatureCatalog.Adjustments, PermissionAction.Create)]
public record CreateAdjustmentCommand(
    Guid WarehouseId,
    Guid BranchId,
    AdjustmentReason Reason,
    string Explanation,
    IReadOnlyCollection<CreateAdjustmentLine> Lines,
    DateTimeOffset? AdjustedAt = null) : IRequest<Guid>;

public class CreateAdjustmentCommandValidator : AbstractValidator<CreateAdjustmentCommand>
{
    public CreateAdjustmentCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();

        RuleFor(x => x.Explanation)
            .NotEmpty().WithMessage("Say why the stock is being adjusted: stock does not vanish for no reason.")
            .MaximumLength(1000);

        RuleFor(x => x.Lines).NotEmpty().WithMessage("An adjustment must have at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).NotEqual(0).WithMessage("A line of zero units adjusts nothing.");
            line.RuleFor(l => l.UnitCost)
                .NotNull()
                .GreaterThanOrEqualTo(0)
                .When(l => l.Quantity > 0)
                .WithMessage("Stock written on must have a unit cost: it is what raises the moving average.");
        });
    }
}

public class CreateAdjustmentCommandHandler : IRequestHandler<CreateAdjustmentCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public CreateAdjustmentCommandHandler(
        IApplicationDbContext db,
        IStockLedger ledger,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateAdjustmentCommand request, CancellationToken cancellationToken)
    {
        // One transaction around the document, its number and every movement it makes. If any line
        // fails — a write-off with no stock behind it, a serial that is already sold — the whole
        // adjustment rolls back, the document number is given back rather than burnt, and the ledger
        // is untouched. A half-posted adjustment would be worse than a rejected one.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var adjustedAt = request.AdjustedAt ?? _clock.UtcNow;

        var adjustment = new StockAdjustment
        {
            Number = await _numbers.NextAsync(DocumentType.StockAdjustment, request.BranchId, cancellationToken),
            WarehouseId = request.WarehouseId,
            BranchId = request.BranchId,
            Reason = request.Reason,
            Explanation = request.Explanation.Trim(),
            AdjustedAt = adjustedAt
        };

        _db.StockAdjustments.Add(adjustment);

        foreach (var line in request.Lines)
        {
            var writeOn = line.Quantity > 0;

            var result = await _ledger.PostAsync(
                new StockPosting(
                    WarehouseId: request.WarehouseId,
                    BranchId: request.BranchId,
                    ProductId: line.ProductId,
                    Type: writeOn ? MovementType.AdjustmentIn : MovementType.AdjustmentOut,
                    Quantity: Math.Abs(line.Quantity),
                    ReferenceType: StockReferenceType.StockAdjustment,
                    ReferenceId: adjustment.Id,
                    ReferenceNumber: adjustment.Number,
                    UnitCost: writeOn ? line.UnitCost : null,
                    SerialNumbers: line.SerialNumbers,
                    OccurredAt: adjustedAt,
                    Notes: line.Notes),
                cancellationToken);

            // One document line per movement, which for a serial-tracked product means one per machine.
            // The line takes the cost the ledger actually valued the movement at, not the cost that was
            // asked for — on a write-off those differ, and the document must show what really happened.
            foreach (var movement in result.Movements)
            {
                adjustment.Lines.Add(new StockAdjustmentLine
                {
                    StockAdjustmentId = adjustment.Id,
                    ProductId = line.ProductId,
                    SerialId = movement.SerialId,
                    Quantity = movement.Quantity,
                    UnitCost = movement.UnitCost,
                    Notes = line.Notes
                });
            }
        }

        // The entity's own rules, not the validator's: no code path — not this handler, not a later
        // one — may write an adjustment with no lines or no explanation.
        adjustment.Validate();

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return adjustment.Id;
    }
}
