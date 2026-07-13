using TechStorePro.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

// Money is numeric(18,4) and quantities numeric(18,4) throughout — database-design.md §2. Never a
// float: 0.1 + 0.2 is the one bug an ERP genuinely cannot ship.
internal static class CatalogTypes
{
    public const string Money = "numeric(18,4)";
    public const string Quantity = "numeric(18,4)";
    public const string Percent = "numeric(9,4)";
}

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("product_categories");

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.HasIndex(c => new { c.CompanyId, c.Name });

        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            // Restrict, not Cascade: deleting "Laptops" must not silently take every sub-category
            // and orphan the products under them.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");

        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.DeletedReason).HasMaxLength(500);

        builder.HasIndex(b => new { b.CompanyId, b.Name }).IsUnique();
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.Property(p => p.ItemCode).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Barcode).HasMaxLength(50);
        builder.Property(p => p.Name).HasMaxLength(300).IsRequired();
        builder.Property(p => p.Model).HasMaxLength(100);
        builder.Property(p => p.Unit).HasMaxLength(20).IsRequired();
        builder.Property(p => p.DeletedReason).HasMaxLength(500);

        builder.Property(p => p.Kind).HasConversion<short>();
        builder.Property(p => p.Condition).HasConversion<short>();
        builder.Property(p => p.TrackingMode).HasConversion<short>();

        builder.Property(p => p.PurchasePrice).HasColumnType(CatalogTypes.Money);
        builder.Property(p => p.SellingPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(p => p.ReorderLevel).HasColumnType(CatalogTypes.Quantity);

        // Specs vary wildly by category (a laptop has a CPU, a cable has a length), so they are
        // jsonb rather than forty mostly-null columns.
        builder.Property(p => p.Specifications).HasColumnType("jsonb");

        // Computed in C#; never persisted, so a stale margin cannot disagree with the prices.
        builder.Ignore(p => p.DefaultMarginPercent);

        // Unique per company, filtered on is_deleted: retiring a product must free its SKU for
        // reuse, or a shop that mistypes a code can never use the correct one.
        builder.HasIndex(p => new { p.CompanyId, p.Sku })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(p => new { p.CompanyId, p.ItemCode })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // The counter scans a barcode; this is the index that lookup rides. Not unique — two
        // products legitimately share a manufacturer barcode when one is a refurbished twin.
        builder.HasIndex(p => new { p.CompanyId, p.Barcode });

        builder.HasIndex(p => new { p.CompanyId, p.CategoryId });

        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Brand).WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.TaxRate).WithMany().HasForeignKey(p => p.TaxRateId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(300).IsRequired();
        builder.Property(c => c.CompanyName).HasMaxLength(300);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.TaxNumber).HasMaxLength(50);
        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.Property(c => c.Type).HasConversion<short>();
        builder.Property(c => c.CreditLimit).HasColumnType(CatalogTypes.Money);
        builder.Property(c => c.Balance).HasColumnType(CatalogTypes.Money);

        builder.HasIndex(c => new { c.CompanyId, c.Code })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(c => new { c.CompanyId, c.Name });
        builder.HasIndex(c => new { c.CompanyId, c.Phone });

        builder.HasOne(c => c.PriceTier).WithMany().HasForeignKey(c => c.PriceTierId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(300).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(256);
        builder.Property(s => s.Phone).HasMaxLength(50);
        builder.Property(s => s.Country).HasMaxLength(100);
        builder.Property(s => s.TaxNumber).HasMaxLength(50);
        builder.Property(s => s.DefaultCurrency).HasMaxLength(3).IsRequired();
        builder.Property(s => s.DeletedReason).HasMaxLength(500);

        builder.Property(s => s.Type).HasConversion<short>();
        builder.Property(s => s.Balance).HasColumnType(CatalogTypes.Money);

        builder.HasIndex(s => new { s.CompanyId, s.Code })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(s => new { s.CompanyId, s.Name });
    }
}

public class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.ToTable("tax_rates");

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Percent).HasColumnType(CatalogTypes.Percent);
        builder.Property(t => t.DeletedReason).HasMaxLength(500);

        // "Which rate was in force on the invoice date?" is the query; this is its index.
        builder.HasIndex(t => new { t.CompanyId, t.ValidFrom });
    }
}

public class PriceTierConfiguration : IEntityTypeConfiguration<PriceTier>
{
    public void Configure(EntityTypeBuilder<PriceTier> builder)
    {
        builder.ToTable("price_tiers");

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.DeletedReason).HasMaxLength(500);

        builder.HasIndex(t => new { t.CompanyId, t.Name }).IsUnique();
    }
}

public class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("price_lists");

        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.HasIndex(l => new { l.CompanyId, l.PriceTierId, l.ValidFrom });

        builder.HasOne(l => l.PriceTier)
            .WithMany(t => t.PriceLists)
            .HasForeignKey(l => l.PriceTierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> builder)
    {
        builder.ToTable("price_list_items");

        builder.Property(i => i.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(i => i.MinimumPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(i => i.DeletedReason).HasMaxLength(500);

        // One price per product per list. Two would make "what does this cost?" ambiguous.
        builder.HasIndex(i => new { i.PriceListId, i.ProductId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasOne(i => i.PriceList)
            .WithMany(l => l.Items)
            .HasForeignKey(i => i.PriceListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PriceHistoryConfiguration : IEntityTypeConfiguration<PriceHistory>
{
    public void Configure(EntityTypeBuilder<PriceHistory> builder)
    {
        builder.ToTable("price_history");

        builder.Property(h => h.Kind).HasConversion<short>();
        builder.Property(h => h.OldPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(h => h.NewPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(h => h.DeletedReason).HasMaxLength(500);

        builder.HasIndex(h => new { h.CompanyId, h.ProductId, h.ChangedAt })
            .IsDescending(false, false, true);

        builder.HasOne(h => h.Product).WithMany().HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.ToTable("discounts");

        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Method).HasConversion<short>();
        builder.Property(d => d.Value).HasColumnType(CatalogTypes.Money);
        builder.Property(d => d.MaxValue).HasColumnType(CatalogTypes.Money);
        builder.Property(d => d.DeletedReason).HasMaxLength(500);

        builder.HasIndex(d => new { d.CompanyId, d.ProductId });
        builder.HasIndex(d => new { d.CompanyId, d.CustomerId });

        builder.HasOne(d => d.Product).WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods");

        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Kind).HasConversion<short>();
        builder.Property(m => m.DeletedReason).HasMaxLength(500);

        builder.HasIndex(m => new { m.CompanyId, m.Name })
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}

public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies");

        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasMaxLength(3);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Symbol).HasMaxLength(10);
    }
}

public class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.ToTable("fx_rates");

        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(r => r.RateToBase).HasColumnType("numeric(18,8)");
        builder.Property(r => r.DeletedReason).HasMaxLength(500);

        // One rate per currency per day. A second would make the day's conversions depend on
        // insertion order.
        builder.HasIndex(r => new { r.CompanyId, r.CurrencyCode, r.RateDate })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasOne(r => r.Currency).WithMany().HasForeignKey(r => r.CurrencyCode).OnDelete(DeleteBehavior.Restrict);
    }
}
