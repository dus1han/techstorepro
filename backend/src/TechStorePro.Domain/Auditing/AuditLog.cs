using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Auditing;

public enum AuditAction : short
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Restore = 4,
    Approve = 5,
    Cancel = 6,
    Print = 7,
    Export = 8,
    Login = 9,
    Logout = 10,
    PermissionChange = 11,
    SettingChange = 12
}

/// <summary>
/// One recorded action (requirements §9). Written by the DbContext from the EF change tracker for
/// data changes, and explicitly by handlers for the actions that leave no row behind — a print, an
/// export, a login.
///
/// <see cref="OldValues"/> and <see cref="NewValues"/> are the point of the table. "Someone changed
/// the credit limit" is not an audit trail; "Maryam changed it from 5,000 to 50,000 at 16:02 from
/// this IP" is.
///
/// The log is append-only. It is not tenant-scoped through <see cref="TenantEntity"/> — it carries
/// its own CompanyId and is never soft-deleted, because an audit row that can be retired by the
/// person being audited is worthless.
/// </summary>
public class AuditLog : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>
    /// Who did it, by username — not by email. Email is optional on a user now (a counter clerk may
    /// not have one) and is not unique, so an audit trail keyed on it could record a blank, or the
    /// same address for two people. The username is neither.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>The CLR entity name, e.g. "Invoice". Not the table name — the reader is a human.</summary>
    public string EntityType { get; set; } = null!;

    /// <summary>Null for actions with no row, such as a login or an export.</summary>
    public Guid? EntityId { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>JSON of the changed properties, before. Null on create.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON of the changed properties, after. Null on delete.</summary>
    public string? NewValues { get; set; }

    /// <summary>Free-text context a handler can attach: a delete reason, a document number.</summary>
    public string? Summary { get; set; }

    public string? IpAddress { get; set; }
    public DateTimeOffset At { get; set; }
}
