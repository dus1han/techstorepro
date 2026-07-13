using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using TechStorePro.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace TechStorePro.Infrastructure.Identity;

public class TokenService : ITokenService
{
    public const string CompanyIdClaim = "company_id";
    public const string UsernameClaim = "username";

    /// <summary>
    /// Marks a platform-operator token. Its <em>absence</em> is not what makes a token a tenant token —
    /// the <see cref="CompanyIdClaim"/> does that. Both are checked positively, by two separate
    /// authorization policies, because a token that is neither must be refused rather than fall through
    /// into whichever branch happens to come first.
    /// </summary>
    public const string PlatformAdminClaim = "platform_admin";

    private readonly IConfiguration _configuration;
    private readonly IDateTime _clock;

    public TokenService(IConfiguration configuration, IDateTime clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public string CreateAccessToken(Guid userId, string username, Guid companyId, int lifetimeMinutes)
    {
        // The claim set is deliberately small: who you are, and which company you are acting in.
        //
        // No role claim — requirements §7 forbids fixed roles. No permission claims either: they are
        // read per request from the database, so that revoking a permission takes effect at once
        // rather than whenever this token happens to expire.
        //
        // No email claim any more: email is optional and non-unique on a user, so it cannot identify
        // anybody. The username can, and it is what the audit trail records.
        return Write(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(UsernameClaim, username),
                new Claim(CompanyIdClaim, companyId.ToString())
            ],
            lifetimeMinutes);
    }

    /// <summary>
    /// A platform operator's token. <b>It carries no <c>company_id</c>, and that is the whole point:</b>
    /// a platform admin belongs to no company.
    ///
    /// It is also why tenant endpoints must demand the company claim <em>positively</em>. A null tenant
    /// switches the DbContext query filters off — which is correct for a migration and catastrophic for
    /// a request — so a token like this one reaching <c>/api/v1/products</c> would read every company on
    /// the platform. The <c>Tenant</c> authorization policy is what stops that, and there is a test that
    /// proves it.
    /// </summary>
    public string CreatePlatformAccessToken(Guid platformAdminId, string username, int lifetimeMinutes) =>
        Write(
            [
                new Claim(JwtRegisteredClaimNames.Sub, platformAdminId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, platformAdminId.ToString()),
                new Claim(UsernameClaim, username),
                new Claim(PlatformAdminClaim, "true")
            ],
            lifetimeMinutes);

    private string Write(List<Claim> claims, int lifetimeMinutes)
    {
        var jwt = _configuration.GetSection("Jwt");

        var key = jwt["Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Jwt:Key is not configured. Set it via user-secrets or the environment.");
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));

        var now = _clock.UtcNow;

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(lifetimeMinutes).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        // 256 bits from a CSPRNG. A refresh token is a bearer credential with a two-week life —
        // it has to be unguessable, and Guid.NewGuid() is not a random number generator.
        var raw = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(raw);

        return (token, HashRefreshToken(token));
    }

    /// <summary>
    /// SHA-256, unsalted, deliberately. This is a lookup key for a high-entropy random token, not a
    /// password: it must be reproducible to find the row, and there is nothing to brute-force
    /// because the input is 256 random bits rather than something a human chose.
    /// </summary>
    public string HashRefreshToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
