using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Commands.RefreshSession;

[AllowAnonymousRequest]
public record RefreshSessionCommand(string RefreshToken) : IRequest<AuthResult>;

public class RefreshSessionCommandValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public class RefreshSessionCommandHandler : IRequestHandler<RefreshSessionCommand, AuthResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IDateTime _clock;
    private readonly IAuthSessionFactory _sessions;

    public RefreshSessionCommandHandler(
        IApplicationDbContext db,
        ITokenService tokens,
        IDateTime clock,
        IAuthSessionFactory sessions)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
        _sessions = sessions;
    }

    public async Task<AuthResult> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var now = _clock.UtcNow;

        // No tenant on the wire yet — the refresh token itself carries the company.
        var stored = await _db.IgnoringTenantFilter<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        // A revoked token being presented is not a mistake — the legitimate holder rotated it away,
        // so whoever is replaying it copied it. We cannot tell attacker from victim, so we end the
        // whole chain and force a fresh login.
        if (stored.RevokedAt is not null)
        {
            await RevokeChainAsync(stored, now, cancellationToken);
            throw new UnauthorizedAccessException(
                "This refresh token has already been used. All sessions have been signed out.");
        }

        if (!stored.IsActive(now))
        {
            throw new UnauthorizedAccessException("Refresh token has expired.");
        }

        var result = await _sessions.IssueAsync(stored.UserId, stored.CompanyId, cancellationToken);

        // Rotate: the token just used is dead the moment its replacement exists.
        stored.RevokedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>Revokes every live token for this user in this company.</summary>
    private async Task RevokeChainAsync(RefreshToken compromised, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var live = await _db.IgnoringTenantFilter<RefreshToken>()
            .Where(t => t.UserId == compromised.UserId
                        && t.CompanyId == compromised.CompanyId
                        && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in live)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
