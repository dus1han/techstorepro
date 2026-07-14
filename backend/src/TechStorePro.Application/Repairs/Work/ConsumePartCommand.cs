using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Inventory.Services;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Application.Repairs.Work;

/// <summary>
/// Fit a part into the customer's machine (requirements §28).
///
/// <b>The part leaves the shelf now, not when the job is billed</b> (§45 D9). A screen fitted on Tuesday is
/// physically gone on Tuesday, whether or not the customer ever pays and whether or not anybody raises an
/// invoice. Deferring the stock movement to invoicing would leave the shop selling parts that are already
/// inside somebody's laptop — and a warranty job, which is never invoiced at all, would consume stock that
/// the system went on believing was available for ever.
///
/// It goes through <c>IStockLedger</c> like every other stock movement in the system (architecture.md §4.5),
/// which is what makes it show up in the balance audit, the valuation and the movement report without any
/// of them knowing that repairs exist.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairParts, PermissionAction.Create)]
public record ConsumePartCommand(
    Guid RepairTicketId,
    Guid ProductId,
    Guid WarehouseId,
    decimal Quantity,
    string? SerialNumber = null,
    decimal? UnitPrice = null,
    bool? IsChargeable = null,
    string? Notes = null) : IRequest<Guid>;

public class ConsumePartCommandValidator : AbstractValidator<ConsumePartCommand>
{
    public ConsumePartCommandValidator()
    {
        RuleFor(x => x.RepairTicketId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).When(x => x.UnitPrice.HasValue);
        RuleFor(x => x.SerialNumber).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class ConsumePartCommandHandler : IRequestHandler<ConsumePartCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly ISalesLinePricer _pricer;
    private readonly IDateTime _clock;

    public ConsumePartCommandHandler(
        IApplicationDbContext db,
        IStockLedger ledger,
        ISalesLinePricer pricer,
        IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _pricer = pricer;
        _clock = clock;
    }

    public async Task<Guid> Handle(ConsumePartCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var ticket = await _db.RepairTickets
            .Include(t => t.Parts)
            .FirstOrDefaultAsync(t => t.Id == request.RepairTicketId, cancellationToken)
            ?? throw new NotFoundException("Repair ticket", request.RepairTicketId);

        // The gate. Nothing may be fitted to a machine whose owner has not agreed to the price — parts
        // consumed against a job the customer then declines are parts the shop has paid for and cannot bill.
        ticket.EnsureWorkAllowed();

        var consumedAt = _clock.UtcNow;

        var serials = string.IsNullOrWhiteSpace(request.SerialNumber)
            ? null
            : new[] { request.SerialNumber.Trim() };

        var result = await _ledger.PostAsync(
            new StockPosting(
                WarehouseId: request.WarehouseId,
                BranchId: ticket.BranchId,
                ProductId: request.ProductId,
                Type: MovementType.RepairConsumption,
                Quantity: request.Quantity,
                ReferenceType: StockReferenceType.RepairTicket,
                ReferenceId: ticket.Id,
                ReferenceNumber: ticket.Number,

                // No UnitCost: an issue is valued at the warehouse's moving average, and the ledger is the
                // only thing that knows what that is right now. A cost passed in here would be the caller
                // inventing COGS.
                UnitCost: null,
                SerialNumbers: serials,
                OccurredAt: consumedAt,
                Notes: request.Notes),
            cancellationToken);

        // What to charge for it. On a warranty job the answer is nothing — but the part is gone from stock
        // either way, and RepairTicket.GrossProfit is where that asymmetry shows up as the loss it is.
        var chargeable = request.IsChargeable ?? !ticket.IsWarranty;

        if (chargeable && ticket.IsWarranty)
        {
            throw new DomainException(
                $"Job {ticket.Number} is under {ticket.WarrantyType} warranty, so the customer cannot be "
                + "charged for parts fitted to it. If the fault turns out not to be covered, reject the "
                + "warranty claim first — that is the decision that makes the job chargeable.");
        }

        var unitPrice = 0m;

        if (chargeable)
        {
            // Priced from the customer's tier and the price list, exactly as a sales line would be — a part
            // sold over the workshop counter is a part sold, and a repair that priced it differently from
            // the shop floor would be a discount nobody approved.
            var priced = await _pricer.PriceAsync(
                request.ProductId,
                ticket.CustomerId,
                request.Quantity,
                unitPriceOverride: request.UnitPrice,
                asOf: consumedAt,
                cancellationToken: cancellationToken);

            unitPrice = priced.UnitPrice;
        }

        var serialId = serials is null
            ? null
            : await _db.Serials
                .Where(s => s.SerialNumber == serials[0])
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var part = new RepairPart
        {
            RepairTicketId = ticket.Id,
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            SerialId = serialId,
            Quantity = request.Quantity,

            // COGS, snapshotted at the moment the part left the shelf (§45 D1). Recomputing it later would
            // restate the profit on every job the workshop has ever done, because the average keeps moving.
            UnitCost = result.UnitCost,

            UnitPrice = unitPrice,
            IsChargeable = chargeable,
            ConsumedAt = consumedAt,
            Notes = request.Notes
        };

        // Through the DbSet, and only the DbSet. Adding to the parent's collection as well double-counts
        // every total computed from it — the P3 bug that P4 found and fixed.
        _db.RepairParts.Add(part);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return part.Id;
    }
}

/// <summary>
/// Take a part back out and put it on the shelf — it was the wrong one, or the fault turned out to be
/// something else.
///
/// A <see cref="MovementType.RepairReturn"/>, which is a real movement and not an UPDATE undoing the first
/// one: the ledger is append-only, and a shop that could erase a consumption could erase the evidence that
/// a part ever went missing.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairParts, PermissionAction.Delete)]
public record ReturnPartCommand(Guid RepairPartId, string? Notes = null) : IRequest<Unit>;

public class ReturnPartCommandHandler : IRequestHandler<ReturnPartCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IStockLedger _ledger;
    private readonly IDateTime _clock;

    public ReturnPartCommandHandler(IApplicationDbContext db, IStockLedger ledger, IDateTime clock)
    {
        _db = db;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<Unit> Handle(ReturnPartCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var part = await _db.RepairParts
            .Include(p => p.RepairTicket)
            .Include(p => p.Serial)
            .FirstOrDefaultAsync(p => p.Id == request.RepairPartId, cancellationToken)
            ?? throw new NotFoundException("Repair part", request.RepairPartId);

        if (part.IsReturned)
        {
            throw new DomainException("That part has already been returned to stock.");
        }

        // A part on a job that has already been billed cannot simply be walked back into stock: the
        // customer has been charged for it. Credit the invoice instead — the same rule sales lives by,
        // where a cancellation and a credit note are not the same thing.
        var billed = await _db.RepairCharges
            .AnyAsync(c => c.RepairTicketId == part.RepairTicketId, cancellationToken);

        if (billed)
        {
            throw new DomainException(
                $"Job {part.RepairTicket.Number} has already been invoiced, so this part cannot be returned "
                + "to stock without giving the customer their money back. Raise a credit note instead.");
        }

        var serials = part.Serial is null ? null : new[] { part.Serial.SerialNumber };

        await _ledger.PostAsync(
            new StockPosting(
                WarehouseId: part.WarehouseId,
                BranchId: part.RepairTicket.BranchId,
                ProductId: part.ProductId,
                Type: MovementType.RepairReturn,
                Quantity: part.Quantity,
                ReferenceType: StockReferenceType.RepairTicket,
                ReferenceId: part.RepairTicketId,
                ReferenceNumber: part.RepairTicket.Number,

                // Valued at the warehouse's average, like any other write-on that carries no cost of its
                // own. The part is coming back to a shelf whose average may have moved since it left; using
                // the old cost would inject a price the warehouse no longer holds anything at.
                UnitCost: null,
                SerialNumbers: serials,
                OccurredAt: _clock.UtcNow,
                Notes: request.Notes ?? "Returned from repair"),
            cancellationToken);

        // The row stays — it is the record that the part was fitted and then was not — but it stops
        // counting toward the job's cost and its bill.
        part.IsReturned = true;
        part.ReturnedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// Book the technician's time to the job (requirements §28).
///
/// No stock, no ledger — an hour is not a thing on a shelf. What it is, is <em>revenue</em>: see
/// <see cref="RepairLabour"/> for why it deliberately carries no cost side.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairParts, PermissionAction.Create)]
public record LogLabourCommand(
    Guid RepairTicketId,
    string Description,
    decimal Hours,
    decimal HourlyRate,
    Guid? TechnicianId = null,
    bool? IsChargeable = null) : IRequest<Guid>;

public class LogLabourCommandValidator : AbstractValidator<LogLabourCommand>
{
    public LogLabourCommandValidator()
    {
        RuleFor(x => x.RepairTicketId).NotEmpty();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Hours).GreaterThan(0);
        RuleFor(x => x.HourlyRate).GreaterThanOrEqualTo(0);
    }
}

public class LogLabourCommandHandler : IRequestHandler<LogLabourCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public LogLabourCommandHandler(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<Guid> Handle(LogLabourCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _db.RepairTickets
            .FirstOrDefaultAsync(t => t.Id == request.RepairTicketId, cancellationToken)
            ?? throw new NotFoundException("Repair ticket", request.RepairTicketId);

        ticket.EnsureWorkAllowed();

        var chargeable = request.IsChargeable ?? !ticket.IsWarranty;

        if (chargeable && ticket.IsWarranty)
        {
            throw new DomainException(
                $"Job {ticket.Number} is under {ticket.WarrantyType} warranty. The time was really spent, "
                + "but the customer is not billed for it.");
        }

        var labour = new RepairLabour
        {
            RepairTicketId = ticket.Id,
            TechnicianId = request.TechnicianId ?? ticket.TechnicianId ?? _user.UserId,
            Description = request.Description,
            Hours = request.Hours,
            HourlyRate = request.HourlyRate,
            IsChargeable = chargeable,
            WorkedAt = _clock.UtcNow
        };

        _db.RepairLabour.Add(labour);

        await _db.SaveChangesAsync(cancellationToken);

        return labour.Id;
    }
}
