using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Sales.Services;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Sales;

/// <inheritdoc cref="IDiscountAuthorizer"/>
public class DiscountAuthorizer : IDiscountAuthorizer
{
    private readonly IApplicationDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _user;

    public DiscountAuthorizer(
        IApplicationDbContext db,
        IPermissionService permissions,
        ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
    }

    public async Task<Guid?> AuthoriseAsync(
        string description,
        decimal floor,
        Guid? approvedBy = null,
        CancellationToken cancellationToken = default)
    {
        // A named approver: the manager came over to the till and authorised it. They must actually hold
        // the permission — otherwise "approved by" would be a name typed into a box, which is worse than
        // no approval at all because it looks like one.
        if (approvedBy is { } approver)
        {
            var mayApprove = await _db.UserPermissions
                .AnyAsync(
                    p => p.UserId == approver
                         && p.FeatureCode == FeatureCatalog.SalesInvoices
                         && p.Action == PermissionAction.Approve,
                    cancellationToken);

            // The owner holds every permission implicitly and has no rows in user_permissions — the same
            // rule that stops a company revoking its own last administrator.
            var isOwner = await _db.Users
                .AnyAsync(u => u.Id == approver && u.IsOwner, cancellationToken);

            if (!mayApprove && !isOwner)
            {
                throw new DomainException(
                    $"{description} is priced below its floor of {floor:0.##} and the named approver is "
                    + "not authorised to approve discounts (§32).");
            }

            return approver;
        }

        // Nobody named: the person at the till is authorising it themselves. Fine, if they may.
        if (await _permissions.HasPermissionAsync(
                FeatureCatalog.SalesInvoices,
                PermissionAction.Approve,
                cancellationToken))
        {
            return _user.UserId;
        }

        throw new DomainException(
            $"{description} is priced below its floor of {floor:0.##} and needs a manager's approval "
            + "(§32). Have someone with approval rights authorise it.");
    }
}
