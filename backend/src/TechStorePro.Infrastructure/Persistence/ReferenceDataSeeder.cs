using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<ReferenceDataSeeder> _logger;

    public ReferenceDataSeeder(ApplicationDbContext db, ILogger<ReferenceDataSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedFeaturesAsync(cancellationToken);
        await SeedSettingDefinitionsAsync(cancellationToken);
        await SeedCurrenciesAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
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
