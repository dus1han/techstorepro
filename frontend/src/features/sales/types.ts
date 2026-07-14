/**
 * The sales module's API contracts (P5).
 *
 * The numeric enums are the wire format (smallint), so they must match Domain/Sales exactly. A drifted
 * value here does not fail to compile — it silently renders "Draft" for a posted invoice, which is the
 * kind of bug that survives a demo.
 */

export enum QuotationStatus {
  Draft = 1,
  Sent = 2,
  Accepted = 3,
  Rejected = 4,
  Expired = 5,
  Converted = 6,
}

export enum SalesOrderStatus {
  Draft = 1,
  Confirmed = 2,
  PartiallyDelivered = 3,
  Delivered = 4,
  Cancelled = 5,
}

export enum DeliveryStatus {
  Delivered = 1,
  Invoiced = 2,
  Cancelled = 3,
}

export enum SalesInvoiceStatus {
  Draft = 1,
  Posted = 2,
  PartiallyPaid = 3,
  Paid = 4,
  Cancelled = 5,
}

export enum RefundMethod {
  CashRefund = 1,
  BankRefund = 2,
  StoreCredit = 3,
  OffsetAgainstBalance = 4,
}

/** Mirrors Domain/Catalog/PaymentMethodKind. Store credit is tender, not a discount. */
export enum PaymentMethodKind {
  Cash = 1,
  BankTransfer = 2,
  Card = 3,
  Cheque = 4,
  Online = 5,
  Custom = 6,
  StoreCredit = 7,
}

export const QUOTATION_STATUS_LABELS: Record<QuotationStatus, string> = {
  [QuotationStatus.Draft]: "Draft",
  [QuotationStatus.Sent]: "Sent",
  [QuotationStatus.Accepted]: "Accepted",
  [QuotationStatus.Rejected]: "Rejected",
  [QuotationStatus.Expired]: "Expired",
  [QuotationStatus.Converted]: "Converted",
};

export const ORDER_STATUS_LABELS: Record<SalesOrderStatus, string> = {
  [SalesOrderStatus.Draft]: "Draft",
  [SalesOrderStatus.Confirmed]: "Confirmed",
  [SalesOrderStatus.PartiallyDelivered]: "Part delivered",
  [SalesOrderStatus.Delivered]: "Delivered",
  [SalesOrderStatus.Cancelled]: "Cancelled",
};

export const DELIVERY_STATUS_LABELS: Record<DeliveryStatus, string> = {
  [DeliveryStatus.Delivered]: "Delivered",
  [DeliveryStatus.Invoiced]: "Invoiced",
  [DeliveryStatus.Cancelled]: "Cancelled",
};

export const INVOICE_STATUS_LABELS: Record<SalesInvoiceStatus, string> = {
  [SalesInvoiceStatus.Draft]: "Draft",
  [SalesInvoiceStatus.Posted]: "Unpaid",
  [SalesInvoiceStatus.PartiallyPaid]: "Part paid",
  [SalesInvoiceStatus.Paid]: "Paid",
  [SalesInvoiceStatus.Cancelled]: "Cancelled",
};

export const REFUND_LABELS: Record<RefundMethod, string> = {
  [RefundMethod.CashRefund]: "Cash refund",
  [RefundMethod.BankRefund]: "Bank refund",
  [RefundMethod.StoreCredit]: "Store credit",
  [RefundMethod.OffsetAgainstBalance]: "Offset against balance",
};

export interface SalesLine {
  id: string;
  productId: string | null;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  discountAmount: number;
  taxPercent: number;
  netTotal: number;
  taxAmount: number;
  lineTotal: number;
  priceSource: string | null;
}

export interface Quotation {
  id: string;
  number: string;
  customerId: string | null;
  customerName: string | null;
  status: QuotationStatus;
  currencyCode: string;
  quotedAt: string;
  validUntil: string | null;
  netTotal: number;
  taxTotal: number;
  total: number;
  notes: string | null;
  lines: SalesLine[];
}

export interface SalesOrderLine extends SalesLine {
  deliveredQuantity: number;
  outstandingQuantity: number;
  stockReservationId: string | null;
}

export interface SalesOrder {
  id: string;
  number: string;
  customerId: string;
  customerName: string;
  branchId: string;
  warehouseId: string;
  quotationId: string | null;
  status: SalesOrderStatus;
  currencyCode: string;
  orderedAt: string;
  expectedAt: string | null;
  netTotal: number;
  taxTotal: number;
  total: number;
  notes: string | null;
  lines: SalesOrderLine[];
}

export interface DeliveryLine {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitCost: number;
  costTotal: number;
  /** The machines that went out of the door. This is what P6's warranty claim follows back. */
  serials: string[];
}

export interface Delivery {
  id: string;
  number: string;
  customerId: string;
  customerName: string;
  branchId: string;
  warehouseId: string;
  salesOrderId: string | null;
  status: DeliveryStatus;
  deliveredAt: string;
  deliveredTo: string | null;
  costTotal: number;
  notes: string | null;
  lines: DeliveryLine[];
}

export interface InvoiceLine extends SalesLine {
  unitCost: number;
  grossProfit: number;
  serials: string[];
}

export interface SalesInvoice {
  id: string;
  number: string;
  customerId: string;
  customerName: string;
  branchId: string;
  salesOrderId: string | null;
  deliveryId: string | null;
  status: SalesInvoiceStatus;
  currencyCode: string;
  invoicedAt: string;
  dueAt: string | null;
  netTotal: number;
  taxTotal: number;
  total: number;
  costTotal: number;
  grossProfit: number;
  paidAmount: number;
  outstandingAmount: number;
  notes: string | null;
  lines: InvoiceLine[];
}

export interface PaymentMethodLine {
  id: string;
  paymentMethodId: string;
  paymentMethodName: string;
  amount: number;
  reference: string | null;
}

export interface PaymentAllocation {
  id: string;
  salesInvoiceId: string;
  invoiceNumber: string;
  amount: number;
}

export interface CustomerPayment {
  id: string;
  number: string;
  customerId: string;
  customerName: string;
  currencyCode: string;
  paidAt: string;
  reference: string | null;
  amount: number;
  allocatedAmount: number;
  /** Money not yet pointed at an invoice — a credit, not an error. */
  unallocatedAmount: number;
  notes: string | null;
  methods: PaymentMethodLine[];
  allocations: PaymentAllocation[];
}

export interface CreditNoteLine {
  id: string;
  salesInvoiceLineId: string;
  productId: string | null;
  description: string;
  quantity: number;
  unitPrice: number;
  taxPercent: number;
  netTotal: number;
  taxAmount: number;
  lineTotal: number;
  restockedToShelf: boolean;
}

export interface CreditNote {
  id: string;
  number: string;
  customerId: string;
  customerName: string;
  salesInvoiceId: string;
  invoiceNumber: string;
  status: number;
  refundMethod: RefundMethod;
  currencyCode: string;
  issuedAt: string;
  reason: string;
  netTotal: number;
  taxTotal: number;
  total: number;
  lines: CreditNoteLine[];
}

export interface StoreCreditEntry {
  id: string;
  amount: number;
  occurredAt: string;
  reason: string;
  creditNoteId: string | null;
  customerPaymentId: string | null;
}

export interface StoreCredit {
  customerId: string;
  customerName: string;
  /** The SUM of the entries — never a stored number, so it can always explain itself. */
  balance: number;
  entries: StoreCreditEntry[];
}

/** What the till hands back after a counter sale. */
export interface CounterSaleResult {
  deliveryId: string;
  invoiceId: string;
  paymentId: string;
  invoiceNumber: string;
  total: number;
  paid: number;
  change: number;
}

export interface PaymentMethod {
  id: string;
  name: string;
  kind: PaymentMethodKind;
  requiresReference: boolean;
  isActive: boolean;
}
