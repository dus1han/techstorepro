using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A permissionable unit of the product — "sales.invoice", "inventory.transfer". Reference data,
/// seeded from <see cref="FeatureCatalog"/>, not tenant-scoped and not user-editable: a company
/// admin grants permissions <em>over</em> features, but cannot invent one.
/// </summary>
public class Feature
{
    public string Code { get; set; } = null!;
    public string Module { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int DisplayOrder { get; set; }

    /// <summary>
    /// The actions that make sense for this feature. Granting Approve on a feature that has no
    /// approval step would be a permission that can never be checked, so the grid does not offer it.
    /// </summary>
    public PermissionAction[] SupportedActions { get; set; } = [];
}

/// <summary>
/// The single source of truth for what can be permissioned. Adding a feature here and running the
/// seeder is the whole of "expose a new screen to the permission grid".
///
/// Only P1 features exist today; each later phase appends its own.
/// </summary>
public static class FeatureCatalog
{
    // --- P1: settings ---
    public const string CompanyProfile = "settings.company";
    public const string Branches = "settings.branches";
    public const string Warehouses = "settings.warehouses";
    public const string Users = "settings.users";
    public const string Permissions = "settings.permissions";
    public const string Settings = "settings.configuration";
    public const string DocumentNumbering = "settings.numbering";
    public const string AuditLog = "settings.audit";

    // --- P2: master data ---
    public const string Products = "catalog.products";
    public const string Categories = "catalog.categories";
    public const string Brands = "catalog.brands";
    public const string Customers = "catalog.customers";
    public const string Suppliers = "catalog.suppliers";
    public const string TaxRates = "catalog.tax_rates";
    public const string Pricing = "catalog.pricing";
    public const string Discounts = "catalog.discounts";
    public const string PaymentMethods = "catalog.payment_methods";
    public const string Currencies = "catalog.currencies";

    // --- P3: inventory ---
    public const string Stock = "inventory.stock";
    public const string StockMovements = "inventory.movements";
    public const string Adjustments = "inventory.adjustments";
    public const string Transfers = "inventory.transfers";
    public const string StockCounts = "inventory.counts";
    public const string Reservations = "inventory.reservations";
    public const string Serials = "inventory.serials";
    public const string Barcodes = "inventory.barcodes";

    // --- P4: purchasing and imports ---
    public const string PurchaseOrders = "purchasing.orders";
    public const string GoodsReceipts = "purchasing.receipts";
    public const string SupplierInvoices = "purchasing.invoices";
    public const string SupplierPayments = "purchasing.payments";
    public const string ImportShipments = "purchasing.imports";

    private static readonly PermissionAction[] ReadOnly = [PermissionAction.View, PermissionAction.Export];

    private static readonly PermissionAction[] Full =
    [
        PermissionAction.View, PermissionAction.Create, PermissionAction.Edit,
        PermissionAction.Delete, PermissionAction.Print, PermissionAction.Export
    ];

    private static readonly PermissionAction[] Manage =
    [
        PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete
    ];

    private static readonly PermissionAction[] ManageAndApprove =
    [
        PermissionAction.View, PermissionAction.Create, PermissionAction.Edit,
        PermissionAction.Delete, PermissionAction.Approve
    ];

    public static readonly IReadOnlyList<Feature> All =
    [
        new() { Code = CompanyProfile, Module = "Settings", Name = "Company profile", DisplayOrder = 10, SupportedActions = [PermissionAction.View, PermissionAction.Edit] },
        new() { Code = Branches, Module = "Settings", Name = "Branches", DisplayOrder = 20, SupportedActions = Manage },
        new() { Code = Warehouses, Module = "Settings", Name = "Warehouses", DisplayOrder = 30, SupportedActions = Manage },
        new() { Code = Users, Module = "Settings", Name = "Users", DisplayOrder = 40, SupportedActions = Manage },
        new() { Code = Permissions, Module = "Settings", Name = "User permissions", DisplayOrder = 50, SupportedActions = [PermissionAction.View, PermissionAction.Edit] },
        new() { Code = Settings, Module = "Settings", Name = "Configuration", DisplayOrder = 60, SupportedActions = [PermissionAction.View, PermissionAction.Edit] },
        new() { Code = DocumentNumbering, Module = "Settings", Name = "Document numbering", DisplayOrder = 70, SupportedActions = [PermissionAction.View, PermissionAction.Edit] },
        new() { Code = AuditLog, Module = "Settings", Name = "Audit trail", DisplayOrder = 80, SupportedActions = ReadOnly },

        // Master data. Discounts and pricing carry Approve: requirements §32 puts a manager's
        // approval behind a discount that exceeds its ceiling, so the permission has to exist before
        // the sales module in P5 can check it.
        new() { Code = Products, Module = "Master data", Name = "Products", DisplayOrder = 110, SupportedActions = Full },
        new() { Code = Categories, Module = "Master data", Name = "Categories", DisplayOrder = 120, SupportedActions = Manage },
        new() { Code = Brands, Module = "Master data", Name = "Brands", DisplayOrder = 130, SupportedActions = Manage },
        new() { Code = Customers, Module = "Master data", Name = "Customers", DisplayOrder = 140, SupportedActions = Full },
        new() { Code = Suppliers, Module = "Master data", Name = "Suppliers", DisplayOrder = 150, SupportedActions = Full },
        new() { Code = TaxRates, Module = "Master data", Name = "Tax rates", DisplayOrder = 160, SupportedActions = Manage },
        new() { Code = Pricing, Module = "Master data", Name = "Price tiers & lists", DisplayOrder = 170, SupportedActions = ManageAndApprove },
        new() { Code = Discounts, Module = "Master data", Name = "Discounts", DisplayOrder = 180, SupportedActions = ManageAndApprove },
        new() { Code = PaymentMethods, Module = "Master data", Name = "Payment methods", DisplayOrder = 190, SupportedActions = Manage },
        new() { Code = Currencies, Module = "Master data", Name = "Currencies & FX", DisplayOrder = 200, SupportedActions = Manage },

        // Inventory. Note what is read-only and what is not — this is where the module's controls live.
        //
        // Stock and movements cannot be Created or Edited by anyone, at any permission level: the only
        // way stock moves is through a document (an adjustment, a transfer, an approved count) that
        // leaves a reason and a name behind it. A "create stock movement" permission would be a licence
        // to conjure inventory out of nothing, and the audit trail would show it as nobody's decision.
        new() { Code = Stock, Module = "Inventory", Name = "Stock on hand", DisplayOrder = 210, SupportedActions = ReadOnly },
        new() { Code = StockMovements, Module = "Inventory", Name = "Stock movements", DisplayOrder = 220, SupportedActions = ReadOnly },

        // Adjustments post immediately and can write stock off. Create is therefore the dangerous
        // grant, not Approve — there is no approval step to hide behind (requirements §21 puts
        // approval behind counts, not adjustments).
        new() { Code = Adjustments, Module = "Inventory", Name = "Stock adjustments", DisplayOrder = 230, SupportedActions = Manage },
        new() { Code = Transfers, Module = "Inventory", Name = "Stock transfers", DisplayOrder = 240, SupportedActions = Manage },

        // Approve is the one that matters: approving a count authorises the write-off it computed.
        new() { Code = StockCounts, Module = "Inventory", Name = "Physical stock counts", DisplayOrder = 250, SupportedActions = ManageAndApprove },

        new() { Code = Reservations, Module = "Inventory", Name = "Stock reservations", DisplayOrder = 260, SupportedActions = Manage },
        new() { Code = Serials, Module = "Inventory", Name = "Serial numbers", DisplayOrder = 270, SupportedActions = [PermissionAction.View, PermissionAction.Export, PermissionAction.Print] },
        new() { Code = Barcodes, Module = "Inventory", Name = "Barcodes & labels", DisplayOrder = 280, SupportedActions = [PermissionAction.View, PermissionAction.Print] },

        // Purchasing. Note where Approve sits, because that is where the money is.
        //
        // A purchase order commits the company to spending, so approving one is the control — and it is
        // separate from creating one, so the person who chooses the supplier need not be the person who
        // signs for the cost.
        new() { Code = PurchaseOrders, Module = "Purchasing", Name = "Purchase orders", DisplayOrder = 310, SupportedActions = ManageAndApprove },

        // Receiving goods moves stock and sets the cost that feeds the moving average. There is no
        // approval step — the goods are physically here, and refusing to book them because a manager is
        // at lunch would leave the shelf and the system disagreeing.
        new() { Code = GoodsReceipts, Module = "Purchasing", Name = "Goods receipts", DisplayOrder = 320, SupportedActions = Manage },

        new() { Code = SupplierInvoices, Module = "Purchasing", Name = "Supplier invoices", DisplayOrder = 330, SupportedActions = ManageAndApprove },
        new() { Code = SupplierPayments, Module = "Purchasing", Name = "Supplier payments", DisplayOrder = 340, SupportedActions = ManageAndApprove },

        // Approve is the one that matters here, and it is not a formality: approving an import's
        // apportionment folds its freight into the weighted average of every product in the container,
        // where it spreads to stock that arrived years ago and never washes out (§45 D1, D6).
        new() { Code = ImportShipments, Module = "Purchasing", Name = "Import shipments & landed cost", DisplayOrder = 350, SupportedActions = ManageAndApprove }
    ];

    public static bool Exists(string code) => All.Any(f => f.Code == code);

    public static bool Supports(string code, PermissionAction action) =>
        All.FirstOrDefault(f => f.Code == code)?.SupportedActions.Contains(action) ?? false;
}
