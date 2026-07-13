using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

public enum LoginResult : short
{
    Success = 1,
    BadCredentials = 2,
    LockedOut = 3,
    InactiveUser = 4,
    InactiveCompany = 5
}

/// <summary>
/// Login history, device and IP tracking (requirements §8).
///
/// Failures are recorded against the email typed, not against a user id, because the most
/// interesting failure — someone probing an address that does not exist — has no user to hang off.
/// </summary>
public class LoginHistory : BaseEntity
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>What the caller actually typed. Never a password, of course.</summary>
    public string Email { get; set; } = null!;

    public Guid? CompanyId { get; set; }

    public LoginResult Result { get; set; }
    public string? FailureReason { get; set; }

    public DateTimeOffset At { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceInfo { get; set; }
}
