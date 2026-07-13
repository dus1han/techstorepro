using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Users;

public record CompanyUserDto(
    Guid CompanyUserId,
    Guid UserId,
    string FullName,
    string Email,
    string? Phone,
    bool IsOwner,
    bool IsActive,
    int PermissionCount,
    IReadOnlyCollection<Guid> BranchIds);

// --- List ---------------------------------------------------------------------------------------

[RequiresPermission(FeatureCatalog.Users, PermissionAction.View)]
public record GetCompanyUsersQuery : IRequest<IReadOnlyCollection<CompanyUserDto>>;

public class GetCompanyUsersQueryHandler
    : IRequestHandler<GetCompanyUsersQuery, IReadOnlyCollection<CompanyUserDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCompanyUsersQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<CompanyUserDto>> Handle(
        GetCompanyUsersQuery request,
        CancellationToken cancellationToken)
    {
        var members = await _db.CompanyUsers
            .AsNoTracking()
            .Include(m => m.User)
            .Include(m => m.Permissions)
            .Include(m => m.BranchAccess)
            .OrderByDescending(m => m.IsOwner)
            .ThenBy(m => m.User.FullName)
            .ToListAsync(cancellationToken);

        return members
            .Select(m => new CompanyUserDto(
                m.Id,
                m.UserId,
                m.User.FullName,
                m.User.Email,
                m.User.Phone,
                m.IsOwner,
                m.IsActive && m.User.IsActive,
                // An owner's permissions are implicit, so a stored count of zero would read as
                // "this user can do nothing" — the exact opposite of the truth.
                m.IsOwner
                    ? FeatureCatalog.All.Sum(f => f.SupportedActions.Length)
                    : m.Permissions.Count(p => p.Granted),
                m.BranchAccess.Select(b => b.BranchId).ToList()))
            .ToList();
    }
}

// --- Invite -------------------------------------------------------------------------------------

/// <summary>
/// Adds a user to the company. If the email already belongs to a platform user — the same person
/// working for two companies — they are attached rather than duplicated, because
/// <see cref="User"/> is global by design.
///
/// New permissions start empty: a new member can do nothing until an admin grants it. Defaulting to
/// any starting set would be a role by another name, and requirements §7 forbids that.
/// </summary>
[RequiresPermission(FeatureCatalog.Users, PermissionAction.Create)]
public record InviteUserCommand(
    string Email,
    string FullName,
    string? Phone,
    string TemporaryPassword,
    IReadOnlyCollection<Guid>? BranchIds = null) : IRequest<Guid>;

public class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TemporaryPassword)
            .NotEmpty()
            .MinimumLength(10).WithMessage("Temporary password must be at least 10 characters.");
    }
}

public class InviteUserCommandHandler : IRequestHandler<InviteUserCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITenantContext _tenant;

    public InviteUserCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        ITenantContext tenant)
    {
        _db = db;
        _hasher = hasher;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenant.CompanyId
            ?? throw new UnauthorizedAccessException("Authentication is required.");

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _db.IgnoringTenantFilter<User>()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Email = email,
                FullName = request.FullName.Trim(),
                Phone = request.Phone,
                PasswordHash = _hasher.Hash(request.TemporaryPassword),
                IsActive = true,
                // They must pick their own password on first sign-in: an admin who knows a user's
                // working password can act as them, and the audit trail would show the user.
                MustChangePassword = true
            };

            _db.Users.Add(user);
        }
        else
        {
            var alreadyMember = await _db.IgnoringTenantFilter<CompanyUser>()
                .AnyAsync(m => m.UserId == user.Id && m.CompanyId == companyId, cancellationToken);

            if (alreadyMember)
            {
                throw new ConflictException($"{email} is already a member of this company.");
            }

            // An existing platform user keeps their own password. Resetting it here would let a
            // company admin change the credentials of a person who also works for a *different*
            // company — a cross-tenant account takeover dressed up as an invitation.
        }

        var membership = new CompanyUser
        {
            CompanyId = companyId,
            UserId = user.Id,
            IsOwner = false,
            IsDefault = false,
            IsActive = true
        };

        _db.CompanyUsers.Add(membership);

        foreach (var branchId in request.BranchIds ?? [])
        {
            if (!await _db.Branches.AnyAsync(b => b.Id == branchId, cancellationToken))
            {
                throw new NotFoundException("Branch", branchId);
            }

            _db.CompanyUserBranches.Add(new CompanyUserBranch
            {
                CompanyUserId = membership.Id,
                BranchId = branchId
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return membership.Id;
    }
}
