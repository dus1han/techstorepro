using System.Globalization;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Configuration;

/// <summary>
/// Reads and writes effective-dated configuration (requirements §11).
///
/// The write path never updates a value in place. It closes the version currently in force and
/// opens a new one — which is the only way General Rule 3 ("historical transactions must not change
/// when settings are updated") can actually hold. An UPDATE would silently restate every document
/// that had ever read the old value.
/// </summary>
public class SettingsProvider : ISettingsProvider
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public SettingsProvider(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        GetAsOfAsync<T>(key, _clock.UtcNow, cancellationToken);

    public async Task<T> GetAsOfAsync<T>(
        string key,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var value = await ResolveAsync(key, branchId: null, asOf, cancellationToken);

        return Convert<T>(key, value);
    }

    public async Task<T> GetForBranchAsync<T>(
        string key,
        Guid branchId,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var at = asOf ?? _clock.UtcNow;

        // Branch override first, company value second, definition default last. A branch that has
        // never overridden a setting must behave exactly as the company does, not fall to the
        // platform default — otherwise overriding one branch would silently change the others.
        var value = await ResolveAsync(key, branchId, at, cancellationToken)
                    ?? await ResolveAsync(key, branchId: null, at, cancellationToken);

        return Convert<T>(key, value);
    }

    public async Task SetAsync(
        string key,
        string value,
        Guid? branchId = null,
        DateTimeOffset? validFrom = null,
        CancellationToken cancellationToken = default)
    {
        if (SettingCatalog.Find(key) is null)
        {
            throw new ArgumentException($"'{key}' is not a known setting.", nameof(key));
        }

        var from = validFrom ?? _clock.UtcNow;

        var current = await _db.SettingValues
            .Where(v => v.Key == key && v.BranchId == branchId && v.IsActive && v.ValidTo == null)
            .OrderByDescending(v => v.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null)
        {
            // Close the old version at the instant the new one begins, rather than deleting it.
            // The old row is what a document raised last month must still resolve to.
            current.ValidTo = from;
        }

        _db.SettingValues.Add(new SettingValue
        {
            Key = key,
            BranchId = branchId,
            Value = value,
            ValidFrom = from,
            ValidTo = null,
            IsActive = true
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> ResolveAsync(
        string key,
        Guid? branchId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var version = await _db.SettingValues
            .Where(v => v.Key == key
                        && v.BranchId == branchId
                        && v.IsActive
                        && v.ValidFrom <= asOf
                        && (v.ValidTo == null || v.ValidTo > asOf))
            .OrderByDescending(v => v.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

        return version?.Value;
    }

    /// <summary>
    /// Falls back to the definition's default, so a read never fails and no caller has to handle a
    /// missing setting. A key that is not in the catalogue at all is a programming error, and throws.
    /// </summary>
    private static T Convert<T>(string key, string? value)
    {
        var definition = SettingCatalog.Find(key)
            ?? throw new ArgumentException($"'{key}' is not a known setting.", nameof(key));

        var raw = value ?? definition.DefaultValue;

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (target == typeof(string))
        {
            return (T)(object)raw;
        }

        if (target == typeof(bool))
        {
            return (T)(object)bool.Parse(raw);
        }

        if (target == typeof(int))
        {
            return (T)(object)int.Parse(raw, CultureInfo.InvariantCulture);
        }

        if (target == typeof(long))
        {
            return (T)(object)long.Parse(raw, CultureInfo.InvariantCulture);
        }

        if (target == typeof(decimal))
        {
            return (T)(object)decimal.Parse(raw, CultureInfo.InvariantCulture);
        }

        throw new NotSupportedException($"Settings cannot be read as {typeof(T).Name}.");
    }
}
