/**
 * Mirrors the API's identity contracts. Hand-written for P1; from P2 these are generated from
 * /openapi/v1.json so a drifted client type becomes a build error rather than a runtime surprise.
 */

/**
 * The seven actions of requirements §7. The numbers are the wire format (smallint), so they must
 * match Domain/Identity/PermissionAction.cs exactly.
 */
export enum PermissionAction {
  View = 1,
  Create = 2,
  Edit = 3,
  Delete = 4,
  Approve = 5,
  Print = 6,
  Export = 7,
}

export const ACTION_LABELS: Record<PermissionAction, string> = {
  [PermissionAction.View]: "View",
  [PermissionAction.Create]: "Create",
  [PermissionAction.Edit]: "Edit",
  [PermissionAction.Delete]: "Delete",
  [PermissionAction.Approve]: "Approve",
  [PermissionAction.Print]: "Print",
  [PermissionAction.Export]: "Export",
};

/** Feature codes, mirroring Domain/Identity/FeatureCatalog.cs. */
export const FEATURES = {
  // P1 — settings
  company: "settings.company",
  branches: "settings.branches",
  warehouses: "settings.warehouses",
  users: "settings.users",
  permissions: "settings.permissions",
  settings: "settings.configuration",
  numbering: "settings.numbering",
  audit: "settings.audit",

  // P2 — master data
  products: "catalog.products",
  categories: "catalog.categories",
  brands: "catalog.brands",
  customers: "catalog.customers",
  suppliers: "catalog.suppliers",
  taxRates: "catalog.tax_rates",
  pricing: "catalog.pricing",
  discounts: "catalog.discounts",
  paymentMethods: "catalog.payment_methods",
  currencies: "catalog.currencies",

  // P3 — inventory. Stock and movements are read-only at every permission level: the only way stock
  // moves is through a document that leaves a reason and a name behind it.
  stock: "inventory.stock",
  stockMovements: "inventory.movements",
  adjustments: "inventory.adjustments",
  transfers: "inventory.transfers",
  stockCounts: "inventory.counts",
  reservations: "inventory.reservations",
  serials: "inventory.serials",
  barcodes: "inventory.barcodes",

  // P4 — purchasing
  purchaseOrders: "purchasing.orders",
  goodsReceipts: "purchasing.receipts",
  supplierInvoices: "purchasing.invoices",
  supplierPayments: "purchasing.payments",
  importShipments: "purchasing.imports",

  // P5 — sales. Approve on invoices is what authorises a price below the price list's floor (§32);
  // approve on credit notes authorises giving money back.
  quotations: "sales.quotations",
  salesOrders: "sales.orders",
  deliveries: "sales.deliveries",
  salesInvoices: "sales.invoices",
  customerPayments: "sales.payments",
  creditNotes: "sales.credit_notes",

  // P6 — repairs. Approve on a job is not a manager signing off internally: it is the record that *the
  // customer* agreed to the estimate, and it is the gate on the parts store (§28). Approve on warranties
  // is the decision that the shop, rather than the customer, eats the cost of a repair.
  repairTickets: "repairs.tickets",
  repairParts: "repairs.parts",
  outsourcing: "repairs.outsourcing",
  warranties: "repairs.warranties",

  // P7 — finance. View and Export only: a report writes nothing back. The grant is still worth being
  // deliberate about, because "what does every customer owe, and how old is it" is the most commercially
  // sensitive question the system can answer.
  receivables: "reports.receivables",
  payables: "reports.payables",

  // P7 slice 2 — the money itself. Approve on accounts is not a manager signing a form: it is the right to
  // move money between two accounts, which is the only act in the system that takes cash out of a drawer
  // without any document to explain it. Delete on expenses is the right to cancel one — which pays the
  // money back.
  accounts: "finance.accounts",
  expenses: "finance.expenses",
  expenseCategories: "finance.expense_categories",
} as const;

export interface Permission {
  feature: string;
  action: PermissionAction;
}

export interface CompanyMembership {
  companyId: string;
  companyName: string;
  isDefault: boolean;
  isOwner: boolean;
}

export interface CurrentUser {
  userId: string;
  username: string;
  fullName: string;
  companyId: string | null;
  companyName: string | null;
  /** Shown so the user can be reminded what to type after the '@' next time they sign in. */
  companyCode: string | null;
  isOwner: boolean;
  permissions: Permission[];
  accessibleBranchIds: string[];
}

export interface AuthResult {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: CurrentUser;
}

export interface Branch {
  id: string;
  name: string;
  code: string;
  address: string | null;
  phone: string | null;
  email: string | null;
  defaultWarehouseId: string | null;
  defaultWarehouseName: string | null;
  isDefault: boolean;
  isActive: boolean;
}

export interface CompanyUser {
  userId: string;
  username: string;
  /** What this person actually types to sign in, e.g. "ahmed@GULF01". */
  login: string;
  fullName: string;
  email: string | null;
  phone: string | null;
  isOwner: boolean;
  isActive: boolean;
  permissionCount: number;
  branchIds: string[];
}

export interface PermissionGridAction {
  action: PermissionAction;
  supported: boolean;
  granted: boolean;
}

export interface PermissionGridFeature {
  feature: string;
  module: string;
  name: string;
  displayOrder: number;
  actions: PermissionGridAction[];
}

export interface PermissionGrid {
  userId: string;
  userFullName: string;
  username: string;
  isOwner: boolean;
  features: PermissionGridFeature[];
}

export interface Setting {
  key: string;
  module: string;
  name: string;
  description: string | null;
  dataType: number;
  scope: number;
  defaultValue: string;
  effectiveValue: string;
  isOverridden: boolean;
  validFrom: string | null;
}
