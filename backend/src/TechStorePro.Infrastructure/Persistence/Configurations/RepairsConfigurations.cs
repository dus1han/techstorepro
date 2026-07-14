using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

public class RepairTicketConfiguration : IEntityTypeConfiguration<RepairTicket>
{
    public void Configure(EntityTypeBuilder<RepairTicket> builder)
    {
        builder.ToTable("repair_tickets");

        builder.Property(t => t.Number).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status).HasConversion<short>();
        builder.Property(t => t.WarrantyType).HasConversion<short>();
        builder.Property(t => t.DeviceSerialNumber).HasMaxLength(100);
        builder.Property(t => t.ReportedFault).HasMaxLength(2000).IsRequired();
        builder.Property(t => t.Accessories).HasMaxLength(1000);
        builder.Property(t => t.ConditionNotes).HasMaxLength(1000);
        builder.Property(t => t.EstimatedCost).HasColumnType(CatalogTypes.Money);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.CancelledReason).HasMaxLength(500);
        builder.Property(t => t.DeletedReason).HasMaxLength(500);

        builder.Ignore(t => t.IsWarranty);
        builder.Ignore(t => t.IsClosed);
        builder.Ignore(t => t.PartsCost);
        builder.Ignore(t => t.OutsourcingCost);
        builder.Ignore(t => t.TotalCost);
        builder.Ignore(t => t.ChargeableTotal);
        builder.Ignore(t => t.GrossProfit);

        builder.HasIndex(t => new { t.CompanyId, t.Number })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(t => new { t.CompanyId, t.CustomerId, t.ReceivedAt });
        builder.HasIndex(t => new { t.CompanyId, t.Status });
        builder.HasIndex(t => new { t.CompanyId, t.TechnicianId });

        // The pending-repairs report (§35) sorts on what was promised and has not been delivered.
        builder.HasIndex(t => new { t.CompanyId, t.PromisedAt });

        // "This exact machine is on the counter again" — the lookup the workshop does most.
        builder.HasIndex(t => new { t.CompanyId, t.DeviceSerialNumber });

        builder.HasOne(t => t.Customer).WithMany().HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Branch).WithMany().HasForeignKey(t => t.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Null for a device the shop has never sold and does not stock. A customer may bring in anything.
        builder.HasOne(t => t.DeviceProduct).WithMany().HasForeignKey(t => t.DeviceProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.DeviceSerial).WithMany().HasForeignKey(t => t.DeviceSerialId)
            .OnDelete(DeleteBehavior.Restrict);

        // The back-edge into Sales (architecture.md §2): the invoice line that sold this machine. Restrict,
        // emphatically — an invoice line that could be deleted out from under a warranty claim would take
        // the proof of the sale with it.
        builder.HasOne(t => t.WarrantyInvoiceLine).WithMany().HasForeignKey(t => t.WarrantyInvoiceLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RepairStatusChangeConfiguration : IEntityTypeConfiguration<RepairStatusChange>
{
    public void Configure(EntityTypeBuilder<RepairStatusChange> builder)
    {
        builder.ToTable("repair_status_history");

        builder.Property(h => h.FromStatus).HasConversion<short>();
        builder.Property(h => h.ToStatus).HasConversion<short>();
        builder.Property(h => h.Notes).HasMaxLength(1000);
        builder.Property(h => h.DeletedReason).HasMaxLength(500);

        builder.HasIndex(h => new { h.CompanyId, h.RepairTicketId, h.ChangedAt });

        builder.HasOne(h => h.RepairTicket).WithMany(t => t.StatusHistory)
            .HasForeignKey(h => h.RepairTicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RepairDiagnosisConfiguration : IEntityTypeConfiguration<RepairDiagnosis>
{
    public void Configure(EntityTypeBuilder<RepairDiagnosis> builder)
    {
        builder.ToTable("repair_diagnoses");

        builder.Property(d => d.Findings).HasMaxLength(2000).IsRequired();
        builder.Property(d => d.RecommendedAction).HasMaxLength(2000);
        builder.Property(d => d.EstimatedCost).HasColumnType(CatalogTypes.Money);
        builder.Property(d => d.DeletedReason).HasMaxLength(500);

        builder.HasIndex(d => new { d.CompanyId, d.RepairTicketId });

        builder.HasOne(d => d.RepairTicket).WithMany(t => t.Diagnoses)
            .HasForeignKey(d => d.RepairTicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RepairPartConfiguration : IEntityTypeConfiguration<RepairPart>
{
    public void Configure(EntityTypeBuilder<RepairPart> builder)
    {
        builder.ToTable("repair_parts");

        builder.Property(p => p.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(p => p.UnitCost).HasColumnType(CatalogTypes.Money);
        builder.Property(p => p.UnitPrice).HasColumnType(CatalogTypes.Money);
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.Property(p => p.DeletedReason).HasMaxLength(500);

        builder.Ignore(p => p.CostTotal);
        builder.Ignore(p => p.ChargeTotal);

        builder.HasIndex(p => new { p.CompanyId, p.RepairTicketId });
        builder.HasIndex(p => new { p.CompanyId, p.ProductId });

        builder.HasOne(p => p.RepairTicket).WithMany(t => t.Parts)
            .HasForeignKey(p => p.RepairTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Warehouse).WithMany().HasForeignKey(p => p.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Serial).WithMany().HasForeignKey(p => p.SerialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RepairLabourConfiguration : IEntityTypeConfiguration<RepairLabour>
{
    public void Configure(EntityTypeBuilder<RepairLabour> builder)
    {
        builder.ToTable("repair_labour");

        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.Hours).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.HourlyRate).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.ChargeTotal);

        builder.HasIndex(l => new { l.CompanyId, l.RepairTicketId });
        builder.HasIndex(l => new { l.CompanyId, l.TechnicianId });

        builder.HasOne(l => l.RepairTicket).WithMany(t => t.Labour)
            .HasForeignKey(l => l.RepairTicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RepairOutsourcingConfiguration : IEntityTypeConfiguration<RepairOutsourcing>
{
    public void Configure(EntityTypeBuilder<RepairOutsourcing> builder)
    {
        builder.ToTable("repair_outsourcing");

        builder.Property(o => o.Status).HasConversion<short>();
        builder.Property(o => o.Cost).HasColumnType(CatalogTypes.Money);
        builder.Property(o => o.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(o => o.ExchangeRate).HasColumnType(CatalogTypes.Rate);
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.DeletedReason).HasMaxLength(500);

        builder.Ignore(o => o.CostInBaseCurrency);

        builder.HasIndex(o => new { o.CompanyId, o.RepairTicketId });
        builder.HasIndex(o => new { o.CompanyId, o.VendorSupplierId });
        builder.HasIndex(o => new { o.CompanyId, o.Status });

        builder.HasOne(o => o.RepairTicket).WithMany(t => t.Outsourcings)
            .HasForeignKey(o => o.RepairTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.VendorSupplier).WithMany().HasForeignKey(o => o.VendorSupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RepairChargeConfiguration : IEntityTypeConfiguration<RepairCharge>
{
    public void Configure(EntityTypeBuilder<RepairCharge> builder)
    {
        builder.ToTable("repair_charges");

        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.HasIndex(c => new { c.CompanyId, c.RepairTicketId });

        // One invoice bills one repair. A second charge row pointing at the same invoice would mean two
        // jobs billed on one document with no way to say which line belonged to which — and the
        // profitability report would count the revenue twice.
        builder.HasIndex(c => new { c.CompanyId, c.SalesInvoiceId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasOne(c => c.RepairTicket).WithMany(t => t.Charges)
            .HasForeignKey(c => c.RepairTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.SalesInvoice).WithMany().HasForeignKey(c => c.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class WarrantyConfiguration : IEntityTypeConfiguration<Warranty>
{
    public void Configure(EntityTypeBuilder<Warranty> builder)
    {
        builder.ToTable("warranties");

        builder.Property(w => w.WarrantyType).HasConversion<short>();
        builder.Property(w => w.SourceType).HasConversion<short>();
        builder.Property(w => w.SerialNumber).HasMaxLength(100);
        builder.Property(w => w.Terms).HasMaxLength(2000);
        builder.Property(w => w.DeletedReason).HasMaxLength(500);

        builder.HasIndex(w => new { w.CompanyId, w.ProductId });
        builder.HasIndex(w => new { w.CompanyId, w.SerialId });

        // The intake lookup: "is the machine on my counter covered by anything?" — by the number engraved
        // on it, which is all the front desk has.
        builder.HasIndex(w => new { w.CompanyId, w.SerialNumber, w.EndsOn });

        builder.HasOne(w => w.Product).WithMany().HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(w => w.Serial).WithMany().HasForeignKey(w => w.SerialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class WarrantyClaimConfiguration : IEntityTypeConfiguration<WarrantyClaim>
{
    public void Configure(EntityTypeBuilder<WarrantyClaim> builder)
    {
        builder.ToTable("warranty_claims");

        builder.Property(c => c.Status).HasConversion<short>();
        builder.Property(c => c.Outcome).HasMaxLength(1000);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.HasIndex(c => new { c.CompanyId, c.WarrantyId });
        builder.HasIndex(c => new { c.CompanyId, c.RepairTicketId });
        builder.HasIndex(c => new { c.CompanyId, c.Status });

        builder.HasOne(c => c.Warranty).WithMany(w => w.Claims)
            .HasForeignKey(c => c.WarrantyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.RepairTicket).WithMany().HasForeignKey(c => c.RepairTicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
