import { api } from "@/lib/api-client";
import type {
  ApportionmentBasis,
  ApportionmentResult,
  ImportChargeType,
} from "@/features/purchasing/types";

/**
 * The purchasing module's calls into the API.
 *
 * **Every state-changing call here sends an `Idempotency-Key`**, for the same reason sales does: this is
 * a module where a double-click costs real money. Receiving the same container twice would put stock on
 * the shelf that does not exist and pay a supplier for goods that never arrived; apportioning it twice
 * would fold the freight into the moving average a second time, where — because the average is moving —
 * it would never wash back out.
 *
 * The key is generated **once per attempt**, not per render: a key that changed on re-render would be a
 * new request every time, which is exactly no protection at all.
 */

function newIdempotencyKey() {
  return crypto.randomUUID();
}

interface Auth {
  token: string;
}

// --- Purchase orders ----------------------------------------------------------------------------

export interface PurchaseOrderLineInput {
  productId: string;
  quantity: number;
  unitPrice: number;
  discountPercent?: number;
  notes?: string | null;
}

/** Raises a draft. Nothing is committed until it is approved. */
export function createPurchaseOrder(
  { token }: Auth,
  body: {
    supplierId: string;
    branchId: string;
    warehouseId: string;
    lines: PurchaseOrderLineInput[];
    currencyCode?: string;
    exchangeRate?: number;
    expectedAt?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/purchase-orders", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** **Commits the company's money** — and it is the gate on receiving. A draft cannot take delivery. */
export function approvePurchaseOrder({ token }: Auth, id: string) {
  return api.post<void>(`api/v1/purchase-orders/${id}/approve`, {
    token,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function cancelPurchaseOrder({ token }: Auth, id: string, reason: string) {
  return api.post<void>(`api/v1/purchase-orders/${id}/cancel`, {
    token,
    body: { purchaseOrderId: id, reason },
  });
}

// --- Goods receipts -----------------------------------------------------------------------------

export interface ReceiveLineInput {
  productId: string;
  quantity: number;
  unitPrice: number;
  discountPercent?: number;
  purchaseOrderLineId?: string | null;
  /** One per unit, for a serial-tracked product. Captured at the door, not at the sale. */
  serialNumbers?: string[] | null;
  notes?: string | null;
}

/**
 * The only call in purchasing that moves stock, and the one where serials bind.
 *
 * The purchase order is optional (§25) and so is the shipment: a direct purchase from the wholesaler
 * down the road passes neither, and that is a first-class path, not a workaround.
 */
export function receiveGoods(
  { token }: Auth,
  body: {
    supplierId: string;
    branchId: string;
    warehouseId: string;
    lines: ReceiveLineInput[];
    purchaseOrderId?: string | null;
    importShipmentId?: string | null;
    currencyCode?: string;
    exchangeRate?: number;
    supplierReference?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/goods-receipts", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

// --- Supplier invoices --------------------------------------------------------------------------

export interface SupplierInvoiceLineInput {
  description: string;
  quantity: number;
  unitPrice: number;
  productId?: string | null;
  discountPercent?: number;
  taxPercent?: number;
}

/** Records the bill. It does **not** touch stock — the goods receipt already did that. */
export function recordSupplierInvoice(
  { token }: Auth,
  body: {
    supplierId: string;
    branchId: string;
    supplierReference: string;
    lines: SupplierInvoiceLineInput[];
    goodsReceiptId?: string | null;
    currencyCode?: string;
    exchangeRate?: number;
    dueAt?: string | null;
    post?: boolean;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/supplier-invoices", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** Posting is what puts the debt on the supplier's balance. A draft owes nothing. */
export function postSupplierInvoice({ token }: Auth, id: string) {
  return api.post<void>(`api/v1/supplier-invoices/${id}/post`, {
    token,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function cancelSupplierInvoice({ token }: Auth, id: string, reason: string) {
  return api.post<void>(`api/v1/supplier-invoices/${id}/cancel`, {
    token,
    body: { supplierInvoiceId: id, reason },
  });
}

// --- Supplier payments --------------------------------------------------------------------------

/**
 * Pays a supplier — a header plus allocations, so one transfer can settle three invoices and one invoice
 * can be settled by two instalments.
 *
 * `exchangeRate` is the rate on the day the money leaves the bank, **not** the rate the invoice was
 * booked at. The gap between the two is the realised FX gain or loss, and it is why this is a field
 * rather than a lookup.
 */
export function paySupplier(
  { token }: Auth,
  body: {
    supplierId: string;
    branchId: string;
    paymentMethodId: string;
    amount: number;
    allocations?: { supplierInvoiceId: string; amount: number }[] | null;
    currencyCode?: string;
    exchangeRate?: number;
    reference?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/supplier-payments", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

// --- Import shipments and landed cost -----------------------------------------------------------

export function createImportShipment(
  { token }: Auth,
  body: {
    supplierId: string;
    branchId: string;
    transportDocument?: string | null;
    vesselOrFlight?: string | null;
    portOfLoading?: string | null;
    portOfDischarge?: string | null;
    shippedAt?: string | null;
    expectedAt?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/import-shipments", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** Bills the container for something that is not the goods: freight, duty, insurance, clearing. */
export function addImportCharge(
  { token }: Auth,
  shipmentId: string,
  body: {
    type: ImportChargeType;
    amount: number;
    currencyCode?: string;
    exchangeRate?: number;
    description?: string | null;
    vendor?: string | null;
    reference?: string | null;
  },
) {
  return api.post<string>(`api/v1/import-shipments/${shipmentId}/charges`, {
    token,
    body: { ...body, shipmentId },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/**
 * **Folds the container's charges into the cost of its goods** (D6: by value).
 *
 * The most consequential call in the module. Costing is weighted average, so this does not merely price
 * this container — it feeds the moving average of every product in it and spreads to units that arrived
 * years ago, where it never washes out. It can be done exactly once, and it is gated on `Approve`.
 */
export function apportionLandedCost(
  { token }: Auth,
  shipmentId: string,
  basis?: ApportionmentBasis,
) {
  const query = basis ? `?basis=${basis}` : "";

  return api.post<ApportionmentResult>(`api/v1/import-shipments/${shipmentId}/apportion${query}`, {
    token,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}
