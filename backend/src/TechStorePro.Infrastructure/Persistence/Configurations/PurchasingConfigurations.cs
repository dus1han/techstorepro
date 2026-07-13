using TechStorePro.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");

        builder.Property(o => o.Number).HasMaxLength(50).IsRequired();
        builder.Property(o => o.Status).HasConversion<short>();
        builder.Property(o => o.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(o => o.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.DeletedReason).HasMaxLength(500);

        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.IsFullyReceived);

        builder.HasIndex(o => new { o.CompanyId, o.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(o => new { o.CompanyId, o.SupplierId, o.OrderedAt });
        builder.HasIndex(o => new { o.CompanyId, o.Status });

        builder.HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Branch).WithMany().HasForeignKey(o => o.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.Warehouse).WithMany().HasForeignKey(o => o.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines");

        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.ReceivedQuantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DiscountPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.Notes).HasMaxLength(500);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.OutstandingQuantity);

        builder.HasIndex(l => new { l.CompanyId, l.PurchaseOrderId });

        builder.HasOne(l => l.PurchaseOrder).WithMany(o => o.Lines)
            .HasForeignKey(l => l.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("goods_receipts");

        builder.Property(r => r.Number).HasMaxLength(50).IsRequired();
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(r => r.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(r => r.SupplierReference).HasMaxLength(100);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.DeletedReason).HasMaxLength(500);

        builder.Ignore(r => r.GoodsTotal);
        builder.Ignore(r => r.GoodsTotalBase);

        builder.HasIndex(r => new { r.CompanyId, r.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(r => new { r.CompanyId, r.SupplierId, r.ReceivedAt });
        builder.HasIndex(r => new { r.CompanyId, r.ImportShipmentId });

        builder.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Branch).WithMany().HasForeignKey(r => r.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable, and that is the point: requirements §25 makes the PO optional, so the direct
        // purchase flow (supplier → GRN → stock) is a first-class path rather than a workaround.
        builder.HasOne(r => r.PurchaseOrder).WithMany()
            .HasForeignKey(r => r.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ImportShipment).WithMany(s => s.Receipts)
            .HasForeignKey(r => r.ImportShipmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.ToTable("goods_receipt_lines");

        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DiscountPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.ApportionedCost).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.Notes).HasMaxLength(500);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.LandedUnitCost);

        builder.HasIndex(l => new { l.CompanyId, l.GoodsReceiptId });
        builder.HasIndex(l => new { l.CompanyId, l.ProductId });

        builder.HasOne(l => l.GoodsReceipt).WithMany(r => r.Lines)
            .HasForeignKey(l => l.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.PurchaseOrderLine).WithMany()
            .HasForeignKey(l => l.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class GoodsReceiptSerialConfiguration : IEntityTypeConfiguration<GoodsReceiptSerial>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptSerial> builder)
    {
        builder.ToTable("goods_receipt_serials");

        builder.Property(s => s.SerialNumber).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DeletedReason).HasMaxLength(500);

        builder.HasIndex(s => new { s.CompanyId, s.GoodsReceiptLineId });

        builder.HasOne(s => s.GoodsReceiptLine).WithMany(l => l.Serials)
            .HasForeignKey(s => s.GoodsReceiptLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportShipmentConfiguration : IEntityTypeConfiguration<ImportShipment>
{
    public void Configure(EntityTypeBuilder<ImportShipment> builder)
    {
        builder.ToTable("import_shipments");

        builder.Property(s => s.Number).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Status).HasConversion<short>();
        builder.Property(s => s.TransportDocument).HasMaxLength(100);
        builder.Property(s => s.VesselOrFlight).HasMaxLength(100);
        builder.Property(s => s.PortOfLoading).HasMaxLength(100);
        builder.Property(s => s.PortOfDischarge).HasMaxLength(100);
        builder.Property(s => s.UnabsorbedCost).HasColumnType(CatalogTypes.Money);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property(s => s.DeletedReason).HasMaxLength(500);

        builder.Ignore(s => s.TotalChargesBase);

        builder.HasIndex(s => new { s.CompanyId, s.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(s => new { s.CompanyId, s.Status });

        builder.HasOne(s => s.Supplier).WithMany().HasForeignKey(s => s.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Branch).WithMany().HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ImportShipmentChargeConfiguration : IEntityTypeConfiguration<ImportShipmentCharge>
{
    public void Configure(EntityTypeBuilder<ImportShipmentCharge> builder)
    {
        builder.ToTable("import_shipment_charges");

        builder.Property(c => c.Type).HasConversion<short>();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.Vendor).HasMaxLength(200);
        builder.Property(c => c.Reference).HasMaxLength(100);
        builder.Property(c => c.Amount).HasColumnType(CatalogTypes.Money);
        builder.Property(c => c.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(c => c.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.Ignore(c => c.AmountBase);

        builder.HasIndex(c => new { c.CompanyId, c.ImportShipmentId });

        builder.HasOne(c => c.ImportShipment).WithMany(s => s.Charges)
            .HasForeignKey(c => c.ImportShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("supplier_invoices");

        builder.Property(i => i.Number).HasMaxLength(50).IsRequired();
        builder.Property(i => i.SupplierReference).HasMaxLength(100).IsRequired();
        builder.Property(i => i.Status).HasConversion<short>();
        builder.Property(i => i.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(i => i.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(i => i.Notes).HasMaxLength(1000);
        builder.Property(i => i.DeletedReason).HasMaxLength(500);

        builder.Ignore(i => i.Total);
        builder.Ignore(i => i.TotalBase);
        builder.Ignore(i => i.PaidAmount);
        builder.Ignore(i => i.OutstandingAmount);
        builder.Ignore(i => i.IsSettled);

        builder.HasIndex(i => new { i.CompanyId, i.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // The same supplier cannot invoice the same reference twice — that is a duplicate entry, and
        // paying it would pay the supplier twice for one delivery.
        builder.HasIndex(i => new { i.CompanyId, i.SupplierId, i.SupplierReference })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(i => new { i.CompanyId, i.Status, i.DueAt });

        builder.HasOne(i => i.Supplier).WithMany().HasForeignKey(i => i.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Branch).WithMany().HasForeignKey(i => i.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.GoodsReceipt).WithMany()
            .HasForeignKey(i => i.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierInvoiceLineConfiguration : IEntityTypeConfiguration<SupplierInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceLine> builder)
    {
        builder.ToTable("supplier_invoice_lines");

        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DiscountPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.TaxPercent).HasColumnType(CatalogTypes.Percent);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.NetTotal);
        builder.Ignore(l => l.TaxAmount);
        builder.Ignore(l => l.LineTotal);

        builder.HasIndex(l => new { l.CompanyId, l.SupplierInvoiceId });

        builder.HasOne(l => l.SupplierInvoice).WithMany(i => i.Lines)
            .HasForeignKey(l => l.SupplierInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable("supplier_payments");

        builder.Property(p => p.Number).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.Amount).HasColumnType(CatalogTypes.Money);
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(p => p.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.DeletedReason).HasMaxLength(500);

        builder.Ignore(p => p.AmountBase);
        builder.Ignore(p => p.AllocatedAmount);
        builder.Ignore(p => p.UnallocatedAmount);
        builder.Ignore(p => p.ExchangeGainOrLoss);

        builder.HasIndex(p => new { p.CompanyId, p.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(p => new { p.CompanyId, p.SupplierId, p.PaidAt });

        builder.HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Branch).WithMany().HasForeignKey(p => p.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PaymentMethod).WithMany().HasForeignKey(p => p.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierPaymentAllocationConfiguration : IEntityTypeConfiguration<SupplierPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<SupplierPaymentAllocation> builder)
    {
        builder.ToTable("supplier_payment_allocations");

        builder.Property(a => a.Amount).HasColumnType(CatalogTypes.Money);
        builder.Property(a => a.InvoiceExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(a => a.PaymentExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(a => a.DeletedReason).HasMaxLength(500);

        builder.Ignore(a => a.ExchangeGainOrLoss);

        // One payment settles many invoices; one invoice is settled by many payments. But the same
        // payment must not be allocated to the same invoice twice — that would double-count the money.
        builder.HasIndex(a => new { a.CompanyId, a.SupplierPaymentId, a.SupplierInvoiceId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(a => new { a.CompanyId, a.SupplierInvoiceId });

        builder.HasOne(a => a.SupplierPayment).WithMany(p => p.Allocations)
            .HasForeignKey(a => a.SupplierPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.SupplierInvoice).WithMany(i => i.Allocations)
            .HasForeignKey(a => a.SupplierInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
