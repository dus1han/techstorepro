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

    private readonly IConfiguration _configuration;
    private readonly IDateTime _clock;

    public TokenService(IConfiguration configuration, IDateTime clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public string CreateAccessToken(Guid userId, string email, Guid companyId, int lifetimeMinutes)
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

        // The claim set is deliberately small: who you are, and which company you are acting in.
        //
        // No role claim — requirements §7 forbids fixed roles. No permission claims either: they are
        // read per request from the database, so that revoking a permission takes effect at once
        // rather than whenever this token happens to expire.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Email, email),
            new(CompanyIdClaim, companyId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

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
