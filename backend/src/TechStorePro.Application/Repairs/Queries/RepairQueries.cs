using MediatR;
using Microsoft.EntityFrameworkCore;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Application.Repairs.Queries;

public record RepairPartDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid WarehouseId,
    decimal Quantity,
    decimal UnitCost,
    decimal UnitPrice,
    bool IsChargeable,
    bool IsReturned,
    decimal CostTotal,
    decimal ChargeTotal,
    DateTimeOffset ConsumedAt,
    string? Notes);

public record RepairLabourDto(
    Guid Id,
    Guid? TechnicianId,
    string Description,
    decimal Hours,
    decimal HourlyRate,
    bool IsChargeable,
    decimal ChargeTotal,
    DateTimeOffset WorkedAt);

public record RepairDiagnosisDto(
    Guid Id,
    Guid? TechnicianId,
    string Findings,
    string? RecommendedAction,
    decimal? EstimatedCost,
    DateTimeOffset DiagnosedAt);

public record RepairOutsourcingDto(
    Guid Id,
    Guid VendorSupplierId,
    string VendorName,
    OutsourcingStatus Status,
    DateTimeOffset SentAt,
    DateTimeOffset? ExpectedAt,
    DateTimeOffset? ReceivedAt,
    decimal Cost,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal CostInBaseCurrency,
    string? Notes);

public record RepairStatusChangeDto(
    RepairTicketStatus FromStatus,
    RepairTicketStatus ToStatus,
    Guid? ChangedBy,
    DateTimeOffset ChangedAt,
    string? Notes);

public record RepairTicketDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid BranchId,
    Guid? DeviceProductId,
    string? DeviceProductName,
    string? DeviceSerialNumber,
    string ReportedFault,
    string? Accessories,
    string? ConditionNotes,
    RepairTicketStatus Status,
    RepairWarrantyType WarrantyType,
    bool IsWarranty,
    Guid? WarrantyInvoiceLineId,
    decimal? EstimatedCost,
    DateTimeOffset? ApprovedAt,
    Guid? TechnicianId,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? PromisedAt,
    DateTimeOffset? DeliveredAt,

    // The money (§35, "repair profitability"). Every figure here is derived from the lines below it, so a
    // screen cannot show a total that disagrees with what it is a total of.
    decimal PartsCost,
    decimal OutsourcingCost,
    decimal TotalCost,
    decimal ChargeableTotal,
    decimal GrossProfit,

    /// <summary>The invoice raised for this job, if it has been billed. A warranty job never has one.</summary>
    Guid? SalesInvoiceId,

    string? Notes,
    string? CancelledReason,
    IReadOnlyCollection<RepairPartDto> Parts,
    IReadOnlyCollection<RepairLabourDto> Labour,
    IReadOnlyCollection<RepairDiagnosisDto> Diagnoses,
    IReadOnlyCollection<RepairOutsourcingDto> Outsourcings,
    IReadOnlyCollection<RepairStatusChangeDto> StatusHistory);

/// <summary>
/// The list and the detail share one loader, so a figure cannot be right on one screen and wrong on the
/// other. Materialised and then mapped in C# rather than projected in SQL, because the money properties are
/// computed and EF has been told to Ignore them.
/// </summary>
internal static class RepairTicketMapping
{
    public static IQueryable<RepairTicket> WithGraph(IQueryable<RepairTicket> query) =>
        query
            .Include(t => t.Customer)
            .Include(t => t.DeviceProduct)
            .Include(t => t.Parts).ThenInclude(p => p.Product)
            .Include(t => t.Labour)
            .Include(t => t.Diagnoses)
            .Include(t => t.Outsourcings).ThenInclude(o => o.VendorSupplier)
            .Include(t => t.StatusHistory)
            .Include(t => t.Charges);

    public static RepairTicketDto ToDto(RepairTicket t) => new(
        t.Id,
        t.Number,
        t.CustomerId,
        t.Customer.Name,
        t.BranchId,
        t.DeviceProductId,
        t.DeviceProduct?.Name,
        t.DeviceSerialNumber,
        t.ReportedFault,
        t.Accessories,
        t.ConditionNotes,
        t.Status,
        t.WarrantyType,
        t.IsWarranty,
        t.WarrantyInvoiceLineId,
        t.EstimatedCost,
        t.ApprovedAt,
        t.TechnicianId,
        t.ReceivedAt,
        t.PromisedAt,
        t.DeliveredAt,
        t.PartsCost,
        t.OutsourcingCost,
        t.TotalCost,
        t.ChargeableTotal,
        t.GrossProfit,
        t.Charges.FirstOrDefault()?.SalesInvoiceId,
        t.Notes,
        t.CancelledReason,
        t.Parts
            .OrderBy(p => p.ConsumedAt)
            .Select(p => new RepairPartDto(
                p.Id, p.ProductId, p.Product.Name, p.WarehouseId, p.Quantity, p.UnitCost, p.UnitPrice,
                p.IsChargeable, p.IsReturned, p.CostTotal, p.ChargeTotal, p.ConsumedAt, p.Notes))
            .ToList(),
        t.Labour
            .OrderBy(l => l.WorkedAt)
            .Select(l => new RepairLabourDto(
                l.Id, l.TechnicianId, l.Description, l.Hours, l.HourlyRate, l.IsChargeable, l.ChargeTotal,
                l.WorkedAt))
            .ToList(),
        t.Diagnoses
            .OrderBy(d => d.DiagnosedAt)
            .Select(d => new RepairDiagnosisDto(
                d.Id, d.TechnicianId, d.Findings, d.RecommendedAction, d.EstimatedCost, d.DiagnosedAt))
            .ToList(),
        t.Outsourcings
            .OrderBy(o => o.SentAt)
            .Select(o => new RepairOutsourcingDto(
                o.Id, o.VendorSupplierId, o.VendorSupplier.Name, o.Status, o.SentAt, o.ExpectedAt,
                o.ReceivedAt, o.Cost, o.CurrencyCode, o.ExchangeRate, o.CostInBaseCurrency, o.Notes))
            .ToList(),
        t.StatusHistory
            .OrderBy(h => h.ChangedAt)
            .Select(h => new RepairStatusChangeDto(
                h.FromStatus, h.ToStatus, h.ChangedBy, h.ChangedAt, h.Notes))
            .ToList());
}

[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.View)]
public record GetRepairTicketsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    RepairTicketStatus? Status = null,
    Guid? CustomerId = null,
    Guid? TechnicianId = null,

    /// <summary>
    /// The pending-repairs report (§35): everything not yet delivered or cancelled. It is a filter rather
    /// than a separate endpoint because "what is on the bench right now" is the same list the workshop
    /// works from all day, just sorted differently.
    /// </summary>
    bool OpenOnly = false) : IRequest<PagedResult<RepairTicketDto>>;

public class GetRepairTicketsQueryHandler
    : IRequestHandler<GetRepairTicketsQuery, PagedResult<RepairTicketDto>>
{
    private readonly IApplicationDbContext _db;

    public GetRepairTicketsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<RepairTicketDto>> Handle(
        GetRepairTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.RepairTickets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();

            query = query.Where(t =>
                t.Number.ToLower().Contains(term)
                || t.Customer.Name.ToLower().Contains(term)
                || (t.DeviceSerialNumber != null && t.DeviceSerialNumber.ToLower().Contains(term))
                || t.ReportedFault.ToLower().Contains(term));
        }

        if (request.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(t => t.CustomerId == customerId);
        }

        if (request.TechnicianId is { } technicianId)
        {
            query = query.Where(t => t.TechnicianId == technicianId);
        }

        if (request.OpenOnly)
        {
            query = query.Where(t =>
                t.Status != RepairTicketStatus.Delivered && t.Status != RepairTicketStatus.Cancelled);
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var tickets = await RepairTicketMapping
            .WithGraph(query)

            // Promised-first for the open list: the job that is late is the one the shop needs to see. A
            // ticket with no promised date sorts last rather than first, which is what NULLS LAST buys.
            .OrderBy(t => request.OpenOnly && t.PromisedAt != null ? 0 : 1)
            .ThenBy(t => request.OpenOnly ? t.PromisedAt : null)
            .ThenByDescending(t => t.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = tickets.Select(RepairTicketMapping.ToDto).ToList();

        return new PagedResult<RepairTicketDto>(items, total, page, pageSize);
    }
}

[RequiresPermission(FeatureCatalog.RepairTickets, PermissionAction.View)]
public record GetRepairTicketByIdQuery(Guid Id) : IRequest<RepairTicketDto>;

public class GetRepairTicketByIdQueryHandler : IRequestHandler<GetRepairTicketByIdQuery, RepairTicketDto>
{
    private readonly IApplicationDbContext _db;

    public GetRepairTicketByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<RepairTicketDto> Handle(
        GetRepairTicketByIdQuery request,
        CancellationToken cancellationToken)
    {
        var ticket = await RepairTicketMapping
            .WithGraph(_db.RepairTickets.AsNoTracking())
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Repair ticket", request.Id);

        return RepairTicketMapping.ToDto(ticket);
    }
}

// --- Warranties ------------------------------------------------------------------------------------

public record WarrantyDto(
    Guid Id,
    RepairWarrantyType WarrantyType,
    WarrantySourceType SourceType,
    Guid ProductId,
    string ProductName,
    string? SerialNumber,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string? Terms,
    int OpenClaims);

[RequiresPermission(FeatureCatalog.Warranties, PermissionAction.View)]
public record GetWarrantiesQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null) : IRequest<PagedResult<WarrantyDto>>;

public class GetWarrantiesQueryHandler : IRequestHandler<GetWarrantiesQuery, PagedResult<WarrantyDto>>
{
    private readonly IApplicationDbContext _db;

    public GetWarrantiesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<WarrantyDto>> Handle(
        GetWarrantiesQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.Warranties.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();

            query = query.Where(w =>
                (w.SerialNumber != null && w.SerialNumber.ToLower().Contains(term))
                || w.Product.Name.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .Include(w => w.Product)
            .Include(w => w.Claims)
            .OrderByDescending(w => w.EndsOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new WarrantyDto(
                w.Id,
                w.WarrantyType,
                w.SourceType,
                w.ProductId,
                w.Product.Name,
                w.SerialNumber,
                w.StartsOn,
                w.EndsOn,
                w.Terms,
                w.Claims.Count(c => c.Status == WarrantyClaimStatus.Open)))
            .ToListAsync(cancellationToken);

        return new PagedResult<WarrantyDto>(items, total, page, pageSize);
    }
}

public record WarrantyClaimDto(
    Guid Id,
    Guid WarrantyId,
    RepairWarrantyType WarrantyType,
    string ProductName,
    string? SerialNumber,
    Guid? RepairTicketId,
    string? RepairTicketNumber,
    WarrantyClaimStatus Status,
    DateTimeOffset ClaimedAt,
    DateTimeOffset? ResolvedAt,
    string? Outcome,

    /// <summary>
    /// What honouring this claim cost the shop — the parts and the vendor off the job it authorised.
    /// <b>This is the number §30 exists to produce</b>: "which products keep coming back?" is unanswerable
    /// without it, and a warranty provision nobody measures is a warranty provision nobody prices.
    /// </summary>
    decimal CostToShop);

[RequiresPermission(FeatureCatalog.Warranties, PermissionAction.View)]
public record GetWarrantyClaimsQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    WarrantyClaimStatus? Status = null) : IRequest<PagedResult<WarrantyClaimDto>>;

public class GetWarrantyClaimsQueryHandler
    : IRequestHandler<GetWarrantyClaimsQuery, PagedResult<WarrantyClaimDto>>
{
    private readonly IApplicationDbContext _db;

    public GetWarrantyClaimsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<WarrantyClaimDto>> Handle(
        GetWarrantyClaimsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _db.WarrantyClaims.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();

            query = query.Where(c =>
                (c.Warranty.SerialNumber != null && c.Warranty.SerialNumber.ToLower().Contains(term))
                || c.Warranty.Product.Name.ToLower().Contains(term)
                || (c.RepairTicket != null && c.RepairTicket.Number.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var claims = await query
            .Include(c => c.Warranty).ThenInclude(w => w.Product)
            .Include(c => c.RepairTicket!).ThenInclude(t => t.Parts)
            .Include(c => c.RepairTicket!).ThenInclude(t => t.Outsourcings)
            .OrderByDescending(c => c.ClaimedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = claims
            .Select(c => new WarrantyClaimDto(
                c.Id,
                c.WarrantyId,
                c.Warranty.WarrantyType,
                c.Warranty.Product.Name,
                c.Warranty.SerialNumber,
                c.RepairTicketId,
                c.RepairTicket?.Number,
                c.Status,
                c.ClaimedAt,
                c.ResolvedAt,
                c.Outcome,
                c.RepairTicket?.TotalCost ?? 0m))
            .ToList();

        return new PagedResult<WarrantyClaimDto>(items, total, page, pageSize);
    }
}
