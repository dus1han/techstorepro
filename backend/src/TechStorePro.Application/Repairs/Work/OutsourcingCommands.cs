using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Application.Repairs.Work;

/// <summary>
/// Send the job out to a third party (requirements §29) — a board-level repair the shop cannot do itself.
///
/// <b>No stock moves.</b> The device belongs to the customer; sending it to a vendor does not make it the
/// vendor's, and it was never the shop's to move. What this records is a <em>cost</em> — and it is a real
/// one, so it lands on the ticket and comes straight off its margin, even on a warranty job where the
/// customer pays nothing.
/// </summary>
[RequiresPermission(FeatureCatalog.Outsourcing, PermissionAction.Create)]
public record SendToVendorCommand(
    Guid RepairTicketId,
    Guid VendorSupplierId,
    decimal? EstimatedCost = null,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    DateTimeOffset? SentAt = null,
    DateTimeOffset? ExpectedAt = null,
    string? Notes = null) : IRequest<Guid>;

public class SendToVendorCommandValidator : AbstractValidator<SendToVendorCommand>
{
    public SendToVendorCommandValidator()
    {
        RuleFor(x => x.RepairTicketId).NotEmpty();
        RuleFor(x => x.VendorSupplierId).NotEmpty();
        RuleFor(x => x.EstimatedCost).GreaterThanOrEqualTo(0).When(x => x.EstimatedCost.HasValue);
        RuleFor(x => x.CurrencyCode).Length(3).When(x => x.CurrencyCode is not null);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).When(x => x.ExchangeRate.HasValue);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class SendToVendorCommandHandler : IRequestHandler<SendToVendorCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDateTime _clock;

    public SendToVendorCommandHandler(IApplicationDbContext db, ITenantContext tenant, IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<Guid> Handle(SendToVendorCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _db.RepairTickets
            .FirstOrDefaultAsync(t => t.Id == request.RepairTicketId, cancellationToken)
            ?? throw new NotFoundException("Repair ticket", request.RepairTicketId);

        // Sending a machine out is work on it, so it is gated exactly as fitting a part is: the customer
        // has not agreed to pay a vendor they have not been told about.
        ticket.EnsureWorkAllowed();

        _ = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.VendorSupplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier", request.VendorSupplierId);

        var baseCurrency = await _db.Companies
            .AsNoTracking()
            .Where(c => c.Id == _tenant.CompanyId)
            .Select(c => c.BaseCurrency)
            .FirstAsync(cancellationToken);

        var currency = request.CurrencyCode?.ToUpperInvariant() ?? baseCurrency;

        var outsourcing = new RepairOutsourcing
        {
            RepairTicketId = ticket.Id,
            VendorSupplierId = request.VendorSupplierId,
            Status = OutsourcingStatus.Sent,
            SentAt = request.SentAt ?? _clock.UtcNow,
            ExpectedAt = request.ExpectedAt,
            Cost = request.EstimatedCost ?? 0m,
            CurrencyCode = currency,

            // A repair vendor abroad bills in dollars; the margin on the job has to be in the money the
            // shop keeps its books in. Snapshotted, like every other rate in the system.
            ExchangeRate = currency == baseCurrency ? 1m : request.ExchangeRate ?? 1m,
            Notes = request.Notes
        };

        _db.RepairOutsourcings.Add(outsourcing);

        await _db.SaveChangesAsync(cancellationToken);

        return outsourcing.Id;
    }
}

/// <summary>
/// The vendor has done the work and sent the machine back, with a bill. That bill is the cost of this job.
/// </summary>
[RequiresPermission(FeatureCatalog.Outsourcing, PermissionAction.Edit)]
public record ReceiveFromVendorCommand(
    Guid OutsourcingId,
    decimal Cost,
    DateTimeOffset? ReceivedAt = null,
    string? Notes = null) : IRequest<Unit>;

public class ReceiveFromVendorCommandValidator : AbstractValidator<ReceiveFromVendorCommand>
{
    public ReceiveFromVendorCommandValidator()
    {
        RuleFor(x => x.OutsourcingId).NotEmpty();
        RuleFor(x => x.Cost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class ReceiveFromVendorCommandHandler : IRequestHandler<ReceiveFromVendorCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public ReceiveFromVendorCommandHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(ReceiveFromVendorCommand request, CancellationToken cancellationToken)
    {
        var outsourcing = await _db.RepairOutsourcings
            .FirstOrDefaultAsync(o => o.Id == request.OutsourcingId, cancellationToken)
            ?? throw new NotFoundException("Outsourced repair", request.OutsourcingId);

        outsourcing.Receive(request.Cost, request.ReceivedAt ?? _clock.UtcNow);

        if (request.Notes is not null)
        {
            outsourcing.Notes = request.Notes;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Recall a job from a vendor who has not done it. Refused once they have charged for it.</summary>
[RequiresPermission(FeatureCatalog.Outsourcing, PermissionAction.Delete)]
public record CancelOutsourcingCommand(Guid OutsourcingId) : IRequest<Unit>;

public class CancelOutsourcingCommandHandler : IRequestHandler<CancelOutsourcingCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public CancelOutsourcingCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(CancelOutsourcingCommand request, CancellationToken cancellationToken)
    {
        var outsourcing = await _db.RepairOutsourcings
            .FirstOrDefaultAsync(o => o.Id == request.OutsourcingId, cancellationToken)
            ?? throw new NotFoundException("Outsourced repair", request.OutsourcingId);

        outsourcing.Cancel();

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
