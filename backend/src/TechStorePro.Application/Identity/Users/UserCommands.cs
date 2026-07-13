using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Users;

public record CompanyUserDto(
    Guid UserId,
    string Username,

    /// <summary>What this person actually types to sign in: <c>ahmed@GULF01</c>.</summary>
    string Login,

    string FullName,
    string? Email,
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
        // Tenant-filtered, so this is every user of the caller's company and nobody else's.
        var users = await _db.Users
            .AsNoTracking()
            .Include(u => u.Company)
            .Include(u => u.Permissions)
            .Include(u => u.BranchAccess)
            .OrderByDescending(u => u.IsOwner)
            .ThenBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        return users
            .Select(u => new CompanyUserDto(
                u.Id,
                u.Username,
                $"{u.Username}@{u.Company.Code}",
                u.FullName,
                u.Email,
                u.Phone,
                u.IsOwner,
                u.IsActive,

                // An owner's permissions are implicit, so a stored count of zero would read as "this
                // user can do nothing" — the exact opposite of the truth.
                u.IsOwner
                    ? FeatureCatalog.All.Sum(f => f.SupportedActions.Length)
                    : u.Permissions.Count(p => p.Granted),

                u.BranchAccess.Select(b => b.BranchId).ToList()))
            .ToList();
    }
}

// --- Create -------------------------------------------------------------------------------------

/// <summary>
/// A company admin adds one of their own staff.
///
/// <b>The username is theirs to choose.</b> It only has to be unique within this company, so a shop can
/// call its manager "admin" without being told that an invisible stranger already took the name. The
/// person will sign in as <c>username@COMPANYCODE</c>.
///
/// There is no attaching to an existing platform-wide account any more, because there is no such thing:
/// a user belongs to one company. Somebody who genuinely works for two has two accounts, which is the
/// honest description of what they are.
///
/// New users start with <b>no permissions at all</b>: they can do nothing until an admin grants it.
/// Defaulting to any starting set would be a role by another name, and requirements §7 forbids that.
/// </summary>
[RequiresPermission(FeatureCatalog.Users, PermissionAction.Create)]
public record CreateUserCommand(
    string Username,
    string FullName,
    string TemporaryPassword,
    string? Email = null,
    string? Phone = null,
    IReadOnlyCollection<Guid>? BranchIds = null) : IRequest<Guid>;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(100)
            .Must(u => !u.Contains('@'))
            .WithMessage(
                "A username cannot contain '@'. The login is 'username@COMPANY', and the '@' is what "
                + "separates the two halves.");

        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.TemporaryPassword)
            .NotEmpty()
            .MinimumLength(10).WithMessage("Temporary password must be at least 10 characters.");

        // Optional now — a counter clerk may simply not have one.
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITenantContext _tenant;

    public CreateUserCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        ITenantContext tenant)
    {
        _db = db;
        _hasher = hasher;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenant.CompanyId
            ?? throw new UnauthorizedAccessException("Authentication is required.");

        var username = User.NormaliseUsername(request.Username);

        // Scoped to this company by the tenant filter, which is exactly the uniqueness rule: another
        // company having an "admin" is none of this company's business.
        var taken = await _db.Users.AnyAsync(u => u.Username == username, cancellationToken);

        if (taken)
        {
            throw new ConflictException($"Somebody in this company already uses the username '{username}'.");
        }

        var user = new User
        {
            CompanyId = companyId,
            Username = username,
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim().ToLowerInvariant(),
            Phone = request.Phone,
            PasswordHash = _hasher.Hash(request.TemporaryPassword),
            IsOwner = false,
            IsActive = true,

            // They must pick their own password on first sign-in: an admin who knows a user's working
            // password can act as them, and the audit trail would show the user.
            MustChangePassword = true
        };

        _db.Users.Add(user);

        foreach (var branchId in request.BranchIds ?? [])
        {
            if (!await _db.Branches.AnyAsync(b => b.Id == branchId, cancellationToken))
            {
                throw new NotFoundException("Branch", branchId);
            }

            _db.UserBranches.Add(new UserBranch
            {
                CompanyId = companyId,
                UserId = user.Id,
                BranchId = branchId
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
