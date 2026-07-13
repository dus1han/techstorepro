using TechStorePro.Domain.Identity;

namespace TechStorePro.Application.Identity.Dtos;

/// <summary>
/// What a successful login or refresh returns.
///
/// There is no list of companies any more, and no active company to pick from: a user belongs to
/// exactly one, so the switcher this list used to feed has nothing left to switch between.
/// </summary>
public record AuthResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    CurrentUserDto User);

/// <summary>
/// The payload of <c>GET /auth/me</c>: who the caller is, which company they work for, and exactly
/// what they may do. The frontend renders its navigation and buttons from <see cref="Permissions"/> —
/// but every one of them is re-checked server-side.
/// </summary>
/// <param name="CompanyCode">
/// Carried so the UI can remind the user what to type after the <c>@</c> next time they sign in. It is
/// the one piece of the login they cannot look up anywhere else.
/// </param>
public record CurrentUserDto(
    Guid UserId,
    string Username,
    string FullName,
    Guid? CompanyId,
    string? CompanyName,
    string? CompanyCode,
    bool IsOwner,
    IReadOnlyCollection<PermissionDto> Permissions,
    IReadOnlyCollection<Guid> AccessibleBranchIds);

/// <summary>What a platform operator's session returns. No company, because they belong to none.</summary>
public record PlatformAuthResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    PlatformAdminDto Admin);

public record PlatformAdminDto(Guid Id, string Username, string FullName);

public record PermissionDto(string Feature, PermissionAction Action);
