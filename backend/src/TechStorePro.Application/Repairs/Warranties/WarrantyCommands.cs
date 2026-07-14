using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Application.Repairs.Warranties;

/// <summary>
/// Record a manufacturer's or a supplier's warranty (requirements §30).
///
/// <b>The shop's own warranty is not registered here, and cannot be.</b> P5 already computes it at the
/// moment of sale and stamps it on the unit (<c>Serial.WarrantyUntil</c>, from the product's
/// <c>WarrantyMonths</c>). Registering it a second time would create two answers to "is this machine still
/// under warranty?", and the day they disagreed the shop would believe whichever one it happened to read.
///
/// What genuinely cannot be derived is somebody <em>else's</em> promise — a manufacturer's three years, a
/// supplier's twelve months on an imported batch. Those are terms on a piece of paper, and this is where
/// they are typed in.
/// </summary>
[RequiresPermission(FeatureCatalog.Warranties, PermissionAction.Create)]
public record RegisterWarrantyCommand(
    RepairWarrantyType WarrantyType,
    Guid ProductId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string? SerialNumber = null,
    WarrantySourceType SourceType = WarrantySourceType.Serial,
    Guid? SourceId = null,
    string? Terms = null) : IRequest<Guid>;

public class RegisterWarrantyCommandValidator : AbstractValidator<RegisterWarrantyCommand>
{
    public RegisterWarrantyCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.SerialNumber).MaximumLength(100);
        RuleFor(x => x.Terms).MaximumLength(2000);

        RuleFor(x => x.WarrantyType)
            .NotEqual(RepairWarrantyType.None)
            .WithMessage("A warranty that covers nobody is not a warranty.");

        RuleFor(x => x.WarrantyType)
            .NotEqual(RepairWarrantyType.Shop)
            .WithMessage(
                "The shop's own warranty is set by the sale, from the product's warranty months — it is "
                + "not registered by hand. Registering it here would give the same machine two expiry "
                + "dates that could disagree.");

        RuleFor(x => x.EndsOn)
            .GreaterThanOrEqualTo(x => x.StartsOn)
            .WithMessage("A warranty cannot end before it starts.");
    }
}

public class RegisterWarrantyCommandHandler : IRequestHandler<RegisterWarrantyCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public RegisterWarrantyCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(RegisterWarrantyCommand request, CancellationToken cancellationToken)
    {
        _ = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);

        var serialNumber = request.SerialNumber?.Trim();

        // Bind it to the unit when the shop knows the unit. The serial number is kept either way: a
        // warranty on a machine the shop never sold is still a warranty, and it is exactly the case the
        // intake lookup has to answer for a customer who bought elsewhere.
        var serialId = string.IsNullOrWhiteSpace(serialNumber)
            ? null
            : await _db.Serials
                .Where(s => s.SerialNumber == serialNumber)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var warranty = new Warranty
        {
            WarrantyType = request.WarrantyType,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SerialId = serialId,
            SerialNumber = serialNumber,
            ProductId = request.ProductId,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            Terms = request.Terms
        };

        warranty.Validate();

        _db.Warranties.Add(warranty);

        await _db.SaveChangesAsync(cancellationToken);

        return warranty.Id;
    }
}

/// <summary>
/// The fault turned out not to be covered — liquid damage, an out-of-warranty part, a machine somebody
/// opened themselves.
///
/// <b>This is the decision that makes the job chargeable</b>, and it carries <c>Approve</c> because it is
/// the moment the shop tells a customer that a repair they believed was free is not. The job carries on;
/// only the free ride ends. Parts and labour booked to it from here are billed as normal.
/// </summary>
[RequiresPermission(FeatureCatalog.Warranties, PermissionAction.Approve)]
public record RejectWarrantyClaimCommand(Guid WarrantyClaimId, string Outcome) : IRequest<Unit>;

public class RejectWarrantyClaimCommandValidator : AbstractValidator<RejectWarrantyClaimCommand>
{
    public RejectWarrantyClaimCommandValidator()
    {
        RuleFor(x => x.WarrantyClaimId).NotEmpty();

        RuleFor(x => x.Outcome)
            .NotEmpty()
            .WithMessage(
                "Rejecting a claim needs a reason. The customer is about to be charged for a repair they "
                + "believed was free, and 'because we said so' is how that becomes a dispute.")
            .MaximumLength(1000);
    }
}

public class RejectWarrantyClaimCommandHandler : IRequestHandler<RejectWarrantyClaimCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public RejectWarrantyClaimCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(RejectWarrantyClaimCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var claim = await _db.WarrantyClaims
            .Include(c => c.RepairTicket).ThenInclude(t => t!.Parts)
            .FirstOrDefaultAsync(c => c.Id == request.WarrantyClaimId, cancellationToken)
            ?? throw new NotFoundException("Warranty claim", request.WarrantyClaimId);

        claim.Reject(_clock.UtcNow, request.Outcome);

        if (claim.RepairTicket is { } ticket)
        {
            // The job is now the customer's to pay for. Everything already fitted to it was booked
            // non-chargeable on the strength of a warranty that has just been refused, so it would
            // otherwise be given away — the shop would eat the parts *and* lose the argument.
            ticket.WarrantyType = RepairWarrantyType.None;

            foreach (var part in ticket.Parts.Where(p => !p.IsReturned && !p.IsChargeable))
            {
                part.IsChargeable = true;
            }

            var labour = await _db.RepairLabour
                .Where(l => l.RepairTicketId == ticket.Id && !l.IsChargeable)
                .ToListAsync(cancellationToken);

            foreach (var hours in labour)
            {
                hours.IsChargeable = true;
            }

            // A part made chargeable has no price on it: it was fitted to a job nobody was going to bill,
            // so nobody priced it. Refusing here beats billing the customer zero for a screen.
            if (ticket.Parts.Any(p => p is { IsReturned: false, IsChargeable: true, UnitPrice: 0m }))
            {
                throw new DomainException(
                    "Parts already fitted to this job have no price on them — they were consumed as warranty "
                    + "work. Re-price them before billing, or the customer is charged nothing for them.");
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// Honour the claim. Usually implicit — delivering a warranty job accepts its claim — but recorded
/// explicitly when the shop settles one without the machine coming back.
/// </summary>
[RequiresPermission(FeatureCatalog.Warranties, PermissionAction.Approve)]
public record AcceptWarrantyClaimCommand(Guid WarrantyClaimId, string? Outcome = null) : IRequest<Unit>;

public class AcceptWarrantyClaimCommandHandler : IRequestHandler<AcceptWarrantyClaimCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public AcceptWarrantyClaimCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(AcceptWarrantyClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await _db.WarrantyClaims
            .FirstOrDefaultAsync(c => c.Id == request.WarrantyClaimId, cancellationToken)
            ?? throw new NotFoundException("Warranty claim", request.WarrantyClaimId);

        claim.Accept(_clock.UtcNow, request.Outcome);

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
