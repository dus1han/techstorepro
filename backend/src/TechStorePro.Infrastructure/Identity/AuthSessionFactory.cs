using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Identity;

/// <summary>
/// The one place a signed-in session is minted. Login, refresh and switch-company all end here, so
/// the claim set, the token lifetimes and the rotation rules cannot drift apart between them.
/// </summary>
public class AuthSessionFactory : IAuthSessionFactory
{
    private readonly IApplicationDbContext _db;
    private readonly ITokenService _tokens;
    private readonly ISettingsProvider _settings;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _caller;

    public AuthSessionFactory(
        IApplicationDbContext db,
        ITokenService tokens,
        ISettingsProvider settings,
        IDateTime clock,
        ICurrentUser caller)
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
        _clock = clock;
        _caller = caller;
    }

    public async Task<AuthResult> IssueAsync(
        Guid userId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.IgnoringTenantFilter<User>()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        // Every membership, so the client can render the company switcher. The tenant filter is
        // bypassed deliberately: the caller's *current* token may name a different company, or none
        // at all if this is a fresh login.
        var memberships = await _db.IgnoringTenantFilter<CompanyUser>()
            .Include(m => m.Company)
            .Where(m => m.UserId == userId && m.IsActive && !m.IsDeleted)
            .ToListAsync(cancellationToken);

        var active = memberships.FirstOrDefault(m => m.CompanyId == companyId)
            ?? throw new UnauthorizedAccessException("You are not a member of that company.");

        // Token lifetimes are configuration, not constants (requirements §11). A company that wants
        // 15-minute access tokens should not need a deploy to get them.
        var accessMinutes = await _settings.GetAsync<int>(SettingCatalog.AccessTokenMinutes, cancellationToken);
        var refreshDays = await _settings.GetAsync<int>(SettingCatalog.RefreshTokenDays, cancellationToken);

        var now = _clock.UtcNow;

        var accessToken = _tokens.CreateAccessToken(user.Id, user.Email, companyId, accessMinutes);
        var (refreshToken, refreshHash) = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            CompanyId = companyId,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(refreshDays),
            IpAddress = _caller.IpAddress,
            DeviceInfo = _caller.UserAgent
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Permissions are resolved fresh by GET /auth/me rather than being stuffed in here: this
        // runs before the new company_id claim exists on the request, so IPermissionService would
        // still be answering for the *previous* company.
        var profile = new CurrentUserDto(
            user.Id,
            user.Email,
            user.FullName,
            companyId,
            active.Company.Name,
            active.IsOwner,
            [],
            []);

        var companies = memberships
            .Where(m => m.Company is { IsActive: true, IsDeleted: false })
            .Select(m => new CompanyMembershipDto(m.CompanyId, m.Company.Name, m.IsDefault, m.IsOwner))
            .ToList();

        return new AuthResult(
            accessToken,
            refreshToken,
            now.AddMinutes(accessMinutes),
            profile,
            companies);
    }
}
