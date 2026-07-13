using System.Text;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Infrastructure.Persistence;

/// <summary>
/// Renames every table, column, key, index and foreign key to snake_case.
///
/// database-design.md §2 mandates snake_case throughout, and EF's default is the CLR name — which
/// would give us <c>"CompanyId"</c>, quoted and case-sensitive, in a database where every other
/// identifier is unquoted lowercase. That mismatch is not cosmetic: <c>select company_id from
/// companies</c> typed by hand in psql would simply fail, and every raw SQL statement in the system
/// (the FOR UPDATE lock in DocumentNumberGenerator, the stock-ledger queries to come) would need
/// quoted PascalCase identifiers to work.
///
/// Applied centrally rather than by naming each column in each configuration, because eighty tables
/// of hand-written <c>HasColumnName</c> is eighty chances to forget one.
/// </summary>
public static class SnakeCaseNamingConvention
{
    public static void ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Table names are already set explicitly by each configuration (ToTable("companies")),
            // but an entity without one still gets converted here.
            var tableName = entity.GetTableName();
            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    /// <summary>
    /// PascalCase / camelCase to snake_case. Runs of capitals stay together, so <c>IpAddress</c>
    /// becomes <c>ip_address</c> and <c>OldValuesJSON</c> would become <c>old_values_json</c> rather
    /// than <c>old_values_j_s_o_n</c>.
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                var isStart = i == 0;
                var previousIsLower = !isStart && char.IsLower(name[i - 1]);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                var previousIsUpper = !isStart && char.IsUpper(name[i - 1]);

                // Underscore before a capital that starts a new word: after a lowercase letter
                // (Company|Id), or at the end of a run of capitals followed by a word (IP|Address).
                if (!isStart && (previousIsLower || (previousIsUpper && nextIsLower)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
