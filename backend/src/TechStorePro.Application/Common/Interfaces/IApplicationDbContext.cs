using TechStorePro.Domain.Auditing;
using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Configuration;
using TechStorePro.Domain.Identity;
using TechStorePro.Domain.Inventory;
using TechStorePro.Domain.Purchasing;
using TechStorePro.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TechStorePro.Application.Common.Interfaces;

/// <summary>
/// The Application layer's view of the database. Feature handlers depend on this rather
/// than on the concrete Infrastructure DbContext.
///
/// Business modules add their DbSets here as they are built.
/// </summary>
public interface IApplicationDbContext
{
    // --- Identity and tenancy (P1) -----------------------------------------------------------
    DbSet<Company> Companies { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<BranchWarehouse> BranchWarehouses { get; }
    DbSet<User> Users { get; }
    DbSet<UserBranch> UserBranches { get; }
    DbSet<UserPermission> UserPermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<LoginHistory> LoginHistory { get; }
    DbSet<Feature> Features { get; }

    // --- Purchasing and imports (P4) -----------------------------------------------------------
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<GoodsReceiptLine> GoodsReceiptLines { get; }
    DbSet<GoodsReceiptSerial> GoodsReceiptSerials { get; }
    DbSet<SupplierInvoice> SupplierInvoices { get; }
    DbSet<SupplierInvoiceLine> SupplierInvoiceLines { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }
    DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations { get; }
    DbSet<ImportShipment> ImportShipments { get; }
    DbSet<ImportShipmentCharge> ImportShipmentCharges { get; }

    // --- Sales (P5) ----------------------------------------------------------------------------
    //
    // None of these may write stock. The delivery is the only sales document that moves goods, and it
    // does it through IStockLedger like everything else — see architecture.md §4.5.
    DbSet<Quotation> Quotations { get; }
    DbSet<QuotationLine> QuotationLines { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesOrderLine> SalesOrderLines { get; }
    DbSet<Delivery> Deliveries { get; }
    DbSet<DeliveryLine> DeliveryLines { get; }
    DbSet<DeliverySerial> DeliverySerials { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<SalesInvoiceLine> SalesInvoiceLines { get; }
    DbSet<CustomerPayment> CustomerPayments { get; }
    DbSet<CustomerPaymentMethod> CustomerPaymentMethods { get; }
    DbSet<CustomerPaymentAllocation> CustomerPaymentAllocations { get; }

    /// <summary>
    /// Requests the API has already answered. Written by <c>IdempotencyFilter</c>, and by nothing else —
    /// a handler that touched this would be forging its own retry history.
    /// </summary>
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    // --- The platform, not a tenant ------------------------------------------------------------
    // Deliberately not ITenantScoped: a platform admin belongs to no company. These are the only
    // tables in the system that no query filter touches.
    DbSet<PlatformAdmin> PlatformAdmins { get; }
    DbSet<PlatformRefreshToken> PlatformRefreshTokens { get; }

    // --- Configuration (P1) ------------------------------------------------------------------
    DbSet<SettingDefinition> SettingDefinitions { get; }
    DbSet<SettingValue> SettingValues { get; }
    DbSet<DocumentNumberSequence> DocumentNumberSequences { get; }

    // --- Auditing (P1) -----------------------------------------------------------------------
    DbSet<AuditLog> AuditLogs { get; }

    // --- Master data (P2) --------------------------------------------------------------------
    DbSet<Product> Products { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<TaxRate> TaxRates { get; }
    DbSet<PriceTier> PriceTiers { get; }
    DbSet<PriceList> PriceLists { get; }
    DbSet<PriceListItem> PriceListItems { get; }
    DbSet<PriceHistory> PriceHistory { get; }
    DbSet<Discount> Discounts { get; }
    DbSet<PaymentMethod> PaymentMethods { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<FxRate> FxRates { get; }

    // --- Inventory (P3) ----------------------------------------------------------------------
    //
    // StockMovements and StockBalances are exposed for *reading*. Writing them is the exclusive
    // business of IStockLedger — a handler that appends a movement by hand bypasses the row lock, the
    // costing and the availability check all at once. See architecture.md §4.5.
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<Serial> Serials { get; }
    DbSet<SerialEvent> SerialEvents { get; }
    DbSet<StockReservation> StockReservations { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferLine> StockTransferLines { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<StockAdjustmentLine> StockAdjustmentLines { get; }
    DbSet<StockCount> StockCounts { get; }
    DbSet<StockCountLine> StockCountLines { get; }
    DbSet<BarcodePrintJob> BarcodePrintJobs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an explicit transaction. Needed where a handler must hold a row lock across several
    /// statements — document numbering and, later, the stock ledger.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Escape hatch for the few places that must bypass the tenant query filter — the login flow
    /// resolves a user and their memberships <em>before</em> a company is known, so there is no
    /// tenant to filter by yet.
    /// </summary>
    IQueryable<TEntity> IgnoringTenantFilter<TEntity>() where TEntity : class;
}
