using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Platform;

/// <summary>
/// A platform operator signing in. <b>A bare username, with no <c>@company</c> after it</b> — that
/// absence is precisely what separates this flow from a tenant login, and it is why the two live at
/// different endpoints against different tables.
/// </summary>
[AllowAnonymousRequest]
public record PlatformLoginCommand(string Username, string Password) : IRequest<PlatformAuthResult>;

public class PlatformLoginCommandValidator : AbstractValidator<PlatformLoginCommand>
{
    public PlatformLoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class PlatformLoginCommandHandler : IRequestHandler<PlatformLoginCommand, PlatformAuthResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _caller;
    private readonly IAuthSessionFactory _sessions;

    public PlatformLoginCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        IDateTime clock,
        ICurrentUser caller,
        IAuthSessionFactory sessions)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _caller = caller;
        _sessions = sessions;
    }

    public async Task<PlatformAuthResult> Handle(
        PlatformLoginCommand request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var now = _clock.UtcNow;

        var admin = await _db.PlatformAdmins
            .FirstOrDefaultAsync(a => a.Username == username, cancellationToken);

        if (admin is null)
        {
            // Same constant-time defence as the tenant login: this credential reaches every company on
            // the platform, so it is the single most valuable account in the system to enumerate.
            _hasher.Verify(request.Password, DummyHash);
            await RecordAsync(null, username, LoginResult.BadCredentials, "No such platform admin", now, cancellationToken);
            throw new UnauthorizedAccessException(Incorrect);
        }

        if (admin.IsLockedOut(now))
        {
            await RecordAsync(admin.Id, username, LoginResult.LockedOut, "Locked out", now, cancellationToken);
            throw new UnauthorizedAccessException(
                $"This account is locked until {admin.LockedUntil:HH:mm} UTC after too many failed attempts.");
        }

        if (!admin.IsActive)
        {
            await RecordAsync(admin.Id, username, LoginResult.InactiveUser, "Inactive", now, cancellationToken);
            throw new UnauthorizedAccessException(Incorrect);
        }

        if (!_hasher.Verify(request.Password, admin.PasswordHash))
        {
            var maxAttempts = int.Parse(SettingCatalog.Find(SettingCatalog.MaxFailedLogins)!.DefaultValue);
            var lockoutMinutes = int.Parse(SettingCatalog.Find(SettingCatalog.LockoutMinutes)!.DefaultValue);

            admin.RegisterFailedLogin(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes));

            await RecordAsync(admin.Id, username, LoginResult.BadCredentials, "Wrong password", now, cancellationToken);
            throw new UnauthorizedAccessException(Incorrect);
        }

        admin.RegisterSuccessfulLogin(now);
        await RecordAsync(admin.Id, username, LoginResult.Success, null, now, cancellationToken);

        return await _sessions.IssuePlatformAsync(admin.Id, cancellationToken);
    }

    private const string Incorrect = "Username or password is incorrect.";

    private const string DummyHash =
        "1.210000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>
    /// Recorded in the same <c>login_history</c> as everyone else, with no user id — a platform admin
    /// is not a <see cref="User"/> and the FK would not resolve. The point is that an attack on the
    /// platform account shows up in the same place an operator is already looking.
    /// </summary>
    private async Task RecordAsync(
        Guid? adminId,
        string username,
        LoginResult result,
        string? reason,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        _db.LoginHistory.Add(new LoginHistory
        {
            UserId = null,
            Login = adminId is null ? username : $"{username} (platform)",
            CompanyId = null,
            Result = result,
            FailureReason = reason,
            At = at,
            IpAddress = _caller.IpAddress,
            UserAgent = _caller.UserAgent
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
