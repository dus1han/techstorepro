using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Services;
using TechStorePro.Domain.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Commands.SwitchCompany;

/// <summary>
/// Re-issues the session against another company the caller belongs to.
///
/// This is an <em>auth</em> operation, not a request parameter, because the active company lives in
/// a signed claim. If switching company were a header or a query string, any authenticated user
/// could read any company's data by editing the request — the tenant boundary would be advisory.
/// </summary>
public record SwitchCompanyCommand(Guid CompanyId) : IRequest<AuthResult>;

public class SwitchCompanyCommandValidator : AbstractValidator<SwitchCompanyCommand>
{
    public SwitchCompanyCommandValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}

public class SwitchCompanyCommandHandler : IRequestHandler<SwitchCompanyCommand, AuthResult>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuthSessionFactory _sessions;

    public SwitchCompanyCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IAuthSessionFactory sessions)
    {
        _db = db;
        _currentUser = currentUser;
        _sessions = sessions;
    }

    public async Task<AuthResult> Handle(SwitchCompanyCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("Authentication is required.");

        // Deliberately bypasses the tenant filter: the caller's token still names the *old* company,
        // so a filtered query could never find their membership of the new one. The membership check
        // immediately below is what keeps this safe — it is the only thing standing between a user
        // and any company id they care to type, so it is not optional.
        var membership = await _db.IgnoringTenantFilter<CompanyUser>()
            .Include(m => m.Company)
            .FirstOrDefaultAsync(
                m => m.UserId == userId
                     && m.CompanyId == request.CompanyId
                     && m.IsActive
                     && !m.IsDeleted,
                cancellationToken);

        if (membership is null || membership.Company is not { IsActive: true, IsDeleted: false })
        {
            throw new UnauthorizedAccessException("You are not a member of that company.");
        }

        return await _sessions.IssueAsync(userId, request.CompanyId, cancellationToken);
    }
}
