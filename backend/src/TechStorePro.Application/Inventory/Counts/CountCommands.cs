using System.Linq.Expressions;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Inventory.Counts;

public record CountLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    string? SerialNumber,
    decimal SystemQuantity,
    decimal CountedQuantity,
    decimal Variance,
    decimal UnitCost,
    decimal VarianceValue,
    string? Notes);

public record CountDto(
    Guid Id,
    string Number,
    Guid WarehouseId,
    string WarehouseName,
    StockCountStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CountedAt,
    DateTimeOffset? ApprovedAt,
    Guid? StockAdjustmentId,
    decimal NetVarianceValue,
    int VarianceLineCount,
    string? Notes,
    IReadOnlyCollection<CountLineDto> Lines);

// --- List / get ---------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.StockCounts, PermissionAction.View)]
public record GetCountsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    StockCountStatus? Status = null,
    Guid? WarehouseId = null) : IRequest<PagedResult<CountDto>>;

public class GetCountsQueryHandler : IRequestHandler<GetCountsQuery, PagedResult<CountDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCountsQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<CountDto>> Handle(GetCountsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.StockCounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c => c.Number.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(c => c.WarehouseId == warehouseId);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderByDescending(c => c.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(Project())
            .ToListAsync(cancellationToken);

        return new PagedResult<CountDto>(items, total, page, pageSize);
    }

    internal static Expression<Func<StockCount, CountDto>> Project() =>
        c => new CountDto(
            c.Id,
            c.Number,
            c.WarehouseId,
            c.Warehouse.Name,
            c.Status,
            c.StartedAt,
            c.CountedAt,
            c.ApprovedAt,
            c.StockAdjustmentId,
            c.Lines.Sum(l => (l.CountedQuantity - l.SystemQuantity) * l.UnitCost),
            c.Lines.Count(l => l.CountedQuantity != l.SystemQuantity),
            c.Notes,
            c.Lines.Select(l => new CountLineDto(
                l.Id,
                l.ProductId,
                l.Product.Name,
                l.Product.Sku,
                l.Serial != null ? l.Serial.SerialNumber : null,
                l.SystemQuantity,
                l.CountedQuantity,
                l.CountedQuantity - l.SystemQuantity,
                l.UnitCost,
                (l.CountedQuantity - l.SystemQuantity) * l.UnitCost,
                l.Notes)).ToList());
}

[RequiresPermission(FeatureCatalog.StockCounts, PermissionAction.View)]
public record GetCountQuery(Guid Id) : IRequest<CountDto>;

public class GetCountQueryHandler : IRequestHandler<GetCountQuery, CountDto>
{
    private readonly IApplicationDbContext _db;

    public GetCountQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CountDto> Handle(GetCountQuery request, CancellationToken cancellationToken) =>
        await _db.StockCounts
            .AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(GetCountsQueryHandler.Project())
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Stock count", request.Id);
}

// --- Start --------------------------------------------------------------------------------------

/// <summary>Opens a count. Lines are added as the shelves are walked; nothing posts until approval.</summary>
[RequiresPermission(FeatureCatalog.StockCounts, PermissionAction.Create)]
public record StartCountCommand(Guid WarehouseId, Guid BranchId, string? Notes = null) : IRequest<Guid>;

public class StartCountCommandValidator : AbstractValidator<StartCountCommand>
{
    public StartCountCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
    }
}

public class StartCountCommandHandler : IRequestHandler<StartCountCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public StartCountCommandHandler(
        IApplicationDbContext db,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(StartCountCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        if (!await _db.Warehouses.AnyAsync(w => w.Id == request.WarehouseId, cancellationToken))
        {
            throw new NotFoundException("Warehouse", request.WarehouseId);
        }

        // One open count per warehouse. Two people counting the same shelves against two documents
        // would each snapshot a system quantity, each compute a variance from it, and both post — and
        // the second write-off would be against stock the first one had already written off.
        var alreadyCounting = await _db.StockCounts.AnyAsync(
            c => c.WarehouseId == request.WarehouseId
                && (c.Status == StockCountStatus.Counting || c.Status == StockCountStatus.PendingApproval),
            cancellationToken);

        if (alreadyCounting)
        {
            throw new ConflictException(
                "This warehouse already has a count in progress. Finish or cancel it first.");
        }

        var count = new StockCount
        {
            Number = await _numbers.NextAsync(DocumentType.StockCount, request.BranchId, cancellationToken),
            WarehouseId = request.WarehouseId,
            BranchId = request.BranchId,
            Status = StockCountStatus.Counting,
            StartedAt = _clock.UtcNow,
            Notes = request.Notes
        };

        _db.StockCounts.Add(count);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return count.Id;
    }
}

// --- Count a line -------------------------------------------------------------------------------

/// <summary>
/// Records what was found on the shelf for one product. Called once per scan, from the counting screen.
///
/// <b>The system quantity is snapshotted here</b>, at the moment of counting — not read again at
/// approval. A count that takes two hours while the shop keeps trading would otherwise compare this
/// morning's shelf against this afternoon's ledger and invent a variance out of the sales that
/// happened in between.
/// </summary>
[RequiresPermission(FeatureCatalog.StockCounts, PermissionAction.Edit)]
public record CountLineCommand(
    Guid CountId,
    Guid ProductId,
    decimal CountedQuantity,
    string? SerialNumber = null,
    string? Notes = null) : IRequest<Guid>;

public class CountLineCommandValidator : AbstractValidator<CountLineCommand>
{
    public CountLineCommandValidator()
    {
        RuleFor(x => x.CountId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
    }
}

public class CountLineCommandHandler : IRequestHandler<CountLineCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CountLineCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CountLineCommand request, CancellationToken cancellationToken)
    {
        var count = await _db.StockCounts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == request.CountId, cancellationToken)
            ?? throw new NotFoundException("Stock count", request.CountId);

        if (count.Status != StockCountStatus.Counting)
        {
            throw new DomainException($"A count that is {count.Status} is no longer being counted.");
        }

        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken))
        {
            throw new NotFoundException("Product", request.ProductId);
        }

        Guid? serialId = null;

        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            var normalised = request.SerialNumber.Trim().ToUpperInvariant();

            var serial = await _db.Serials
                .FirstOrDefaultAsync(s => s.SerialNumber == normalised, cancellationToken)
                ?? throw new NotFoundException("Serial", normalised);

            serialId = serial.Id;
        }

        var balance = await _db.StockBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.WarehouseId == count.WarehouseId && b.ProductId == request.ProductId,
                cancellationToken);

        var existing = count.Lines.FirstOrDefault(
            l => l.ProductId == request.ProductId && l.SerialId == serialId);

        if (existing is not null)
        {
            // Re-counting a product replaces the count, it does not add to it. A clerk who scans a shelf
            // twice has counted it twice, not found twice as much — and the unique index on
            // (count, product, serial) means the alternative was a crash, not a duplicate.
            existing.CountedQuantity = request.CountedQuantity;
            existing.Notes = request.Notes;

            await _db.SaveChangesAsync(cancellationToken);

            return existing.Id;
        }

        var line = new StockCountLine
        {
            StockCountId = count.Id,
            ProductId = request.ProductId,
            SerialId = serialId,
            SystemQuantity = balance?.Quantity ?? 0m,
            CountedQuantity = request.CountedQuantity,
            UnitCost = balance?.AverageCost ?? 0m,
            Notes = request.Notes
        };

        // Through the DbSet, not only the navigation collection: `count` was loaded from the database
        // and is Unchanged, and a child discovered on an Unchanged parent with an id already set is
        // taken by EF for an existing row — it would UPDATE a line that has never been inserted, match
        // zero rows, and throw. Every scan on the shelf would fail.
        // ONLY the DbSet — EF's fixup adds it to count.Lines itself, and adding it there by hand as
        // well would hold the same line twice, doubling the variance the count computes.
        _db.StockCountLines.Add(line);

        await _db.SaveChangesAsync(cancellationToken);

        return line.Id;
    }
}

// --- Submit -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.StockCounts, PermissionAction.Edit)]
public record SubmitCountCommand(Guid Id) : IRequest;

public class SubmitCountCommandHandler : IRequestHandler<SubmitCountCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public SubmitCountCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task Handle(SubmitCountCommand request, CancellationToken cancellationToken)
    {
        var count = await _db.StockCounts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Stock count", request.Id);

        count.SubmitForApproval(_clock.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);
    }
}

// --- Approve ------------------------------------------------------------------------------------

/// <summary>
/// Approves a count and posts its variance (requirements §21).
///
/// <b>This is the only place in the module where stock is created or destroyed on somebody's say-so</b>,
/// which is why it is a separate permission from counting. The person walking the shelves and the
/// person authorising the write-off should not have to be the same person — and with
/// <see cref="PermissionAction.Approve"/> granted separately, they need not be.
///
/// The variance posts as an ordinary <see cref="StockAdjustment"/>, through the ordinary ledger. It
/// gets a document number, a reason, and a line per product, exactly like a manual write-off — because
/// from the ledger's point of view that is precisely what it is.
/// </summary>
[RequiresPermission(FeatureCatalog.StockCounts, PermissionAction.Approve)]
public record ApproveCountCommand(Guid Id) : IRequest<Guid?>;

public class ApproveCountCommandHandler : IRequestHandler<ApproveCountCommand, Guid?>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _currentUser;

    public ApproveCountCommandHandler(
        IApplicationDbContext db,
        IStockLedger ledger,
        IDocumentNumberGenerator numbers,
        IDateTime clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _ledger = ledger;
        _numbers = numbers;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Guid?> Handle(ApproveCountCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var count = await _db.StockCounts
            .Include(c => c.Lines)
            .ThenInclude(l => l.Serial)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Stock count", request.Id);

        var approvedAt = _clock.UtcNow;
        var variances = count.Lines.Where(l => l.Variance != 0).ToList();

        if (variances.Count == 0)
        {
            // The shelf and the system agree. There is nothing to post, and raising an empty adjustment
            // document would put noise in the write-off report forever.
            count.Approve(_currentUser.UserId, approvedAt, adjustmentId: null);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var adjustment = new StockAdjustment
        {
            Number = await _numbers.NextAsync(DocumentType.StockAdjustment, count.BranchId, cancellationToken),
            WarehouseId = count.WarehouseId,
            BranchId = count.BranchId,
            Reason = AdjustmentReason.DataCorrection,
            Explanation = $"Variance from physical stock count {count.Number}.",
            AdjustedAt = approvedAt,
            StockCountId = count.Id
        };

        _db.StockAdjustments.Add(adjustment);

        foreach (var line in variances)
        {
            var writeOn = line.Variance > 0;

            var result = await _ledger.PostAsync(
                new StockPosting(
                    WarehouseId: count.WarehouseId,
                    BranchId: count.BranchId,
                    ProductId: line.ProductId,
                    Type: writeOn ? MovementType.CountAdjustmentIn : MovementType.CountAdjustmentOut,
                    Quantity: Math.Abs(line.Variance),
                    ReferenceType: StockReferenceType.StockCount,
                    ReferenceId: count.Id,
                    ReferenceNumber: count.Number,

                    // A count surplus is valued at the warehouse's existing average — the stock was
                    // always there, the ledger simply did not know. Inventing a fresh cost for it would
                    // move the average on the strength of a clerical error.
                    UnitCost: null,
                    SerialNumbers: line.Serial is null ? null : [line.Serial.SerialNumber],
                    OccurredAt: approvedAt,
                    Notes: line.Notes),
                cancellationToken);

            foreach (var movement in result.Movements)
            {
                var adjustmentLine = new StockAdjustmentLine
                {
                    StockAdjustmentId = adjustment.Id,
                    ProductId = line.ProductId,
                    SerialId = movement.SerialId,
                    Quantity = movement.Quantity,
                    UnitCost = movement.UnitCost,
                    Notes = line.Notes
                };

                // Through the DbSet: PostAsync has already saved, so `adjustment` is Unchanged and EF
                // would take these lines for existing rows and UPDATE nothing. See the same note in
                // CreateAdjustmentCommandHandler.
                // ONLY the DbSet — see the note in CreateAdjustmentCommandHandler.
                _db.StockAdjustmentLines.Add(adjustmentLine);
            }
        }

        adjustment.Validate();

        // Refuses to mark the count approved without the adjustment that posted its variance. A count
        // that says "approved" while the ledger never moved is the worst possible outcome: the shop
        // believes it has reconciled, and it has not.
        count.Approve(_currentUser.UserId, approvedAt, adjustment.Id);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return adjustment.Id;
    }
}

// --- Cancel -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.StockCounts, PermissionAction.Delete)]
public record CancelCountCommand(Guid Id) : IRequest;

public class CancelCountCommandHandler : IRequestHandler<CancelCountCommand>
{
    private readonly IApplicationDbContext _db;

    public CancelCountCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(CancelCountCommand request, CancellationToken cancellationToken)
    {
        var count = await _db.StockCounts
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Stock count", request.Id);

        count.Cancel();

        await _db.SaveChangesAsync(cancellationToken);
    }
}
