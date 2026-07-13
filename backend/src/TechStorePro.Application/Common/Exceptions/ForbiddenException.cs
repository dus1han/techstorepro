namespace TechStorePro.Application.Common.Exceptions;

/// <summary>
/// The caller is authenticated but lacks the permission. Rendered as 403.
///
/// Note what this is <em>not</em> used for: reaching across tenants. That is a 404, not a 403,
/// because a 403 would confirm the record exists — telling company A that company B's invoice id is
/// real is itself a leak (api-design.md §4).
/// </summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string feature, string action)
        : base($"Permission denied: {action} on {feature}.")
    {
        Feature = feature;
        Action = action;
    }

    public ForbiddenException(string message) : base(message)
    {
    }

    public string? Feature { get; }
    public string? Action { get; }
}

/// <summary>A concurrency clash or a duplicate. Rendered as 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
