using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Commands.Login;

[AllowAnonymousRequest]
public record LoginCommand(string Email, string Password, Guid? CompanyId = null) : IRequest<AuthResult>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _caller;
    private readonly ISettingsProvider _settings;
    private readonly IAuthSessionFactory _sessions;

    public LoginCommandHandler(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        IDateTime clock,
        ICurrentUser caller,
        ISettingsProvider settings,
        IAuthSessionFactory sessions)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _caller = caller;
        _settings = settings;
        _sessions = sessions;
    }

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var now = _clock.UtcNow;

        // No tenant filter can apply here: we do not know which company the caller belongs to until
        // after we have identified them.
        var user = await _db.IgnoringTenantFilter<User>()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            // Verify against nothing anyway. Returning immediately would make a non-existent account
            // measurably faster to reject than a wrong password, which is a free user-enumeration
            // oracle for anyone with a stopwatch.
            _hasher.Verify(request.Password, DummyHash);
            await RecordAttemptAsync(null, email, LoginResult.BadCredentials, "No such user", now, cancellationToken);
            throw new UnauthorizedAccessException("Email or password is incorrect.");
        }

        if (user.IsLockedOut(now))
        {
            await RecordAttemptAsync(user.Id, email, LoginResult.LockedOut, "Account is locked", now, cancellationToken);
            throw new UnauthorizedAccessException(
                $"This account is locked until {user.LockedUntil:HH:mm} UTC after too many failed attempts.");
        }

        if (!user.IsActive || user.IsDeleted)
        {
            await RecordAttemptAsync(user.Id, email, LoginResult.InactiveUser, "User is inactive", now, cancellationToken);
            throw new UnauthorizedAccessException("Email or password is incorrect.");
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            // The policy is per-company configuration, but a failed login happens before a company is
            // known — so the platform defaults apply. This is the one place the settings engine
            // cannot be reached, and it is why the defaults live in SettingCatalog rather than here.
            var maxAttempts = int.Parse(SettingCatalog.Find(SettingCatalog.MaxFailedLogins)!.DefaultValue);
            var lockoutMinutes = int.Parse(SettingCatalog.Find(SettingCatalog.LockoutMinutes)!.DefaultValue);

            user.RegisterFailedLogin(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes));

            await RecordAttemptAsync(user.Id, email, LoginResult.BadCredentials, "Wrong password", now, cancellationToken);
            throw new UnauthorizedAccessException("Email or password is incorrect.");
        }

        var memberships = await _db.IgnoringTenantFilter<CompanyUser>()
            .Include(m => m.Company)
            .Where(m => m.UserId == user.Id && m.IsActive && !m.IsDeleted)
            .ToListAsync(cancellationToken);

        var usable = memberships
            .Where(m => m.Company is { IsActive: true, IsDeleted: false })
            .ToList();

        if (usable.Count == 0)
        {
            await RecordAttemptAsync(user.Id, email, LoginResult.InactiveCompany, "No active company", now, cancellationToken);
            throw new UnauthorizedAccessException("This account has no active company.");
        }

        var target = request.CompanyId is { } requested
            ? usable.FirstOrDefault(m => m.CompanyId == requested)
              ?? throw new UnauthorizedAccessException("You are not a member of that company.")
            : usable.FirstOrDefault(m => m.IsDefault) ?? usable[0];

        user.RegisterSuccessfulLogin(now);
        await RecordAttemptAsync(user.Id, email, LoginResult.Success, null, now, cancellationToken, target.CompanyId);

        return await _sessions.IssueAsync(user.Id, target.CompanyId, cancellationToken);
    }

    /// <summary>
    /// A well-formed hash of a password nobody has, in the exact format <c>PasswordHasher</c> emits
    /// (version.iterations.salt.hash). It must parse, or verification would bail out early and the
    /// whole point would be lost: this exists so that a non-existent account costs the same
    /// ~210,000 PBKDF2 iterations as a real one with a wrong password. Otherwise the response time
    /// tells an attacker which email addresses are registered.
    /// </summary>
    private const string DummyHash =
        "1.210000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private async Task RecordAttemptAsync(
        Guid? userId,
        string email,
        LoginResult result,
        string? reason,
        DateTimeOffset at,
        CancellationToken cancellationToken,
        Guid? companyId = null)
    {
        _db.LoginHistory.Add(new LoginHistory
        {
            UserId = userId,
            Email = email,
            CompanyId = companyId,
            Result = result,
            FailureReason = reason,
            At = at,
            IpAddress = _caller.IpAddress,
            UserAgent = _caller.UserAgent
        });

        // Saved even on the failure paths, because the lockout counter and the history are the whole
        // point — a failed login that is not persisted protects nobody.
        await _db.SaveChangesAsync(cancellationToken);
    }
}
