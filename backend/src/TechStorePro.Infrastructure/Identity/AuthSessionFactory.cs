using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Identity;

/// <summary>
/// The one place a signed-in session is minted. Login and refresh both end here, so the claim set, the
/// token lifetimes and the rotation rules cannot drift apart between them.
///
/// There is no company argument any more. A user belongs to exactly one company, so the session's
/// company is <c>user.CompanyId</c> and nothing else — passing it in would invite a caller to pass the
/// wrong one.
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

    public async Task<AuthResult> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // The tenant filter is bypassed deliberately: this runs *before* the token that would name the
        // company exists, so there is no tenant on the request to filter by yet.
        var user = await _db.IgnoringTenantFilter<User>()
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        // Token lifetimes are configuration, not constants (requirements §11). A company that wants
        // 15-minute access tokens should not need a deploy to get them.
        var accessMinutes = await _settings.GetAsync<int>(SettingCatalog.AccessTokenMinutes, cancellationToken);
        var refreshDays = await _settings.GetAsync<int>(SettingCatalog.RefreshTokenDays, cancellationToken);

        var now = _clock.UtcNow;

        var accessToken = _tokens.CreateAccessToken(user.Id, user.Username, user.CompanyId, accessMinutes);
        var (refreshToken, refreshHash) = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(refreshDays),
            IpAddress = _caller.IpAddress,
            DeviceInfo = _caller.UserAgent
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Permissions are resolved fresh by GET /auth/me rather than being stuffed in here: this runs
        // before the new company_id claim exists on the request, so IPermissionService would still be
        // answering for whatever company the *previous* token named — or for none at all.
        var profile = new CurrentUserDto(
            user.Id,
            user.Username,
            user.FullName,
            user.CompanyId,
            user.Company.Name,
            user.Company.Code,
            user.IsOwner,
            [],
            []);

        return new AuthResult(accessToken, refreshToken, now.AddMinutes(accessMinutes), profile);
    }

    public async Task<PlatformAuthResult> IssuePlatformAsync(
        Guid platformAdminId,
        CancellationToken cancellationToken = default)
    {
        var admin = await _db.PlatformAdmins
            .FirstOrDefaultAsync(a => a.Id == platformAdminId, cancellationToken)
            ?? throw new NotFoundException(nameof(PlatformAdmin), platformAdminId);

        // Fixed, not read from Settings. ISettingsProvider resolves per company and a platform admin
        // has none — but the deeper reason is that a tenant must not get to choose how long the
        // platform operator's session lives. Shorter than a shop's, because this credential can reach
        // every company on the platform.
        const int accessMinutes = 15;
        const int refreshDays = 1;

        var now = _clock.UtcNow;

        var accessToken = _tokens.CreatePlatformAccessToken(admin.Id, admin.Username, accessMinutes);
        var (refreshToken, refreshHash) = _tokens.CreateRefreshToken();

        _db.PlatformRefreshTokens.Add(new PlatformRefreshToken
        {
            PlatformAdminId = admin.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(refreshDays),
            IpAddress = _caller.IpAddress,
            DeviceInfo = _caller.UserAgent
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new PlatformAuthResult(
            accessToken,
            refreshToken,
            now.AddMinutes(accessMinutes),
            new PlatformAdminDto(admin.Id, admin.Username, admin.FullName));
    }
}
