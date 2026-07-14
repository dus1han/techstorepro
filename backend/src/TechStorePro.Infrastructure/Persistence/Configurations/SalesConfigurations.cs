using TechStorePro.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("quotations");

        builder.Property(q => q.Number).HasMaxLength(50).IsRequired();
        builder.Property(q => q.Status).HasConversion<short>();
        builder.Property(q => q.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(q => q.Notes).HasMaxLength(1000);
        builder.Property(q => q.DeletedReason).HasMaxLength(500);

        // Computed in C# from SalesMath, so there is no column and no SQL equivalent. See the note at
        // the top of SalesQueries.cs.
        builder.Ignore(q => q.NetTotal);
        builder.Ignore(q => q.TaxTotal);
        builder.Ignore(q => q.Total);

        builder.HasIndex(q => new { q.CompanyId, q.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(q => new { q.CompanyId, q.CustomerId, q.QuotedAt });
        builder.HasIndex(q => new { q.CompanyId, q.Status });

        // Nullable: a quotation may be raised for a walk-in enquiry with nobody on file yet.
        builder.HasOne(q => q.Customer).WithMany().HasForeignKey(q => q.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.Branch).WithMany().HasForeignKey(q => q.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class QuotationLineConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> builder)
    {
        builder.ToTable("quotation_lines");

        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DiscountPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.DiscountAmount).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.TaxPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.PriceSource).HasMaxLength(200);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.NetTotal);
        builder.Ignore(l => l.TaxAmount);
        builder.Ignore(l => l.LineTotal);

        builder.HasIndex(l => new { l.CompanyId, l.QuotationId });

        builder.HasOne(l => l.Quotation).WithMany(q => q.Lines)
            .HasForeignKey(l => l.QuotationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("sales_orders");

        builder.Property(o => o.Number).HasMaxLength(50).IsRequired();
        builder.Property(o => o.Status).HasConversion<short>();
        builder.Property(o => o.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.DeletedReason).HasMaxLength(500);

        builder.Ignore(o => o.NetTotal);
        builder.Ignore(o => o.TaxTotal);
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.IsFullyDelivered);

        builder.HasIndex(o => new { o.CompanyId, o.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(o => new { o.CompanyId, o.CustomerId, o.OrderedAt });
        builder.HasIndex(o => new { o.CompanyId, o.Status });

        builder.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Branch).WithMany().HasForeignKey(o => o.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Warehouse).WithMany().HasForeignKey(o => o.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Quotation).WithMany().HasForeignKey(o => o.QuotationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("sales_order_lines");

        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.DeliveredQuantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DiscountPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.DiscountAmount).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.TaxPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.PriceSource).HasMaxLength(200);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.NetTotal);
        builder.Ignore(l => l.TaxAmount);
        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.OutstandingQuantity);

        builder.HasIndex(l => new { l.CompanyId, l.SalesOrderId });
        builder.HasIndex(l => new { l.CompanyId, l.ProductId });

        builder.HasOne(l => l.SalesOrder).WithMany(o => o.Lines)
            .HasForeignKey(l => l.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // No navigation to the reservation, deliberately. Reservations belong to the ledger, and a
        // navigation property here would be an invitation for a handler to modify one directly — which
        // is precisely what IStockLedger exists to prevent (architecture.md §4.5).
    }
}

public class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries");

        builder.Property(d => d.Number).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Status).HasConversion<short>();
        builder.Property(d => d.DeliveredTo).HasMaxLength(200);
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.Property(d => d.DeletedReason).HasMaxLength(500);

        builder.Ignore(d => d.CostTotal);

        builder.HasIndex(d => new { d.CompanyId, d.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(d => new { d.CompanyId, d.CustomerId, d.DeliveredAt });
        builder.HasIndex(d => new { d.CompanyId, d.SalesOrderId });
        builder.HasIndex(d => new { d.CompanyId, d.Status });

        builder.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Branch).WithMany().HasForeignKey(d => d.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Warehouse).WithMany().HasForeignKey(d => d.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable: the counter sale has no order behind it (§25's reasoning, applied to sales).
        builder.HasOne(d => d.SalesOrder).WithMany().HasForeignKey(d => d.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DeliveryLineConfiguration : IEntityTypeConfiguration<DeliveryLine>
{
    public void Configure(EntityTypeBuilder<DeliveryLine> builder)
    {
        builder.ToTable("delivery_lines");

        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitCost).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.Notes).HasMaxLength(500);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.CostTotal);

        builder.HasIndex(l => new { l.CompanyId, l.DeliveryId });
        builder.HasIndex(l => new { l.CompanyId, l.ProductId });

        builder.HasOne(l => l.Delivery).WithMany(d => d.Lines)
            .HasForeignKey(l => l.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.SalesOrderLine).WithMany()
            .HasForeignKey(l => l.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DeliverySerialConfiguration : IEntityTypeConfiguration<DeliverySerial>
{
    public void Configure(EntityTypeBuilder<DeliverySerial> builder)
    {
        builder.ToTable("delivery_serials");

        builder.Property(s => s.SerialNumber).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DeletedReason).HasMaxLength(500);

        builder.HasIndex(s => new { s.CompanyId, s.DeliveryLineId });

        // The same machine cannot go out of the door twice. The serial state machine already refuses to
        // move a Sold unit, so this is belt and braces — but it is the constraint an auditor can read
        // without running the code.
        builder.HasIndex(s => new { s.CompanyId, s.SerialId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasOne(s => s.DeliveryLine).WithMany(l => l.Serials)
            .HasForeignKey(s => s.DeliveryLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.ToTable("sales_invoices");

        builder.Property(i => i.Number).HasMaxLength(50).IsRequired();
        builder.Property(i => i.Status).HasConversion<short>();
        builder.Property(i => i.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(1000);
        builder.Property(i => i.DeletedReason).HasMaxLength(500);

        builder.Ignore(i => i.NetTotal);
        builder.Ignore(i => i.TaxTotal);
        builder.Ignore(i => i.Total);
        builder.Ignore(i => i.CostTotal);
        builder.Ignore(i => i.GrossProfit);
        builder.Ignore(i => i.PaidAmount);
        builder.Ignore(i => i.OutstandingAmount);
        builder.Ignore(i => i.IsSettled);

        builder.HasIndex(i => new { i.CompanyId, i.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // One delivery, one invoice. Billing the same goods twice would double the customer's debt and
        // double the revenue, and the second invoice would look exactly as legitimate as the first — so
        // the database refuses it, not just the handler.
        builder.HasIndex(i => new { i.CompanyId, i.DeliveryId })
            .IsUnique()
            .HasFilter("is_deleted = false AND delivery_id IS NOT NULL");

        builder.HasIndex(i => new { i.CompanyId, i.CustomerId, i.InvoicedAt });
        builder.HasIndex(i => new { i.CompanyId, i.Status, i.DueAt });

        builder.HasOne(i => i.Customer).WithMany().HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Branch).WithMany().HasForeignKey(i => i.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.SalesOrder).WithMany().HasForeignKey(i => i.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Delivery).WithMany().HasForeignKey(i => i.DeliveryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerPaymentConfiguration : IEntityTypeConfiguration<CustomerPayment>
{
    public void Configure(EntityTypeBuilder<CustomerPayment> builder)
    {
        builder.ToTable("customer_payments");

        builder.Property(p => p.Number).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.DeletedReason).HasMaxLength(500);

        // There is no Amount column: the total is the sum of the tender. A header amount that could
        // disagree with its method lines would be a till that does not balance.
        builder.Ignore(p => p.Amount);
        builder.Ignore(p => p.AllocatedAmount);
        builder.Ignore(p => p.UnallocatedAmount);

        builder.HasIndex(p => new { p.CompanyId, p.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(p => new { p.CompanyId, p.CustomerId, p.PaidAt });

        builder.HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Branch).WithMany().HasForeignKey(p => p.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerPaymentMethodConfiguration : IEntityTypeConfiguration<CustomerPaymentMethod>
{
    public void Configure(EntityTypeBuilder<CustomerPaymentMethod> builder)
    {
        builder.ToTable("customer_payment_methods");

        builder.Property(m => m.Amount).HasColumnType(CatalogTypes.Money);
        builder.Property(m => m.Reference).HasMaxLength(100);
        builder.Property(m => m.DeletedReason).HasMaxLength(500);

        builder.HasIndex(m => new { m.CompanyId, m.CustomerPaymentId });

        builder.HasOne(m => m.CustomerPayment).WithMany(p => p.Methods)
            .HasForeignKey(m => m.CustomerPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.PaymentMethod).WithMany().HasForeignKey(m => m.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CustomerPaymentAllocationConfiguration : IEntityTypeConfiguration<CustomerPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<CustomerPaymentAllocation> builder)
    {
        builder.ToTable("customer_payment_allocations");

        builder.Property(a => a.Amount).HasColumnType(CatalogTypes.Money);
        builder.Property(a => a.DeletedReason).HasMaxLength(500);

        // One payment settles many invoices; one invoice is settled by many payments. But the same
        // payment must not be allocated to the same invoice twice — that would double-count the money.
        builder.HasIndex(a => new { a.CompanyId, a.CustomerPaymentId, a.SalesInvoiceId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(a => new { a.CompanyId, a.SalesInvoiceId });

        builder.HasOne(a => a.CustomerPayment).WithMany(p => p.Allocations)
            .HasForeignKey(a => a.CustomerPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.SalesInvoice).WithMany(i => i.Allocations)
            .HasForeignKey(a => a.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> builder)
    {
        builder.ToTable("sales_invoice_lines");

        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DiscountPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.DiscountAmount).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.TaxPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.UnitCost).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.PriceSource).HasMaxLength(200);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.NetTotal);
        builder.Ignore(l => l.TaxAmount);
        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.CostTotal);
        builder.Ignore(l => l.GrossProfit);

        builder.HasIndex(l => new { l.CompanyId, l.SalesInvoiceId });
        builder.HasIndex(l => new { l.CompanyId, l.ProductId });

        builder.HasOne(l => l.SalesInvoice).WithMany(i => i.Lines)
            .HasForeignKey(l => l.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.DeliveryLine).WithMany()
            .HasForeignKey(l => l.DeliveryLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
