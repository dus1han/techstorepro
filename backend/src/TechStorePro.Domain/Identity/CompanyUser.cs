using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A person's membership of one company. Permissions hang here, not on <see cref="User"/>: the same
/// person may be an owner in one company and a counter clerk in another, and the two must not leak
/// into each other.
/// </summary>
public class CompanyUser : TenantEntity
{
    public Company Company { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>The company this member lands in when they log in without choosing.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The company's first user, created at registration. An owner implicitly holds every
    /// permission, and cannot be locked out of the permission screen — otherwise a company could
    /// revoke its own last administrator and permanently brick itself.
    /// </summary>
    public bool IsOwner { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Branches this member may work in. Empty = all branches of the company.</summary>
    public ICollection<CompanyUserBranch> BranchAccess { get; set; } = [];

    public ICollection<UserPermission> Permissions { get; set; } = [];
}

/// <summary>
/// Restricts a member to specific branches (requirements §6 "Branch access").
///
/// A join row: hard-deleted rather than retired, for the same reason as <see cref="UserPermission"/>
/// — re-granting branch access must not collide with a soft-deleted row holding the unique key.
/// </summary>
public class CompanyUserBranch : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public Guid CompanyUserId { get; set; }
    public CompanyUser CompanyUser { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
}
