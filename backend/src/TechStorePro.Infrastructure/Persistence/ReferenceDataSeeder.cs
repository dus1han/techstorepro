using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TechStorePro.Infrastructure.Persistence;

/// <summary>
/// Keeps the <c>features</c> and <c>setting_definitions</c> tables in step with the code catalogues
/// they mirror. Runs at startup, after migrations.
///
/// These are reference data, not tenant data: a company grants permissions <em>over</em> features
/// and sets <em>values</em> for settings, but neither invents the catalogue. Seeding from code means
/// adding a feature is a code change plus a restart, not a hand-written migration that someone
/// forgets on one environment.
///
/// The sync is additive and update-only. It never deletes a row whose code has disappeared from the
/// catalogue: a stale feature row is harmless, whereas cascading it away would take every grant that
/// referenced it — and doing that silently, at startup, because someone renamed a constant, is how a
/// deploy quietly strips a company's permissions.
/// </summary>
public class ReferenceDataSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReferenceDataSeeder> _logger;

    public ReferenceDataSeeder(
        ApplicationDbContext db,
        IPasswordHasher hasher,
        IConfiguration configuration,
        ILogger<ReferenceDataSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedFeaturesAsync(cancellationToken);
        await SeedSettingDefinitionsAsync(cancellationToken);
        await SeedCurrenciesAsync(cancellationToken);
        await SeedFirstPlatformAdminAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the very first platform operator, from configuration, if there are none at all.
    ///
    /// <b>This exists because the system now has a bootstrap problem.</b> Companies are onboarded by a
    /// platform admin, and platform admins are created by other platform admins — so with an empty
    /// table, nobody can create anybody, and the product cannot be used at all. Something has to make
    /// the first one, and configuration is the least-bad place: it is the one input that is already
    /// trusted at start-up and already comes from a secret store in a real environment.
    ///
    /// It runs only when the table is <em>empty</em>. It is not an upsert and it will not resurrect a
    /// deleted admin or reset a password — a seeder that quietly rewrote a live credential on every
    /// deploy would be a backdoor with a changelog entry.
    /// </summary>
    private async Task SeedFirstPlatformAdminAsync(CancellationToken cancellationToken)
    {
        if (await _db.PlatformAdmins.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            return;
        }

        var section = _configuration.GetSection("Platform:FirstAdmin");

        var username = section["Username"];
        var password = section["Password"];
        var fullName = section["FullName"] ?? "Platform Administrator";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "There is no platform administrator and none is configured, so no company can be "
                + "onboarded. Set Platform:FirstAdmin:Username and Platform:FirstAdmin:Password "
                + "(via user-secrets or the environment) and restart.");
            return;
        }

        if (password.Length < 12)
        {
            // Refuse rather than create a weak one. This account can reach every company on the
            // platform; a short password on it is not a small problem.
            throw new InvalidOperationException(
                "Platform:FirstAdmin:Password must be at least 12 characters. This account can reach "
                + "every company on the platform.");
        }

        _db.PlatformAdmins.Add(new PlatformAdmin
        {
            Username = User.NormaliseUsername(username),
            FullName = fullName,
            Email = section["Email"],
            PasswordHash = _hasher.Hash(password),
            IsActive = true,

            // Whoever put it in configuration knows it, and configuration is not where a live
            // credential should keep living.
            MustChangePassword = true
        });

        _logger.LogWarning(
            "Created the first platform administrator '{Username}' from configuration. Change its "
            + "password and remove it from configuration.",
            username);
    }

    /// <summary>
    /// ISO 4217. Not tenant-scoped: the dirham is the dirham for every company, and a per-company
    /// copy of the currency list would be a hundred rows all saying the same thing.
    ///
    /// A starter set rather than the full ISO table — the currencies a Gulf computer importer
    /// actually trades in. More can be added without a migration.
    /// </summary>
    private async Task SeedCurrenciesAsync(CancellationToken cancellationToken)
    {
        Currency[] currencies =
        [
            new() { Code = "AED", Name = "UAE Dirham", Symbol = "د.إ", DecimalPlaces = 2 },
            new() { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2 },
            new() { Code = "EUR", Name = "Euro", Symbol = "€", DecimalPlaces = 2 },
            new() { Code = "GBP", Name = "Pound Sterling", Symbol = "£", DecimalPlaces = 2 },
            new() { Code = "SAR", Name = "Saudi Riyal", Symbol = "﷼", DecimalPlaces = 2 },
            new() { Code = "CNY", Name = "Chinese Yuan", Symbol = "¥", DecimalPlaces = 2 },
            new() { Code = "INR", Name = "Indian Rupee", Symbol = "₹", DecimalPlaces = 2 },
            // Zero decimal places, and not a typo: ¥1,000 is one thousand yen, not ten. Treating it
            // as a two-decimal currency divides every yen amount by a hundred somewhere downstream.
            new() { Code = "JPY", Name = "Japanese Yen", Symbol = "¥", DecimalPlaces = 0 }
        ];

        var existing = await _db.Currencies.Select(c => c.Code).ToListAsync(cancellationToken);

        foreach (var currency in currencies.Where(c => !existing.Contains(c.Code)))
        {
            _db.Currencies.Add(currency);
            _logger.LogInformation("Seeded currency {CurrencyCode}", currency.Code);
        }
    }

    private async Task SeedFeaturesAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.Features.ToDictionaryAsync(f => f.Code, cancellationToken);

        foreach (var feature in FeatureCatalog.All)
        {
            if (existing.TryGetValue(feature.Code, out var row))
            {
                row.Module = feature.Module;
                row.Name = feature.Name;
                row.DisplayOrder = feature.DisplayOrder;
                row.SupportedActions = feature.SupportedActions;
                continue;
            }

            _db.Features.Add(new Feature
            {
                Code = feature.Code,
                Module = feature.Module,
                Name = feature.Name,
                DisplayOrder = feature.DisplayOrder,
                SupportedActions = feature.SupportedActions
            });

            _logger.LogInformation("Seeded new feature {FeatureCode}", feature.Code);
        }
    }

    private async Task SeedSettingDefinitionsAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.SettingDefinitions.ToDictionaryAsync(d => d.Key, cancellationToken);

        foreach (var definition in SettingCatalog.All)
        {
            if (existing.TryGetValue(definition.Key, out var row))
            {
                row.Module = definition.Module;
                row.Name = definition.Name;
                row.Description = definition.Description;
                row.DataType = definition.DataType;
                row.Scope = definition.Scope;

                // The default is deliberately NOT overwritten on an existing row. A company that has
                // never set a value reads the default, so changing it here would silently change
                // that company's behaviour on deploy — a settings change nobody made and nobody sees.
                continue;
            }

            _db.SettingDefinitions.Add(new SettingDefinition
            {
                Key = definition.Key,
                Module = definition.Module,
                Name = definition.Name,
                Description = definition.Description,
                DataType = definition.DataType,
                Scope = definition.Scope,
                DefaultValue = definition.DefaultValue
            });

            _logger.LogInformation("Seeded new setting {SettingKey}", definition.Key);
        }
    }
}
