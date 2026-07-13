using TechStorePro.Domain.Auditing;
using TechStorePro.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

public class SettingDefinitionConfiguration : IEntityTypeConfiguration<SettingDefinition>
{
    public void Configure(EntityTypeBuilder<SettingDefinition> builder)
    {
        builder.ToTable("setting_definitions");

        builder.HasKey(d => d.Key);
        builder.Property(d => d.Key).HasMaxLength(100);
        builder.Property(d => d.Module).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.Property(d => d.DefaultValue).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.DataType).HasConversion<short>();
        builder.Property(d => d.Scope).HasConversion<short>();
    }
}

public class SettingValueConfiguration : IEntityTypeConfiguration<SettingValue>
{
    public void Configure(EntityTypeBuilder<SettingValue> builder)
    {
        builder.ToTable("setting_values");

        builder.Property(v => v.Key).HasMaxLength(100).IsRequired();
        builder.Property(v => v.Value).HasMaxLength(4000).IsRequired();
        builder.Property(v => v.DeletedReason).HasMaxLength(500);

        // The resolution query is "this key, this branch (or company-wide), in force at this
        // instant" — so that is the index. ValidFrom descending because the newest in-force version
        // is the one wanted, and it is found first.
        builder.HasIndex(v => new { v.CompanyId, v.Key, v.BranchId, v.ValidFrom })
            .HasDatabaseName("ix_setting_values_lookup")
            .IsDescending(false, false, false, true);
    }
}

public class DocumentNumberSequenceConfiguration : IEntityTypeConfiguration<DocumentNumberSequence>
{
    public void Configure(EntityTypeBuilder<DocumentNumberSequence> builder)
    {
        builder.ToTable("document_number_sequences");

        builder.Property(s => s.Prefix).HasMaxLength(10).IsRequired();
        builder.Property(s => s.DocumentType).HasConversion<short>();
        builder.Property(s => s.DeletedReason).HasMaxLength(500);

        // One counter per company + branch + type + year. Unique because two counters for the same
        // key would issue the same number twice, which is precisely what "gapless" forbids.
        builder.HasIndex(s => new { s.CompanyId, s.BranchId, s.DocumentType, s.Year }).IsUnique();
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasConversion<short>();
        builder.Property(a => a.Username).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.Summary).HasMaxLength(1000);

        // jsonb, not text: the "what changed" question gets asked with a filter on the payload
        // often enough that a scannable type pays for itself.
        builder.Property(a => a.OldValues).HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnType("jsonb");

        // The activity timeline of requirements §9: "everything that happened to this record".
        builder.HasIndex(a => new { a.CompanyId, a.EntityType, a.EntityId, a.At })
            .IsDescending(false, false, false, true);

        builder.HasIndex(a => new { a.CompanyId, a.At }).IsDescending(false, true);
    }
}
