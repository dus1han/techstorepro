namespace TechStorePro.Application.Common.Interfaces;

/// <summary>
/// Reads effective-dated configuration (requirements §11).
///
/// Every read is "as of" a moment. A handler pricing an invoice asks for the value in force on the
/// <em>invoice date</em>, not the value in force now — which is the entire mechanism behind General
/// Rule 3, "historical transactions must not change when settings are updated". Re-opening a March
/// invoice in July must not silently re-price it at July's tax rate.
///
/// <see cref="GetAsync{T}"/> without a date means "as of now", which is right for live decisions
/// (a password policy, a session timeout) and wrong for anything that touches a document.
/// </summary>
public interface ISettingsProvider
{
    Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task<T> GetAsOfAsync<T>(string key, DateTimeOffset asOf, CancellationToken cancellationToken = default);

    /// <summary>Branch override, falling back to the company value, falling back to the default.</summary>
    Task<T> GetForBranchAsync<T>(
        string key,
        Guid branchId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a new version: closes the row currently in force and opens a new one from
    /// <paramref name="validFrom"/>. It never updates a value in place — that would rewrite history.
    /// </summary>
    Task SetAsync(
        string key,
        string value,
        Guid? branchId = null,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default);
}
