using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Configuration;

/// <summary>
/// One version of one setting's value, for one company (and optionally one branch).
///
/// Values are <b>versioned, never overwritten</b>. Changing a setting writes a new row with a new
/// <see cref="ValidFrom"/> and closes the previous one. That is what makes General Rule 3 —
/// "historical transactions must not change when settings are updated" — actually true rather than
/// merely intended: a document raised last March resolves the value that was in force last March.
/// </summary>
public class SettingValue : TenantEntity
{
    public string Key { get; set; } = null!;

    /// <summary>Null = the company-wide value. Set = a branch override of it.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Serialised per the definition's <see cref="SettingDataType"/>.</summary>
    public string Value { get; set; } = null!;

    public DateTimeOffset ValidFrom { get; set; }

    /// <summary>Null = still in force.</summary>
    public DateTimeOffset? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Is this version the one in force at <paramref name="at"/>?</summary>
    public bool IsInForceAt(DateTimeOffset at) =>
        IsActive && ValidFrom <= at && (ValidTo is null || ValidTo > at);
}
