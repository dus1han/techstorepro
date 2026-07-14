/**
 * The purchasing module's API contracts (P4).
 *
 * The numeric enums are the wire format (smallint), so they must match Domain/Purchasing exactly. A
 * drifted value here does not fail to compile — it silently renders "Draft" for an approved order, which
 * is the kind of bug that survives a demo.
 */

export enum PurchaseOrderStatus {
  Draft = 1,
  Approved = 2,
  PartiallyReceived = 3,
  Received = 4,
  Cancelled = 5,
}

export enum SupplierInvoiceStatus {
  Draft = 1,
  Posted = 2,
  PartiallyPaid = 3,
  Paid = 4,
  Cancelled = 5,
}

export enum ImportShipmentStatus {
  InTransit = 1,
  Arrived = 2,
  /** Charges apportioned and folded into inventory. The container's true cost is known — and fixed. */
  Costed = 3,
  Cancelled = 4,
}

export enum ImportChargeType {
  Freight = 1,
  Insurance = 2,
  Customs = 3,
  Clearing = 4,
  Handling = 5,
  Other = 6,
}

export enum ApportionmentBasis {
  ByValue = 1,
  ByQuantity = 2,
  ByWeight = 3,
}

export const PO_STATUS_LABELS: Record<PurchaseOrderStatus, string> = {
  [PurchaseOrderStatus.Draft]: "Draft",
  [PurchaseOrderStatus.Approved]: "Approved",
  [PurchaseOrderStatus.PartiallyReceived]: "Part received",
  [PurchaseOrderStatus.Received]: "Received",
  [PurchaseOrderStatus.Cancelled]: "Cancelled",
};

export const SUPPLIER_INVOICE_STATUS_LABELS: Record<SupplierInvoiceStatus, string> = {
  [SupplierInvoiceStatus.Draft]: "Draft",
  [SupplierInvoiceStatus.Posted]: "Unpaid",
  [SupplierInvoiceStatus.PartiallyPaid]: "Part paid",
  [SupplierInvoiceStatus.Paid]: "Paid",
  [SupplierInvoiceStatus.Cancelled]: "Cancelled",
};

export const SHIPMENT_STATUS_LABELS: Record<ImportShipmentStatus, string> = {
  [ImportShipmentStatus.InTransit]: "In transit",
  [ImportShipmentStatus.Arrived]: "Arrived",
  [ImportShipmentStatus.Costed]: "Costed",
  [ImportShipmentStatus.Cancelled]: "Cancelled",
};

export const CHARGE_TYPE_LABELS: Record<ImportChargeType, string> = {
  [ImportChargeType.Freight]: "Freight",
  [ImportChargeType.Insurance]: "Insurance",
  [ImportChargeType.Customs]: "Customs duty",
  [ImportChargeType.Clearing]: "Clearing",
  [ImportChargeType.Handling]: "Handling",
  [ImportChargeType.Other]: "Other",
};

export interface PurchaseOrderLine {
  id: string;
  productId: string;
  productName: string;
  productSku: string;
  quantity: number;
  receivedQuantity: number;
  outstandingQuantity: number;
  unitPrice: number;
  discountPercent: number;
  lineTotal: number;
  notes: string | null;
}

export interface PurchaseOrder {
  id: string;
  number: string;
  supplierId: string;
  supplierName: string;
  branchId: string;
  warehouseId: string;
  status: PurchaseOrderStatus;
  currencyCode: string;
  exchangeRate: number;
  orderedAt: string;
  expectedAt: string | null;
  approvedAt: string | null;
  /** In the supplier's currency. */
  total: number;
  /** In the company's own money — what the commitment actually costs. */
  totalBase: number;
  isFullyReceived: boolean;
  notes: string | null;
  lines: PurchaseOrderLine[];
}

export interface GoodsReceiptLine {
  id: string;
  productId: string;
  productName: string;
  productSku: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  lineTotal: number;
  /** The container's charges folded into this line. Zero until the shipment is costed. */
  apportionedCost: number;
  /** What the ledger actually booked, per unit: goods price plus its share of the freight. */
  landedUnitCost: number;
  /** Captured at the door. This is what makes a warranty claim answerable two years later. */
  serials: string[];
  notes: string | null;
}

export interface GoodsReceipt {
  id: string;
  number: string;
  supplierId: string;
  supplierName: string;
  branchId: string;
  warehouseId: string;
  warehouseName: string;
  /** Null on a direct purchase — §25 makes the order optional, and means it. */
  purchaseOrderId: string | null;
  purchaseOrderNumber: string | null;
  importShipmentId: string | null;
  importShipmentNumber: string | null;
  currencyCode: string;
  exchangeRate: number;
  supplierReference: string | null;
  receivedAt: string;
  goodsTotal: number;
  goodsTotalBase: number;
  notes: string | null;
  lines: GoodsReceiptLine[];
}

export interface SupplierInvoiceLine {
  id: string;
  productId: string | null;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  taxPercent: number;
  netTotal: number;
  taxAmount: number;
  lineTotal: number;
}

export interface SupplierInvoice {
  id: string;
  number: string;
  /** The supplier's own number — what they will quote when they chase payment. */
  supplierReference: string;
  supplierId: string;
  supplierName: string;
  branchId: string;
  goodsReceiptId: string | null;
  goodsReceiptNumber: string | null;
  status: SupplierInvoiceStatus;
  currencyCode: string;
  exchangeRate: number;
  invoicedAt: string;
  dueAt: string | null;
  total: number;
  /** What the company owes in its own money, fixed at the invoice-date rate. */
  totalBase: number;
  paidAmount: number;
  outstandingAmount: number;
  notes: string | null;
  lines: SupplierInvoiceLine[];
}

export interface SupplierPaymentAllocation {
  id: string;
  supplierInvoiceId: string;
  supplierInvoiceNumber: string;
  amount: number;
  invoiceExchangeRate: number;
  paymentExchangeRate: number;
  /**
   * Positive is a gain: the debt cost less to settle than it was booked at. It is P&L, not a reduction
   * in the cost of the stock — the goods did not become cheaper to buy, the currency moved.
   */
  exchangeGainOrLoss: number;
}

export interface SupplierPayment {
  id: string;
  number: string;
  supplierId: string;
  supplierName: string;
  branchId: string;
  paymentMethodId: string;
  paymentMethodName: string;
  reference: string | null;
  amount: number;
  currencyCode: string;
  exchangeRate: number;
  amountBase: number;
  allocatedAmount: number;
  /** Paid but not yet pointed at an invoice — an advance. A real state, not an error. */
  unallocatedAmount: number;
  exchangeGainOrLoss: number;
  paidAt: string;
  notes: string | null;
  allocations: SupplierPaymentAllocation[];
}

export interface ImportCharge {
  id: string;
  type: ImportChargeType;
  description: string | null;
  vendor: string | null;
  amount: number;
  currencyCode: string;
  exchangeRate: number;
  amountBase: number;
  incurredAt: string;
}

export interface ImportShipment {
  id: string;
  number: string;
  supplierId: string;
  supplierName: string;
  status: ImportShipmentStatus;
  transportDocument: string | null;
  vesselOrFlight: string | null;
  shippedAt: string | null;
  expectedAt: string | null;
  arrivedAt: string | null;
  costedAt: string | null;
  /** Freight, duty, insurance, clearing — summed in the company's own money. */
  totalCharges: number;
  /**
   * Charges that reached the shipment after its goods had already been sold. Real money with nowhere in
   * inventory to live — reported rather than dropped (which would overstate margin) or smeared over
   * whatever else is on the shelf (which would charge one container's freight to another's goods).
   */
  unabsorbedCost: number;
  receiptCount: number;
  charges: ImportCharge[];
}

export interface ApportionedLine {
  goodsReceiptLineId: string;
  productId: string;
  productName: string;
  quantity: number;
  lineValueBase: number;
  apportionedCost: number;
  /** The number that feeds the moving average. Getting it wrong spreads to stock that arrived years ago. */
  landedUnitCost: number;
  /** How much of this line was still on the shelf to carry the cost. */
  quantityStillInStock: number;
  absorbedCost: number;
}

/** What the apportionment hands back: what the container's charges reached, and what they could not. */
export interface ApportionmentResult {
  shipmentId: string;
  totalCharges: number;
  absorbed: number;
  unabsorbed: number;
  lines: ApportionedLine[];
}
