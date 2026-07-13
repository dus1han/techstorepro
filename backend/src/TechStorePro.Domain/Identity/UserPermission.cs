using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// One grant: this user may perform this action on this feature.
///
/// This is the entire authorisation model. Requirements §7 says "No fixed roles", so there is no
/// role table, no template table and nothing that resolves a role name at runtime — a grant is
/// always a row against a single user. The permission screen offers bulk toggles (a whole module,
/// a whole action column, copy another user's grid), but every one of them writes individual rows
/// here, so editing one user can never silently change another.
/// </summary>
/// <remarks>
/// Audited and tenant-scoped, but deliberately <b>not</b> soft-deletable, unlike most entities.
///
/// A grant is a join row, not a business record: revoking it must actually remove it, because the
/// unique index on (user, feature, action) would otherwise reject re-granting the same permission
/// later — a retired row still occupies the key. The history of who granted and revoked what is not
/// lost; it lives in the audit log, which is where a permissions question gets answered anyway.
/// </remarks>
public class UserPermission : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>A code from <see cref="FeatureCatalog"/>.</summary>
    public string FeatureCode { get; set; } = null!;

    public PermissionAction Action { get; set; }

    /// <summary>
    /// Present-and-false is a real state, distinct from absent: it lets the UI show an explicitly
    /// withheld permission, and lets a future "deny overrides" rule exist without a schema change.
    /// Today, absent and false both mean denied.
    /// </summary>
    public bool Granted { get; set; } = true;
}
