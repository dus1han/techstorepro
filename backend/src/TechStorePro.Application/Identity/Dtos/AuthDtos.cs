using TechStorePro.Domain.Identity;

namespace TechStorePro.Application.Identity.Dtos;

/// <summary>What a successful login, refresh or company switch returns.</summary>
public record AuthResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    CurrentUserDto User,
    IReadOnlyCollection<CompanyMembershipDto> Companies);

public record CompanyMembershipDto(
    Guid CompanyId,
    string CompanyName,
    bool IsDefault,
    bool IsOwner);

/// <summary>
/// The payload of <c>GET /auth/me</c>: who the caller is, which company they are acting in, and
/// exactly what they may do. The frontend renders its navigation and buttons from
/// <see cref="Permissions"/> — but every one of them is re-checked server-side.
/// </summary>
public record CurrentUserDto(
    Guid UserId,
    string Email,
    string FullName,
    Guid? ActiveCompanyId,
    string? ActiveCompanyName,
    bool IsOwner,
    IReadOnlyCollection<PermissionDto> Permissions,
    IReadOnlyCollection<Guid> AccessibleBranchIds);

public record PermissionDto(string Feature, PermissionAction Action);
