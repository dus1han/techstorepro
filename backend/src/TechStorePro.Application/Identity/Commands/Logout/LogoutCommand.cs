using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Commands.Logout;

/// <summary>
/// Revokes the caller's refresh token. The access token is not revoked — it is short-lived and
/// stateless by design; revoking it would mean a database round-trip on every request, which is the
/// cost JWT exists to avoid.
/// </summary>
public record LogoutCommand(string RefreshToken, bool AllSessions = false) : IRequest;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _currentUser;

    public LogoutCommandHandler(
        IApplicationDbContext db,
        ITokenService tokens,
        IDateTime clock,
        ICurrentUser currentUser)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (request.AllSessions && _currentUser.UserId is { } userId)
        {
            var all = await _db.IgnoringTenantFilter<RefreshToken>()
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in all)
            {
                token.RevokedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var hash = _tokens.HashRefreshToken(request.RefreshToken);

        var stored = await _db.IgnoringTenantFilter<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, cancellationToken);

        // Logging out with a token that is already dead is not an error — the caller wanted to be
        // signed out, and they are. Reporting a failure here only teaches clients to ignore it.
        if (stored is null)
        {
            return;
        }

        stored.RevokedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
