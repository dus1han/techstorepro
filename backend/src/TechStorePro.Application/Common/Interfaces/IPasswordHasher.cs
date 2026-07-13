namespace TechStorePro.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Constant-time verification. Returns false for a malformed stored hash, never throws.</summary>
    bool Verify(string password, string hash);
}

/// <summary>Issues the access and refresh tokens (requirements §8).</summary>
public interface ITokenService
{
    /// <summary>
    /// An access token for a user of one company. Carries <c>sub</c>, <c>username</c> and
    /// <c>company_id</c> — and no permissions, and no role. See <see cref="IPermissionService"/>.
    /// </summary>
    string CreateAccessToken(Guid userId, string username, Guid companyId, int lifetimeMinutes);

    /// <summary>
    /// An access token for a platform operator. Carries <c>platform_admin</c> and, deliberately, <b>no
    /// <c>company_id</c></b> — they belong to no company. Tenant endpoints must therefore refuse it.
    /// </summary>
    string CreatePlatformAccessToken(Guid platformAdminId, string username, int lifetimeMinutes);

    /// <summary>Returns the raw token to hand to the caller, and the hash to store.</summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}
