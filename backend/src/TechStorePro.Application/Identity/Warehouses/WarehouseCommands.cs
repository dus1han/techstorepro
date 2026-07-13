using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Warehouses;

public record WarehouseDto(
    Guid Id,
    string Name,
    string Code,
    WarehouseType Type,
    Guid? BranchId,
    string? BranchName,
    bool IsShared,
    IReadOnlyCollection<WarehouseAccessDto> SharedWith,
    bool IsActive);

public record WarehouseAccessDto(Guid BranchId, string BranchName, bool CanIssue, bool CanReceive);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Warehouses, PermissionAction.View)]
public record GetWarehousesQuery : IRequest<IReadOnlyCollection<WarehouseDto>>;

public class GetWarehousesQueryHandler
    : IRequestHandler<GetWarehousesQuery, IReadOnlyCollection<WarehouseDto>>
{
    private readonly IApplicationDbContext _db;

    public GetWarehousesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<WarehouseDto>> Handle(
        GetWarehousesQuery request,
        CancellationToken cancellationToken)
    {
        var warehouses = await _db.Warehouses
            .AsNoTracking()
            .Include(w => w.Branch)
            .Include(w => w.AccessibleToBranches).ThenInclude(a => a.Branch)
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);

        return warehouses
            .Select(w => new WarehouseDto(
                w.Id,
                w.Name,
                w.Code,
                w.Type,
                w.BranchId,
                w.Branch?.Name,
                w.IsShared,
                w.AccessibleToBranches
                    .Select(a => new WarehouseAccessDto(a.BranchId, a.Branch.Name, a.CanIssue, a.CanReceive))
                    .ToList(),
                w.IsActive))
            .ToList();
    }
}

// --- Create -------------------------------------------------------------------------------------

/// <summary>
/// Creates a warehouse. <paramref name="BranchId"/> null means company-shared: usable by the
/// branches named in <paramref name="SharedWith"/>, and by nobody else.
/// </summary>
[RequiresPermission(FeatureCatalog.Warehouses, PermissionAction.Create)]
public record CreateWarehouseCommand(
    string Name,
    string Code,
    WarehouseType Type,
    Guid? BranchId,
    IReadOnlyCollection<WarehouseAccessGrant>? SharedWith = null) : IRequest<Guid>;

public record WarehouseAccessGrant(Guid BranchId, bool CanIssue = true, bool CanReceive = true);

public class CreateWarehouseCommandValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);

        // Access grants only mean something for a shared warehouse. Accepting them on a
        // branch-owned one would store rules that are never consulted, and quietly imply an
        // isolation guarantee that does not exist.
        RuleFor(x => x)
            .Must(x => x.BranchId is null || x.SharedWith is null or { Count: 0 })
            .WithMessage("A branch-owned warehouse cannot also be shared with other branches. "
                         + "Leave branchId null to create a shared warehouse.");
    }
}

public class CreateWarehouseCommandHandler : IRequestHandler<CreateWarehouseCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateWarehouseCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> Handle(CreateWarehouseCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _db.Warehouses.AnyAsync(w => w.Code == code, cancellationToken))
        {
            throw new ConflictException($"A warehouse with code '{code}' already exists.");
        }

        if (request.BranchId is { } ownerId
            && !await _db.Branches.AnyAsync(b => b.Id == ownerId, cancellationToken))
        {
            throw new NotFoundException("Branch", ownerId);
        }

        var warehouse = new Warehouse
        {
            Name = request.Name.Trim(),
            Code = code,
            Type = request.Type,
            BranchId = request.BranchId,
            IsActive = true
        };

        _db.Warehouses.Add(warehouse);

        foreach (var grant in request.SharedWith ?? [])
        {
            if (!await _db.Branches.AnyAsync(b => b.Id == grant.BranchId, cancellationToken))
            {
                throw new NotFoundException("Branch", grant.BranchId);
            }

            _db.BranchWarehouses.Add(new BranchWarehouse
            {
                BranchId = grant.BranchId,
                WarehouseId = warehouse.Id,
                CanIssue = grant.CanIssue,
                CanReceive = grant.CanReceive
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return warehouse.Id;
    }
}

// --- Share with a branch ------------------------------------------------------------------------

/// <summary>
/// Grants (or revokes) one branch's access to a company-shared warehouse. This is the decision that
/// makes "shared" mean something narrower than "everyone can drain it".
/// </summary>
[RequiresPermission(FeatureCatalog.Warehouses, PermissionAction.Edit)]
public record SetWarehouseAccessCommand(
    Guid WarehouseId,
    IReadOnlyCollection<WarehouseAccessGrant> Branches) : IRequest;

public class SetWarehouseAccessCommandHandler : IRequestHandler<SetWarehouseAccessCommand>
{
    private readonly IApplicationDbContext _db;

    public SetWarehouseAccessCommandHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task Handle(SetWarehouseAccessCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _db.Warehouses
            .Include(w => w.AccessibleToBranches)
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId, cancellationToken)
            ?? throw new NotFoundException("Warehouse", request.WarehouseId);

        if (!warehouse.IsShared)
        {
            throw new DomainException(
                $"Warehouse '{warehouse.Name}' is owned by a branch. Only a company-shared warehouse "
                + "(one with no owning branch) can be shared with other branches.");
        }

        var wanted = request.Branches.ToDictionary(g => g.BranchId);

        foreach (var existing in warehouse.AccessibleToBranches.ToList())
        {
            if (wanted.TryGetValue(existing.BranchId, out var grant))
            {
                existing.CanIssue = grant.CanIssue;
                existing.CanReceive = grant.CanReceive;
                wanted.Remove(existing.BranchId);
                continue;
            }

            _db.BranchWarehouses.Remove(existing);
        }

        foreach (var grant in wanted.Values)
        {
            if (!await _db.Branches.AnyAsync(b => b.Id == grant.BranchId, cancellationToken))
            {
                throw new NotFoundException("Branch", grant.BranchId);
            }

            _db.BranchWarehouses.Add(new BranchWarehouse
            {
                BranchId = grant.BranchId,
                WarehouseId = warehouse.Id,
                CanIssue = grant.CanIssue,
                CanReceive = grant.CanReceive
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
