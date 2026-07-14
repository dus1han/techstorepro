using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Application.Repairs.Tickets;

/// <summary>
/// The workshop workflow (requirements §28), one command per transition.
///
/// The transitions themselves are refused or allowed by <see cref="RepairTicket"/>, not here — a state
/// machine enforced in the handlers is a state machine that the next handler forgets about. What these add
/// is who did it, when, and the side effects that go with it.
/// </summary>
internal static class RepairTicketLoader
{
    /// <summary>
    /// Loads a ticket with the graph its transitions need.
    ///
    /// Parts are included on every load because <see cref="RepairTicket.Cancel"/> refuses a job that has
    /// parts fitted to it — and a Cancel that could not see the parts would happily cancel a machine with
    /// a new screen in it and lose the shop the screen.
    /// </summary>
    public static async Task<RepairTicket> LoadAsync(
        IApplicationDbContext db,
        Guid id,
        CancellationToken cancellationToken) =>
        await db.RepairTickets
            .Include(t => t.Parts)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
        ?? throw new NotFoundException("Repair ticket", id);
}

// --- Diagnosis -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Edit)]
public record BeginDiagnosisCommand(Guid RepairTicketId, Guid? TechnicianId = null) : IRequest<Unit>;

public class BeginDiagnosisCommandHandler : IRequestHandler<BeginDiagnosisCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public BeginDiagnosisCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(BeginDiagnosisCommand request, CancellationToken cancellationToken)
    {
        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        _db.RepairStatusChanges.Add(ticket.BeginDiagnosis(request.TechnicianId, _clock.UtcNow));

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// The technician has found the fault and priced the fix.
///
/// On a chargeable job this parks the ticket at <c>AwaitingApproval</c> — <b>the gate on the parts
/// store</b>. On a warranty job it goes straight to the bench: there is no price, so there is nobody to
/// agree to it (see <see cref="RepairTicket.RecordDiagnosis"/>).
/// </summary>
[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Edit)]
public record RecordDiagnosisCommand(
    Guid RepairTicketId,
    string Findings,
    string? RecommendedAction = null,
    decimal? EstimatedCost = null,
    Guid? TechnicianId = null) : IRequest<Unit>;

public class RecordDiagnosisCommandValidator : AbstractValidator<RecordDiagnosisCommand>
{
    public RecordDiagnosisCommandValidator()
    {
        RuleFor(x => x.RepairTicketId).NotEmpty();
        RuleFor(x => x.Findings).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.RecommendedAction).MaximumLength(2000);
        RuleFor(x => x.EstimatedCost).GreaterThanOrEqualTo(0).When(x => x.EstimatedCost.HasValue);
    }
}

public class RecordDiagnosisCommandHandler : IRequestHandler<RecordDiagnosisCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public RecordDiagnosisCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(RecordDiagnosisCommand request, CancellationToken cancellationToken)
    {
        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        var at = _clock.UtcNow;

        _db.RepairDiagnoses.Add(new RepairDiagnosis
        {
            RepairTicketId = ticket.Id,
            TechnicianId = request.TechnicianId ?? ticket.TechnicianId,
            Findings = request.Findings,
            RecommendedAction = request.RecommendedAction,
            EstimatedCost = request.EstimatedCost,
            DiagnosedAt = at
        });

        _db.RepairStatusChanges.Add(
            ticket.RecordDiagnosis(request.EstimatedCost, at, request.TechnicianId ?? ticket.TechnicianId));

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

// --- The customer's decision -----------------------------------------------------------------------

/// <summary>
/// The customer has agreed to the estimate. <b>This is what unlocks the parts store</b> (§28).
///
/// It carries <c>Approve</c> rather than <c>Edit</c> because it is the moment the shop is authorised to
/// spend the customer's money — and, through the parts it will consume, its own stock.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Approve)]
public record ApproveEstimateCommand(Guid RepairTicketId, Guid? ApprovedBy = null) : IRequest<Unit>;

public class ApproveEstimateCommandHandler : IRequestHandler<ApproveEstimateCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public ApproveEstimateCommandHandler(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(ApproveEstimateCommand request, CancellationToken cancellationToken)
    {
        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        // Who took the customer's yes. Defaulted to whoever is logged in, because that is who did.
        _db.RepairStatusChanges.Add(
            ticket.ApproveByCustomer(request.ApprovedBy ?? _user.UserId, _clock.UtcNow));

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// The customer looked at the estimate and said no. The device goes back untouched.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Approve)]
public record DeclineEstimateCommand(Guid RepairTicketId, string Reason) : IRequest<Unit>;

public class DeclineEstimateCommandValidator : AbstractValidator<DeclineEstimateCommand>
{
    public DeclineEstimateCommandValidator()
    {
        RuleFor(x => x.RepairTicketId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class DeclineEstimateCommandHandler : IRequestHandler<DeclineEstimateCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public DeclineEstimateCommandHandler(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(DeclineEstimateCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        _db.RepairStatusChanges.Add(ticket.DeclineByCustomer(request.Reason, _user.UserId, _clock.UtcNow));

        await ReleaseDeviceAsync(_db, ticket, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }

    /// <summary>
    /// The machine goes back to the customer, so the unit is no longer in the workshop. It returns to
    /// <see cref="SerialStatus.Sold"/> — where it was before it came in — and emphatically not to
    /// <c>InStock</c>, which would put a customer's own laptop back on the shelf to be sold again.
    /// </summary>
    internal static async Task ReleaseDeviceAsync(
        IApplicationDbContext db,
        RepairTicket ticket,
        CancellationToken cancellationToken)
    {
        if (ticket.DeviceSerialId is not { } serialId)
        {
            return;
        }

        var serial = await db.Serials.FirstAsync(s => s.Id == serialId, cancellationToken);

        if (serial.Status == SerialStatus.InRepair)
        {
            serial.Status = SerialStatus.Sold;
        }
    }
}

// --- Bench and collection --------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Edit)]
public record BeginTestingCommand(Guid RepairTicketId) : IRequest<Unit>;

public class BeginTestingCommandHandler : IRequestHandler<BeginTestingCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public BeginTestingCommandHandler(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(BeginTestingCommand request, CancellationToken cancellationToken)
    {
        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        _db.RepairStatusChanges.Add(ticket.BeginTesting(_user.UserId, _clock.UtcNow));

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Edit)]
public record MarkReadyCommand(Guid RepairTicketId) : IRequest<Unit>;

public class MarkReadyCommandHandler : IRequestHandler<MarkReadyCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public MarkReadyCommandHandler(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(MarkReadyCommand request, CancellationToken cancellationToken)
    {
        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        _db.RepairStatusChanges.Add(ticket.MarkReady(_user.UserId, _clock.UtcNow));

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// The customer collects the machine.
///
/// It does not require the bill to be paid. A shop that would not hand back a customer's own laptop until
/// an invoice cleared would be holding it hostage; the debt sits on the customer's balance, which is where
/// a debt belongs.
///
/// Any warranty claim still open is settled here: the repair happened and the shop paid for it, which is
/// exactly what "accepted" means.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Edit)]
public record DeliverDeviceCommand(
    Guid RepairTicketId,
    string? CollectedBy = null,
    DateTimeOffset? DeliveredAt = null) : IRequest<Unit>;

public class DeliverDeviceCommandHandler : IRequestHandler<DeliverDeviceCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public DeliverDeviceCommandHandler(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(DeliverDeviceCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        var at = request.DeliveredAt ?? _clock.UtcNow;

        _db.RepairStatusChanges.Add(
            ticket.Deliver(
                _user.UserId,
                at,
                request.CollectedBy is null ? null : $"Collected by {request.CollectedBy}"));

        await DeclineEstimateCommandHandler.ReleaseDeviceAsync(_db, ticket, cancellationToken);

        var claims = await _db.WarrantyClaims
            .Where(c => c.RepairTicketId == ticket.Id && c.Status == WarrantyClaimStatus.Open)
            .ToListAsync(cancellationToken);

        foreach (var claim in claims)
        {
            claim.Accept(at, $"Repaired under warranty on job {ticket.Number}.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}

[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Delete)]
public record CancelRepairCommand(Guid RepairTicketId, string Reason) : IRequest<Unit>;

public class CancelRepairCommandValidator : AbstractValidator<CancelRepairCommand>
{
    public CancelRepairCommandValidator()
    {
        RuleFor(x => x.RepairTicketId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class CancelRepairCommandHandler : IRequestHandler<CancelRepairCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public CancelRepairCommandHandler(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<Unit> Handle(CancelRepairCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var ticket = await RepairTicketLoader.LoadAsync(_db, request.RepairTicketId, cancellationToken);

        // Refused if parts are fitted — see RepairTicket.Cancel. Return them to stock first.
        _db.RepairStatusChanges.Add(ticket.Cancel(request.Reason, _user.UserId, _clock.UtcNow));

        await DeclineEstimateCommandHandler.ReleaseDeviceAsync(_db, ticket, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}
