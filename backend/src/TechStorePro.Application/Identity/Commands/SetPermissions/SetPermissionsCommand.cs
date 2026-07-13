using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Commands.SetPermissions;

public record PermissionAssignment(string Feature, PermissionAction Action, bool Granted);

/// <summary>
/// Replaces a member's grants with exactly the set supplied.
///
/// The whole grid is sent, not a diff — a diff would race with a second admin editing the same
/// member and silently merge two conflicting intentions. Sending the full set makes the last writer
/// win, visibly.
/// </summary>
[RequiresPermission(FeatureCatalog.Permissions, PermissionAction.Edit)]
public record SetPermissionsCommand(
    Guid UserId,
    IReadOnlyCollection<PermissionAssignment> Permissions) : IRequest;

public class SetPermissionsCommandValidator : AbstractValidator<SetPermissionsCommand>
{
    public SetPermissionsCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleForEach(x => x.Permissions).ChildRules(p =>
        {
            p.RuleFor(x => x.Feature)
                .Must(FeatureCatalog.Exists)
                .WithMessage(x => $"'{x.Feature}' is not a known feature.");

            // A grant for an action the feature does not support can never be checked by anything.
            // Storing it would quietly grow a set of permissions that look meaningful and are not.
            p.RuleFor(x => x)
                .Must(x => !x.Granted || FeatureCatalog.Supports(x.Feature, x.Action))
                .WithMessage(x => $"Feature '{x.Feature}' does not support the {x.Action} action.");
        });
    }
}

public class SetPermissionsCommandHandler : IRequestHandler<SetPermissionsCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _currentUser;

    public SetPermissionsCommandHandler(
        IApplicationDbContext db,
        IPermissionService permissions,
        ICurrentUser currentUser)
    {
        _db = db;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    public async Task Handle(SetPermissionsCommand request, CancellationToken cancellationToken)
    {
        // Tenant-filtered: a user id from another company simply does not resolve, so this cannot be
        // used to edit a stranger's permissions by guessing their id.
        var user = await _db.Users
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        // The owner's permissions are implicit and total. Allowing them to be edited would let an
        // administrator strip the owner — including stripping themselves — and leave the company with
        // nobody able to grant anything back. The recovery would be a manual database edit.
        if (user.IsOwner)
        {
            throw new DomainException(
                "The company owner holds every permission implicitly and cannot have them edited.");
        }

        var wanted = request.Permissions
            .Where(p => p.Granted)
            .Select(p => (p.Feature, p.Action))
            .ToHashSet();

        var existing = user.Permissions.ToDictionary(p => (p.FeatureCode, p.Action));

        foreach (var (key, permission) in existing)
        {
            if (!wanted.Contains(key))
            {
                _db.UserPermissions.Remove(permission);
            }
        }

        foreach (var key in wanted)
        {
            if (existing.TryGetValue(key, out var already))
            {
                already.Granted = true;
                continue;
            }

            _db.UserPermissions.Add(new UserPermission
            {
                UserId = user.Id,
                FeatureCode = key.Item1,
                Action = key.Item2,
                Granted = true
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        // The cached grants for this user are now wrong. Dropping them here is what makes a revocation
        // take effect on the user's very next request, rather than up to two minutes later — which is
        // the entire reason permissions are not carried in the token.
        _permissions.InvalidateCache(user.Id);
    }
}
