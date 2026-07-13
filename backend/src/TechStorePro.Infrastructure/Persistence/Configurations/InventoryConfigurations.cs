using TechStorePro.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStorePro.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.Property(m => m.Type).HasConversion<short>();
        builder.Property(m => m.ReferenceType).HasConversion<short>();
        builder.Property(m => m.ReferenceNumber).HasMaxLength(50);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.Property(m => m.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(m => m.BalanceAfter).HasColumnType(CatalogTypes.Quantity);
        builder.Property(m => m.UnitCost).HasColumnType(CatalogTypes.Money);
        builder.Property(m => m.AverageCostAfter).HasColumnType(CatalogTypes.Money);

        // Money with no units behind it: the landed cost of an import folded into stock that was
        // received weeks earlier. The balance audit sums this alongside quantity × unit_cost — leave it
        // out and every import would look like a permanent, unfixable discrepancy.
        builder.Property(m => m.ValueAdjustment).HasColumnType(CatalogTypes.Money);

        builder.Ignore(m => m.Value);

        // The index behind "what happened to this product?" and behind historical stock, which replays
        // movements up to a date. Descending on occurred_at because every caller wants the recent end.
        builder.HasIndex(m => new { m.CompanyId, m.ProductId, m.OccurredAt })
            .IsDescending(false, false, true);

        // The index behind a warehouse's stock card.
        builder.HasIndex(m => new { m.CompanyId, m.WarehouseId, m.ProductId, m.OccurredAt })
            .IsDescending(false, false, false, true);

        // "Show me the movements this GRN / invoice / adjustment made" — the audit trail from a
        // document back to the stock it moved.
        builder.HasIndex(m => new { m.CompanyId, m.ReferenceType, m.ReferenceId });

        builder.HasIndex(m => new { m.CompanyId, m.SerialId });

        // Restrict everywhere: the ledger is append-only, and a cascade from any of these would delete
        // history. A product that has ever moved cannot be hard-deleted, and that is the intent.
        builder.HasOne(m => m.Warehouse).WithMany().HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Branch).WithMany().HasForeignKey(m => m.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Serial).WithMany().HasForeignKey(m => m.SerialId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("stock_balances");

        builder.Property(b => b.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(b => b.ReservedQuantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(b => b.AverageCost).HasColumnType(CatalogTypes.Money);

        builder.Ignore(b => b.AvailableQuantity);
        builder.Ignore(b => b.TotalValue);

        // One balance per product per warehouse — and this unique index is not decoration. It is the
        // conflict target of the upsert in StockLedger.LockBalanceAsync, which is what lets a
        // first-ever receipt take a row lock on a row that does not exist yet. Drop it and two
        // concurrent first receipts of the same product silently create two balances.
        builder.HasIndex(b => new { b.CompanyId, b.WarehouseId, b.ProductId }).IsUnique();

        // The low-stock report (requirements §36) scans this.
        builder.HasIndex(b => new { b.CompanyId, b.ProductId });

        builder.HasOne(b => b.Warehouse).WithMany().HasForeignKey(b => b.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Product).WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SerialConfiguration : IEntityTypeConfiguration<Serial>
{
    public void Configure(EntityTypeBuilder<Serial> builder)
    {
        builder.ToTable("serials");

        builder.Property(s => s.SerialNumber).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Status).HasConversion<short>();
        builder.Property(s => s.PurchaseCost).HasColumnType(CatalogTypes.Money);
        builder.Property(s => s.DeletedReason).HasMaxLength(500);

        // One physical machine, one row. Unfiltered by is_deleted, unlike a SKU: a retired serial must
        // NOT free its number for reuse. The whole promise of the serial ledger is that this number,
        // scanned two years from now, finds the one machine it was ever attached to.
        builder.HasIndex(s => new { s.CompanyId, s.SerialNumber }).IsUnique();

        builder.HasIndex(s => new { s.CompanyId, s.ProductId, s.Status });
        builder.HasIndex(s => new { s.CompanyId, s.WarehouseId, s.Status });

        builder.HasOne(s => s.Product).WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Warehouse).WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Supplier).WithMany().HasForeignKey(s => s.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SerialEventConfiguration : IEntityTypeConfiguration<SerialEvent>
{
    public void Configure(EntityTypeBuilder<SerialEvent> builder)
    {
        builder.ToTable("serial_events");

        builder.Property(e => e.Type).HasConversion<short>();
        builder.Property(e => e.Status).HasConversion<short>();
        builder.Property(e => e.ReferenceType).HasConversion<short>();
        builder.Property(e => e.ReferenceNumber).HasMaxLength(50);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        // "Show me this machine's history" — the §18 report, in one index.
        builder.HasIndex(e => new { e.CompanyId, e.SerialId, e.At });

        builder.HasOne(e => e.Serial)
            .WithMany(s => s.Events)
            .HasForeignKey(e => e.SerialId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.Property(r => r.Status).HasConversion<short>();
        builder.Property(r => r.ReferenceType).HasConversion<short>();
        builder.Property(r => r.ReferenceNumber).HasMaxLength(50);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.DeletedReason).HasMaxLength(500);

        builder.Property(r => r.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(r => r.FulfilledQuantity).HasColumnType(CatalogTypes.Quantity);

        builder.Ignore(r => r.OutstandingQuantity);

        // The sweep that expires forgotten reservations rides this index, and so does "why is this
        // product unavailable?" — both filter on active reservations for one product.
        builder.HasIndex(r => new { r.CompanyId, r.WarehouseId, r.ProductId, r.Status });
        builder.HasIndex(r => new { r.CompanyId, r.Status, r.ExpiresAt });

        builder.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Product).WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Serial).WithMany().HasForeignKey(r => r.SerialId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.ToTable("stock_transfers");

        builder.Property(t => t.Number).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status).HasConversion<short>();
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.DeletedReason).HasMaxLength(500);

        builder.Ignore(t => t.HasShortfall);

        builder.HasIndex(t => new { t.CompanyId, t.Number }).IsUnique();
        builder.HasIndex(t => new { t.CompanyId, t.Status });

        builder.HasOne(t => t.FromWarehouse).WithMany().HasForeignKey(t => t.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.ToWarehouse).WithMany().HasForeignKey(t => t.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Branch).WithMany().HasForeignKey(t => t.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> builder)
    {
        builder.ToTable("stock_transfer_lines");

        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.ReceivedQuantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitCost).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.ShortfallQuantity);

        builder.HasOne(l => l.StockTransfer)
            .WithMany(t => t.Lines)
            .HasForeignKey(l => l.StockTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Serial).WithMany().HasForeignKey(l => l.SerialId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.ToTable("stock_adjustments");

        builder.Property(a => a.Number).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Reason).HasConversion<short>();
        builder.Property(a => a.Explanation).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.DeletedReason).HasMaxLength(500);

        builder.Ignore(a => a.NetValue);

        builder.HasIndex(a => new { a.CompanyId, a.Number }).IsUnique();

        // "What did we write off this year, and why?" — the report that justifies the mandatory reason.
        builder.HasIndex(a => new { a.CompanyId, a.Reason, a.AdjustedAt });

        builder.HasOne(a => a.Warehouse).WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Branch).WithMany().HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> builder)
    {
        builder.ToTable("stock_adjustment_lines");

        builder.Property(l => l.Quantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitCost).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.Notes).HasMaxLength(1000);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.IsWriteOn);

        builder.HasOne(l => l.StockAdjustment)
            .WithMany(a => a.Lines)
            .HasForeignKey(l => l.StockAdjustmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Serial).WithMany().HasForeignKey(l => l.SerialId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.ToTable("stock_counts");

        builder.Property(c => c.Number).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Status).HasConversion<short>();
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.DeletedReason).HasMaxLength(500);

        builder.Ignore(c => c.Variances);
        builder.Ignore(c => c.NetVarianceValue);

        builder.HasIndex(c => new { c.CompanyId, c.Number }).IsUnique();
        builder.HasIndex(c => new { c.CompanyId, c.WarehouseId, c.Status });

        builder.HasOne(c => c.Warehouse).WithMany().HasForeignKey(c => c.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Branch).WithMany().HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> builder)
    {
        builder.ToTable("stock_count_lines");

        builder.Property(l => l.SystemQuantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.CountedQuantity).HasColumnType(CatalogTypes.Quantity);
        builder.Property(l => l.UnitCost).HasColumnType(CatalogTypes.Money);
        builder.Property(l => l.Notes).HasMaxLength(1000);
        builder.Property(l => l.DeletedReason).HasMaxLength(500);

        builder.Ignore(l => l.Variance);
        builder.Ignore(l => l.VarianceValue);

        // One line per product per count. A scanner that reads the same shelf label twice must add to
        // the line it already made, not create a second one that silently halves the variance.
        builder.HasIndex(l => new { l.StockCountId, l.ProductId, l.SerialId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasOne(l => l.StockCount)
            .WithMany(c => c.Lines)
            .HasForeignKey(l => l.StockCountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Serial).WithMany().HasForeignKey(l => l.SerialId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BarcodePrintJobConfiguration : IEntityTypeConfiguration<BarcodePrintJob>
{
    public void Configure(EntityTypeBuilder<BarcodePrintJob> builder)
    {
        builder.ToTable("barcode_print_jobs");

        builder.Property(j => j.SourceType).HasConversion<short>();
        builder.Property(j => j.Symbology).HasConversion<short>();
        builder.Property(j => j.Template).HasConversion<short>();
        builder.Property(j => j.DeletedReason).HasMaxLength(500);

        builder.HasIndex(j => new { j.CompanyId, j.SourceType, j.SourceId });
    }
}
