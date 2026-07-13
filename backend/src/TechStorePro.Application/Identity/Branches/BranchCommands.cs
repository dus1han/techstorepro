using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Branches;

public record BranchDto(
    Guid Id,
    string Name,
    string Code,
    string? Address,
    string? Phone,
    string? Email,
    Guid? DefaultWarehouseId,
    string? DefaultWarehouseName,
    bool IsDefault,
    bool IsActive);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Branches, PermissionAction.View)]
public record GetBranchesQuery(int Page = 1, int PageSize = 25, string? Search = null)
    : IRequest<PagedResult<BranchDto>>;

public class GetBranchesQueryHandler : IRequestHandler<GetBranchesQuery, PagedResult<BranchDto>>
{
    private readonly IApplicationDbContext _db;

    public GetBranchesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<BranchDto>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        // No WHERE company_id here, and none is needed: the DbContext's global filter has already
        // applied it. That is the point of enforcing tenancy centrally — this handler cannot leak
        // another company's branches even if its author never thinks about tenancy at all.
        var query = _db.Branches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Deliberately not EF.Functions.ILike: that is Npgsql-specific, and the Application layer
            // must not depend on the database provider. ToLower().Contains() translates to the same
            // case-insensitive LIKE on Postgres and still works against any other provider — which is
            // what lets the domain tests run without a real database.
            var term = request.Search.Trim().ToLower();

            query = query.Where(b =>
                b.Name.ToLower().Contains(term) || b.Code.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        // Capped server-side: a client cannot ask for the whole table (api-design.md §4).
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var items = await query
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BranchDto(
                b.Id, b.Name, b.Code, b.Address, b.Phone, b.Email,
                b.DefaultWarehouseId,
                b.DefaultWarehouse != null ? b.DefaultWarehouse.Name : null,
                b.IsDefault, b.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<BranchDto>(items, total, page, pageSize);
    }
}

// --- Create -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Branches, PermissionAction.Create)]
public record CreateBranchCommand(
    string Name,
    string Code,
    string? Address,
    string? Phone,
    string? Email) : IRequest<Guid>;

public class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public class CreateBranchCommandHandler : IRequestHandler<CreateBranchCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateBranchCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _db.Branches.AnyAsync(b => b.Code == code, cancellationToken))
        {
            throw new ConflictException($"A branch with code '{code}' already exists.");
        }

        var branch = new Branch
        {
            Name = request.Name.Trim(),
            Code = code,
            Address = request.Address,
            Phone = request.Phone,
            Email = request.Email,
            IsActive = true,
            IsDefault = false
        };

        // CompanyId is stamped by the DbContext on insert. Setting it here would be redundant, and
        // trusting a client-supplied one would be a tenancy hole.
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(cancellationToken);

        return branch.Id;
    }
}

// --- Update -------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Branches, PermissionAction.Edit)]
public record UpdateBranchCommand(
    Guid Id,
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    Guid? DefaultWarehouseId,
    bool IsActive) : IRequest;

public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand>
{
    private readonly IApplicationDbContext _db;

    public UpdateBranchCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Branch", request.Id);

        if (request.DefaultWarehouseId is { } warehouseId)
        {
            var warehouse = await _db.Warehouses
                .FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken)
                ?? throw new NotFoundException("Warehouse", warehouseId);

            // A branch may default to a warehouse it owns, or to a company-shared one it has been
            // granted access to — never to another branch's private warehouse.
            var usable = warehouse.BranchId is null || warehouse.BranchId == branch.Id;

            if (!usable)
            {
                throw new DomainException(
                    $"Warehouse '{warehouse.Name}' belongs to another branch and cannot be this branch's default.");
            }

            branch.DefaultWarehouseId = warehouseId;
        }
        else
        {
            branch.DefaultWarehouseId = null;
        }

        branch.Name = request.Name.Trim();
        branch.Address = request.Address;
        branch.Phone = request.Phone;
        branch.Email = request.Email;
        branch.IsActive = request.IsActive;

        // The audit row — old and new values — is captured by the DbContext from the change tracker.
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// --- Delete (soft, with a reason) ---------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Branches, PermissionAction.Delete)]
public record DeleteBranchCommand(Guid Id, string Reason) : IRequest;

public class DeleteBranchCommandValidator : AbstractValidator<DeleteBranchCommand>
{
    public DeleteBranchCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        // Requirements §10 asks for a delete reason, so it is mandatory rather than optional. An
        // optional reason is an empty reason.
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when deleting a branch.")
            .MaximumLength(500);
    }
}

public class DeleteBranchCommandHandler : IRequestHandler<DeleteBranchCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteBranchCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeleteBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Branch", request.Id);

        if (branch.IsDefault)
        {
            throw new DomainException(
                "The default branch cannot be deleted. Make another branch the default first.");
        }

        // Set the reason before Remove(): the DbContext rewrites the delete into an update, and the
        // reason has to be on the entity by then to survive.
        branch.DeletedReason = request.Reason.Trim();

        _db.Branches.Remove(branch);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// --- Restore ------------------------------------------------------------------------------------

/// <summary>Requirements §10: a retired record can be brought back.</summary>
[RequiresPermission(FeatureCatalog.Branches, PermissionAction.Delete)]
public record RestoreBranchCommand(Guid Id) : IRequest;

public class RestoreBranchCommandHandler : IRequestHandler<RestoreBranchCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public RestoreBranchCommandHandler(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task Handle(RestoreBranchCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenant.CompanyId
            ?? throw new UnauthorizedAccessException("Authentication is required.");

        // A deleted branch is invisible to a normal query by definition — seeing past the
        // soft-delete filter is the whole job here. But IgnoreQueryFilters drops the *tenant* filter
        // with it, so the company check must be written by hand. Without it, any company could
        // restore any other company's branch by guessing an id.
        var branch = await _db.IgnoringTenantFilter<Branch>()
            .FirstOrDefaultAsync(
                b => b.Id == request.Id && b.IsDeleted && b.CompanyId == companyId,
                cancellationToken)
            ?? throw new NotFoundException("Branch", request.Id);

        branch.IsDeleted = false;
        branch.DeletedAt = null;
        branch.DeletedBy = null;
        branch.DeletedReason = null;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
