namespace TechStorePro.Domain.Configuration;

public enum SettingDataType : short
{
    String = 1,
    Integer = 2,
    Decimal = 3,
    Boolean = 4,
    Json = 5
}

public enum SettingScope : short
{
    /// <summary>One value for the whole company.</summary>
    Company = 1,

    /// <summary>May be overridden per branch; falls back to the company value.</summary>
    Branch = 2
}

/// <summary>
/// The catalogue of what is settable. Reference data seeded from <see cref="SettingCatalog"/>, not
/// tenant-scoped: a company chooses a setting's <em>value</em>, it does not invent new settings.
/// </summary>
public class SettingDefinition
{
    public string Key { get; set; } = null!;
    public string Module { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public SettingDataType DataType { get; set; }
    public SettingScope Scope { get; set; }

    /// <summary>Used when a company has never set a value. Always present, so a read never fails.</summary>
    public string DefaultValue { get; set; } = null!;
}

/// <summary>
/// Every business rule that requirements §11 says must be configurable rather than hardcoded.
///
/// Only the P1 rules exist today; later phases append theirs (tax, discount limits, warranty
/// periods, reservation expiry). The rule for contributors is blunt: <b>if you are about to write a
/// magic number into a handler, it belongs here instead.</b>
/// </summary>
public static class SettingCatalog
{
    // Password policy and login protection (requirements §8)
    public const string PasswordMinLength = "security.password.min_length";
    public const string PasswordRequireUpper = "security.password.require_uppercase";
    public const string PasswordRequireDigit = "security.password.require_digit";
    public const string PasswordRequireSymbol = "security.password.require_symbol";
    public const string MaxFailedLogins = "security.login.max_failed_attempts";
    public const string LockoutMinutes = "security.login.lockout_minutes";
    public const string SessionTimeoutMinutes = "security.session.timeout_minutes";

    // Token lifetimes (requirements §8)
    public const string AccessTokenMinutes = "security.token.access_minutes";
    public const string RefreshTokenDays = "security.token.refresh_days";

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new() { Key = PasswordMinLength, Module = "Security", Name = "Minimum password length", DataType = SettingDataType.Integer, Scope = SettingScope.Company, DefaultValue = "10" },
        new() { Key = PasswordRequireUpper, Module = "Security", Name = "Require an uppercase letter", DataType = SettingDataType.Boolean, Scope = SettingScope.Company, DefaultValue = "true" },
        new() { Key = PasswordRequireDigit, Module = "Security", Name = "Require a digit", DataType = SettingDataType.Boolean, Scope = SettingScope.Company, DefaultValue = "true" },
        new() { Key = PasswordRequireSymbol, Module = "Security", Name = "Require a symbol", DataType = SettingDataType.Boolean, Scope = SettingScope.Company, DefaultValue = "false" },
        new() { Key = MaxFailedLogins, Module = "Security", Name = "Failed logins before lockout", DataType = SettingDataType.Integer, Scope = SettingScope.Company, DefaultValue = "5" },
        new() { Key = LockoutMinutes, Module = "Security", Name = "Lockout duration (minutes)", DataType = SettingDataType.Integer, Scope = SettingScope.Company, DefaultValue = "15" },
        new() { Key = SessionTimeoutMinutes, Module = "Security", Name = "Session timeout (minutes)", DataType = SettingDataType.Integer, Scope = SettingScope.Company, DefaultValue = "480" },
        new() { Key = AccessTokenMinutes, Module = "Security", Name = "Access token lifetime (minutes)", DataType = SettingDataType.Integer, Scope = SettingScope.Company, DefaultValue = "30" },
        new() { Key = RefreshTokenDays, Module = "Security", Name = "Refresh token lifetime (days)", DataType = SettingDataType.Integer, Scope = SettingScope.Company, DefaultValue = "14" }
    ];

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(s => s.Key == key);
}
