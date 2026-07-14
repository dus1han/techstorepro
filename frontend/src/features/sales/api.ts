import { api } from "@/lib/api-client";
import type { CounterSaleResult, RefundMethod } from "@/features/sales/types";

/**
 * The sales module's calls into the API.
 *
 * **Every state-changing call here sends an `Idempotency-Key`.** That is not belt and braces: this is
 * the module where a double-click costs real money. The cashier taps "Take payment", the network
 * hesitates, they tap again — and without the key the shop has sold the laptop twice, taken the money
 * twice, and issued two invoices that both look entirely legitimate. Nobody notices until they count the
 * drawer.
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

export interface TenderInput {
  paymentMethodId: string;
  amount: number;
  reference?: string | null;
}

export interface CounterSaleLineInput {
  productId: string;
  quantity: number;
  serialNumbers?: string[] | null;
  unitPrice?: number | null;
  discountPercent?: number;
  discountAmount?: number;
}

/** The till: goods out, bill raised, money taken — one call, one transaction. */
export function sellAtCounter(
  { token }: Auth,
  body: {
    customerId: string;
    branchId: string;
    warehouseId: string;
    lines: CounterSaleLineInput[];
    methods: TenderInput[];
    discountApprovedBy?: string | null;
    notes?: string | null;
  },
) {
  return api.post<CounterSaleResult>("api/v1/pos/sales", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function recordPayment(
  { token }: Auth,
  body: {
    customerId: string;
    branchId: string;
    methods: TenderInput[];
    allocations?: { salesInvoiceId: string; amount: number }[] | null;
    reference?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/customer-payments", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function issueCreditNote(
  { token }: Auth,
  body: {
    salesInvoiceId: string;
    lines: { salesInvoiceLineId: string; quantity: number; serialNumbers?: string[] | null; restock: boolean }[];
    refund: RefundMethod;
    reason: string;
    warehouseId?: string | null;
  },
) {
  return api.post<string>("api/v1/credit-notes", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function createQuotation(
  { token }: Auth,
  body: {
    branchId: string;
    customerId?: string | null;
    lines: { productId: string; quantity: number; unitPrice?: number | null; discountPercent?: number }[];
    validUntil?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/quotations", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function acceptQuotation({ token }: Auth, id: string) {
  return api.post<void>(`api/v1/quotations/${id}/accept`, { token });
}

export function rejectQuotation({ token }: Auth, id: string, reason: string) {
  return api.post<void>(`api/v1/quotations/${id}/reject`, {
    token,
    body: { quotationId: id, reason },
  });
}

/** Turns a quotation into an order **at the price that was quoted** — it does not re-price. */
export function convertQuotation({ token }: Auth, id: string, warehouseId: string) {
  return api.post<string>(`api/v1/quotations/${id}/convert`, {
    token,
    body: { quotationId: id, warehouseId },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** Confirming is what RESERVES the stock and checks the credit limit. */
export function confirmOrder({ token }: Auth, id: string) {
  return api.post<void>(`api/v1/sales-orders/${id}/confirm`, {
    token,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function cancelOrder({ token }: Auth, id: string, reason: string) {
  return api.post<void>(`api/v1/sales-orders/${id}/cancel`, {
    token,
    body: { salesOrderId: id, reason },
  });
}

/** The only call in sales that moves stock. Serial numbers bind here, at the door. */
export function deliver(
  { token }: Auth,
  body: {
    branchId: string;
    warehouseId: string;
    salesOrderId?: string | null;
    customerId?: string | null;
    lines: { productId: string; quantity: number; serialNumbers?: string[] | null }[];
    deliveredTo?: string | null;
  },
) {
  return api.post<string>("api/v1/deliveries", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function raiseInvoice({ token }: Auth, deliveryId: string) {
  return api.post<string>("api/v1/sales-invoices", {
    token,
    body: { deliveryId },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}
