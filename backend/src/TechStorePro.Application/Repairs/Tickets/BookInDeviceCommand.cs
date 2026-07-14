using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Repairs.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Application.Repairs.Tickets;

/// <summary>
/// A device comes in over the counter (requirements §28) — the job sheet.
///
/// <b>No stock moves, and that is the whole point.</b> The laptop on the bench belongs to the customer.
/// The shop does not own it, cannot sell it, and must not value it, so intake writes no stock movement and
/// no balance changes. (The one thing that happens to inventory is that a serial the shop <em>sold</em> is
/// marked <see cref="SerialStatus.InRepair"/> — a note about where the unit is, not a claim that the shop
/// owns it again.)
///
/// The interesting work here is the <b>warranty question</b>, and it is answered automatically rather than
/// left to the clerk. The alternative is a tickbox on a form, and a tickbox is exactly how a shop ends up
/// billing a customer for a repair it had already promised to do for free.
/// </summary>
[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.Create)]
public record BookInDeviceCommand(
    Guid CustomerId,
    Guid BranchId,
    string ReportedFault,
    Guid? DeviceProductId = null,
    string? DeviceSerialNumber = null,
    string? Accessories = null,
    string? ConditionNotes = null,
    Guid? TechnicianId = null,
    DateTimeOffset? ReceivedAt = null,
    DateTimeOffset? PromisedAt = null,
    string? Notes = null) : IRequest<Guid>;

public class BookInDeviceCommandValidator : AbstractValidator<BookInDeviceCommand>
{
    public BookInDeviceCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();

        RuleFor(x => x.ReportedFault)
            .NotEmpty().WithMessage("Write down what the customer says is wrong with it.")
            .MaximumLength(2000);

        RuleFor(x => x.DeviceSerialNumber).MaximumLength(100);
        RuleFor(x => x.Accessories).MaximumLength(1000);
        RuleFor(x => x.ConditionNotes).MaximumLength(1000);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class BookInDeviceCommandHandler : IRequestHandler<BookInDeviceCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IWarrantyLookup _warranties;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IDateTime _clock;

    public BookInDeviceCommandHandler(
        IApplicationDbContext db,
        IWarrantyLookup warranties,
        IDocumentNumberGenerator numbers,
        IDateTime clock)
    {
        _db = db;
        _warranties = warranties;
        _numbers = numbers;
        _clock = clock;
    }

    public async Task<Guid> Handle(BookInDeviceCommand request, CancellationToken cancellationToken)
    {
        // The document number is taken under a row lock, so it needs the transaction even though no stock
        // moves here. A rollback returns the number rather than burning it (§5).
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var receivedAt = request.ReceivedAt ?? _clock.UtcNow;

        _ = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        // Is anyone else paying for this? Asked once, at the door, and stamped onto the job — because the
        // answer decides whether the customer ever sees an estimate.
        var cover = await _warranties.FindAsync(
            request.DeviceSerialNumber,
            request.DeviceProductId,
            receivedAt,
            cancellationToken);

        var ticket = new RepairTicket
        {
            Number = await _numbers.NextAsync(DocumentType.RepairTicket, request.BranchId, cancellationToken),
            CustomerId = request.CustomerId,
            BranchId = request.BranchId,

            // The lookup found the unit, so it knows the product better than the caller does.
            DeviceProductId = request.DeviceProductId ?? cover.ProductId,
            DeviceSerialNumber = request.DeviceSerialNumber?.Trim(),
            DeviceSerialId = cover.SerialId,

            ReportedFault = request.ReportedFault,
            Accessories = request.Accessories,
            ConditionNotes = request.ConditionNotes,

            Status = RepairTicketStatus.Received,
            WarrantyType = cover.WarrantyType,
            WarrantyInvoiceLineId = cover.SoldInvoiceLineId,

            TechnicianId = request.TechnicianId,
            ReceivedAt = receivedAt,
            PromisedAt = request.PromisedAt,
            Notes = request.Notes
        };

        _db.RepairTickets.Add(ticket);

        // The trail starts here, so that a job's history has a beginning and not merely a middle.
        _db.RepairStatusChanges.Add(new RepairStatusChange
        {
            RepairTicketId = ticket.Id,
            FromStatus = RepairTicketStatus.Received,
            ToStatus = RepairTicketStatus.Received,
            ChangedAt = receivedAt,
            Notes = $"Booked in. {cover.Explanation}"
        });

        // A warranty being relied on gets a claim raised against it, open until the job proves it out.
        // Without this the shop would eat the cost of a free repair with nothing recording that it did,
        // and could never answer the question its margin depends on: which products keep coming back?
        if (cover is { IsCovered: true, WarrantyId: { } warrantyId })
        {
            _db.WarrantyClaims.Add(new WarrantyClaim
            {
                WarrantyId = warrantyId,
                RepairTicketId = ticket.Id,
                Status = WarrantyClaimStatus.Open,
                ClaimedAt = receivedAt,
                Notes = request.ReportedFault
            });
        }

        // The unit is in the workshop. It is still the customer's — this says where it is, not who owns it,
        // and Serial.MoveTo refuses any transition that would put a sold machine back on the shelf.
        if (cover.SerialId is { } serialId)
        {
            var serial = await _db.Serials.FirstAsync(s => s.Id == serialId, cancellationToken);

            if (serial.Status == SerialStatus.Sold)
            {
                serial.Status = SerialStatus.InRepair;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ticket.Id;
    }
}

/// <summary>
/// "Is this machine still under warranty?" — asked from the counter before anything is booked in, because
/// the customer is standing there and wants to know.
///
/// It is a query rather than a side effect of intake so that the front desk can answer without creating a
/// job sheet for a machine the customer may decide to take home again.
/// </summary>
[RequiresPermission(FeatureCatalog.Warranties, PermissionAction.View)]
public record CheckWarrantyQuery(string SerialNumber) : IRequest<WarrantyCover>;

public class CheckWarrantyQueryHandler : IRequestHandler<CheckWarrantyQuery, WarrantyCover>
{
    private readonly IWarrantyLookup _warranties;
    private readonly IDateTime _clock;

    public CheckWarrantyQueryHandler(IWarrantyLookup warranties, IDateTime clock)
    {
        _warranties = warranties;
        _clock = clock;
    }

    public Task<WarrantyCover> Handle(CheckWarrantyQuery request, CancellationToken cancellationToken) =>
        _warranties.FindAsync(request.SerialNumber, null, _clock.UtcNow, cancellationToken);
}
