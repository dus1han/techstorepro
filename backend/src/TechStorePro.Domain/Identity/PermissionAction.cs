namespace TechStorePro.Domain.Identity;

/// <summary>
/// The seven actions of requirements §7. A permission is one of these applied to one feature —
/// there is no role anywhere in the model.
///
/// Stored as smallint (see database-design.md §2): Postgres enum types are painful to alter, and
/// this list will grow.
/// </summary>
public enum PermissionAction : short
{
    View = 1,
    Create = 2,
    Edit = 3,
    Delete = 4,
    Approve = 5,
    Print = 6,
    Export = 7
}
