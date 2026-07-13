using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.LegalName).HasMaxLength(200);
        builder.Property(c => c.TaxNumber).HasMaxLength(50);
        builder.Property(c => c.RegistrationNumber).HasMaxLength(50);
        builder.Property(c => c.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Country).HasMaxLength(100);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.Website).HasMaxLength(256);
        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.HasIndex(c => c.IsActive);
    }
}

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");

        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Phone).HasMaxLength(50);
        builder.Property(b => b.Email).HasMaxLength(256);
        builder.Property(b => b.DeletedReason).HasMaxLength(500);

        // Tenant column leads every index (database-design.md §2): a query is scoped before it is
        // filtered, so the index is usable rather than merely present.
        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();

        builder.HasOne(b => b.DefaultWarehouse)
            .WithMany()
            .HasForeignKey(b => b.DefaultWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.OwnedWarehouses)
            .WithOne(w => w.Branch!)
            .HasForeignKey(w => w.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");

        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Code).HasMaxLength(20).IsRequired();
        builder.Property(w => w.Type).HasConversion<short>();
        builder.Property(w => w.DeletedReason).HasMaxLength(500);

        builder.HasIndex(w => new { w.CompanyId, w.Code }).IsUnique();

        // Null BranchId = company-shared. Indexed because "which warehouses does this branch own?"
        // is asked on every stock screen.
        builder.HasIndex(w => new { w.CompanyId, w.BranchId });
    }
}

public class BranchWarehouseConfiguration : IEntityTypeConfiguration<BranchWarehouse>
{
    public void Configure(EntityTypeBuilder<BranchWarehouse> builder)
    {
        builder.ToTable("branch_warehouses");

        // Hard-deleted join row, so no soft-delete filter is needed on this unique index.
        builder.HasIndex(bw => new { bw.CompanyId, bw.BranchId, bw.WarehouseId }).IsUnique();

        builder.HasOne(bw => bw.Branch)
            .WithMany(b => b.SharedWarehouseAccess)
            .HasForeignKey(bw => bw.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(bw => bw.Warehouse)
            .WithMany(w => w.AccessibleToBranches)
            .HasForeignKey(bw => bw.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        // citext: "Ali@Shop.ae" and "ali@shop.ae" are the same person, and a case-sensitive unique
        // index would happily let both exist.
        builder.Property(u => u.Email).HasColumnType("citext").HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Phone).HasMaxLength(50);
        builder.Property(u => u.DeletedReason).HasMaxLength(500);

        // Unique across the whole platform, not per company — users are global by design.
        builder.HasIndex(u => u.Email).IsUnique();
    }
}

public class CompanyUserConfiguration : IEntityTypeConfiguration<CompanyUser>
{
    public void Configure(EntityTypeBuilder<CompanyUser> builder)
    {
        builder.ToTable("company_users");

        builder.Property(cu => cu.DeletedReason).HasMaxLength(500);

        builder.HasIndex(cu => new { cu.CompanyId, cu.UserId }).IsUnique();
        builder.HasIndex(cu => cu.UserId);

        builder.HasOne(cu => cu.Company)
            .WithMany(c => c.Members)
            .HasForeignKey(cu => cu.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cu => cu.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(cu => cu.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CompanyUserBranchConfiguration : IEntityTypeConfiguration<CompanyUserBranch>
{
    public void Configure(EntityTypeBuilder<CompanyUserBranch> builder)
    {
        builder.ToTable("company_user_branches");

        builder.HasIndex(cub => new { cub.CompanyUserId, cub.BranchId }).IsUnique();

        builder.HasOne(cub => cub.CompanyUser)
            .WithMany(cu => cu.BranchAccess)
            .HasForeignKey(cub => cub.CompanyUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cub => cub.Branch)
            .WithMany()
            .HasForeignKey(cub => cub.BranchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("user_permissions");

        builder.Property(p => p.FeatureCode).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Action).HasConversion<short>();

        // One row per (member, feature, action). The uniqueness is what makes a grant idempotent —
        // ticking a box twice must not create two grants that then disagree when one is revoked.
        // Safe as an unfiltered unique index only because grants are hard-deleted: a soft-deleted
        // row would keep occupying this key and block the permission from ever being re-granted.
        builder.HasIndex(p => new { p.CompanyUserId, p.FeatureCode, p.Action }).IsUnique();

        builder.HasOne(p => p.CompanyUser)
            .WithMany(cu => cu.Permissions)
            .HasForeignKey(p => p.CompanyUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.DeviceInfo).HasMaxLength(500);
        builder.Property(t => t.IpAddress).HasMaxLength(64);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.CompanyId });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LoginHistoryConfiguration : IEntityTypeConfiguration<LoginHistory>
{
    public void Configure(EntityTypeBuilder<LoginHistory> builder)
    {
        builder.ToTable("login_history");

        builder.Property(h => h.Email).HasColumnType("citext").HasMaxLength(256).IsRequired();
        builder.Property(h => h.Result).HasConversion<short>();
        builder.Property(h => h.FailureReason).HasMaxLength(200);
        builder.Property(h => h.IpAddress).HasMaxLength(64);
        builder.Property(h => h.UserAgent).HasMaxLength(500);
        builder.Property(h => h.DeviceInfo).HasMaxLength(500);

        builder.HasIndex(h => new { h.Email, h.At });
        builder.HasIndex(h => new { h.UserId, h.At });

        builder.HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class FeatureConfiguration : IEntityTypeConfiguration<Feature>
{
    public void Configure(EntityTypeBuilder<Feature> builder)
    {
        builder.ToTable("features");

        builder.HasKey(f => f.Code);
        builder.Property(f => f.Code).HasMaxLength(100);
        builder.Property(f => f.Module).HasMaxLength(50).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();

        // The permission grid renders these; storing them as smallint[] keeps one row per feature
        // instead of a join table whose only content is "this action is legal here".
        builder.Property(f => f.SupportedActions)
            .HasConversion(
                actions => actions.Select(a => (short)a).ToArray(),
                values => values.Select(v => (PermissionAction)v).ToArray())
            .HasColumnType("smallint[]");
    }
}
