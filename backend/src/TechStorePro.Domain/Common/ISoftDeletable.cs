namespace TechStorePro.Domain.Common;

/// <summary>
/// Accounting and repair history must remain auditable, so rows are retired rather than
/// removed. Deletes on these entities are rewritten into updates by the DbContext.
///
/// A retired row records <em>why</em> it was retired: requirements §10 asks for the reason and a
/// restore path, not merely a flag. "Who deleted this, and what did they say at the time?" is the
/// question a disputed stock write-off actually turns on.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
    string? DeletedReason { get; set; }
}
