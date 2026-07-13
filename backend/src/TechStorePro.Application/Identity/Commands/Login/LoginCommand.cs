using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Commands.Login;

/// <param name="Login">
/// The whole login as one string: <c>ahmed@GULF01</c>. One field rather than two, because a separate
/// "company code" box asks the user to know something they cannot discover, and a company dropdown
/// would show every tenant on the platform to anyone who opened the page.
/// </param>
[AllowAnonymousRequest]
public record LoginCommand(string Login, string Password) : IRequest<AuthResult>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // Deliberately not validated for shape here. A malformed login is handled in the handler, on
        // the same timing path as a wrong password, so that "that company does not exist" cannot be
        // told apart from "that password is wrong" — see the handler.
        RuleFor(x => x.Login).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTime _clock;
    private readonly ICurrentUser _caller;
    private readonly IAuthSessionFactory _sessions;

    public LoginCommandHandler(
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

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var typed = request.Login.Trim();
        var now = _clock.UtcNow;

        // --- Resolve the login ------------------------------------------------------------------
        //
        // Every failure below — malformed login, unknown company, unknown user, wrong password —
        // ends in the same message and the same amount of work. A caller must not be able to learn
        // that GULF01 is a real company, or that "ahmed" is a real user in it, by timing the response
        // or reading the error. That is tenant and user enumeration, and it is free if you let it be.

        LoginName? parsed = null;

        try
        {
            parsed = LoginName.Parse(typed);
        }
        catch (DomainException)
        {
            // Not even the right shape. Still pay the hashing cost, still record the attempt: this is
            // exactly what a scanner probing bare usernames looks like, and it is worth seeing.
        }

        var user = parsed is { } name
            ? await FindUserAsync(name, cancellationToken)
            : null;

        if (user is null)
        {
            // Verify against a real-looking hash anyway. Returning early would make a non-existent
            // account measurably faster to reject than a wrong password — a free enumeration oracle
            // for anyone holding a stopwatch.
            _hasher.Verify(request.Password, DummyHash);
            await RecordAttemptAsync(null, typed, LoginResult.BadCredentials, "No such user", now, cancellationToken);
            throw new UnauthorizedAccessException(Incorrect);
        }

        if (user.IsLockedOut(now))
        {
            await RecordAttemptAsync(user.Id, typed, LoginResult.LockedOut, "Account is locked", now, cancellationToken);
            throw new UnauthorizedAccessException(
                $"This account is locked until {user.LockedUntil:HH:mm} UTC after too many failed attempts.");
        }

        if (!user.IsActive)
        {
            await RecordAttemptAsync(user.Id, typed, LoginResult.InactiveUser, "User is inactive", now, cancellationToken);
            throw new UnauthorizedAccessException(Incorrect);
        }

        if (!user.Company.IsActive)
        {
            // The company was suspended by the platform, so nobody in it may sign in — including its
            // owner. This is the enforcement point for a company that has stopped paying.
            await RecordAttemptAsync(user.Id, typed, LoginResult.InactiveCompany, "Company is inactive", now, cancellationToken);
            throw new UnauthorizedAccessException("This company is not active. Contact TechStorePro support.");
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            // The policy is per-company configuration, but the lockout has to be applied before a
            // company context exists on the request — so the platform defaults apply here.
            var maxAttempts = int.Parse(SettingCatalog.Find(SettingCatalog.MaxFailedLogins)!.DefaultValue);
            var lockoutMinutes = int.Parse(SettingCatalog.Find(SettingCatalog.LockoutMinutes)!.DefaultValue);

            user.RegisterFailedLogin(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes));

            await RecordAttemptAsync(user.Id, typed, LoginResult.BadCredentials, "Wrong password", now, cancellationToken);
            throw new UnauthorizedAccessException(Incorrect);
        }

        user.RegisterSuccessfulLogin(now);
        await RecordAttemptAsync(user.Id, typed, LoginResult.Success, null, now, cancellationToken, user.CompanyId);

        return await _sessions.IssueAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Finds the user by company code and username. Both filters are bypassed on purpose: there is no
    /// tenant on the request yet — establishing one is the entire point of logging in.
    /// </summary>
    private async Task<User?> FindUserAsync(LoginName name, CancellationToken cancellationToken)
    {
        var company = await _db.IgnoringTenantFilter<Company>()
            .FirstOrDefaultAsync(c => c.Code == name.CompanyCode && !c.IsDeleted, cancellationToken);

        if (company is null)
        {
            return null;
        }

        var user = await _db.IgnoringTenantFilter<User>()
            .FirstOrDefaultAsync(
                u => u.CompanyId == company.Id && u.Username == name.Username && !u.IsDeleted,
                cancellationToken);

        if (user is not null)
        {
            // Attach the company we already loaded, so the caller can read user.Company without a
            // second round trip — and without an Include that the tenant filter would fight over.
            user.Company = company;
        }

        return user;
    }

    /// <summary>
    /// One message for every credential failure. "No such company", "no such user" and "wrong
    /// password" are the same sentence on purpose: told apart, they are a map of the platform.
    /// </summary>
    private const string Incorrect = "Username or password is incorrect.";

    /// <summary>
    /// A well-formed hash of a password nobody has, in the exact format <c>PasswordHasher</c> emits
    /// (version.iterations.salt.hash). It must parse, or verification would bail out early and the
    /// whole point would be lost: this exists so that a non-existent account costs the same ~210,000
    /// PBKDF2 iterations as a real one with a wrong password.
    /// </summary>
    private const string DummyHash =
        "1.210000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private async Task RecordAttemptAsync(
        Guid? userId,
        string login,
        LoginResult result,
        string? reason,
        DateTimeOffset at,
        CancellationToken cancellationToken,
        Guid? companyId = null)
    {
        _db.LoginHistory.Add(new LoginHistory
        {
            UserId = userId,

            // Verbatim, not normalised. On a failed attempt the whole value of this row is seeing what
            // somebody was probing with.
            Login = login,

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
