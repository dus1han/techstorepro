import { api } from "@/lib/api-client";
import type { RepairWarrantyType, WarrantyCover } from "@/features/repairs/types";

/**
 * The repairs module's calls into the API.
 *
 * **The state-changing calls send an `Idempotency-Key`**, and the one that matters most is
 * {@link consumePart}: a double-clicked "fit part" would take two screens off the shelf and put one of
 * them nowhere. The same goes for {@link billRepair} — twice and the customer owes for the repair twice.
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

// --- Intake -------------------------------------------------------------------------------------

/**
 * Book a device in over the counter (§28).
 *
 * **No stock moves** — the machine on the bench belongs to the customer. What the server does here that
 * the client cannot is answer the warranty question: it looks the serial up, finds the sale that put the
 * machine in the customer's hands, and decides whether this repair is free. Nobody ticks a box.
 */
export function bookInDevice(
  { token }: Auth,
  body: {
    customerId: string;
    branchId: string;
    reportedFault: string;
    deviceProductId?: string | null;
    deviceSerialNumber?: string | null;
    accessories?: string | null;
    conditionNotes?: string | null;
    technicianId?: string | null;
    promisedAt?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>("api/v1/repairs", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/**
 * "Is this machine still under warranty?" — asked from the counter before anything is booked in, because
 * the customer is standing there and wants to know.
 */
export function checkWarranty({ token }: Auth, serialNumber: string) {
  return api.get<WarrantyCover>("api/v1/warranties/check", {
    token,
    query: { serialNumber },
  });
}

// --- The workshop workflow ----------------------------------------------------------------------

export function beginDiagnosis({ token }: Auth, id: string, technicianId?: string | null) {
  return api.post<void>(`api/v1/repairs/${id}/diagnose`, {
    token,
    body: { repairTicketId: id, technicianId },
  });
}

/**
 * The findings and the estimate.
 *
 * A chargeable job now waits for the customer; a **warranty job goes straight to the bench**, because
 * there is no price for anyone to agree to.
 */
export function recordDiagnosis(
  { token }: Auth,
  id: string,
  body: {
    findings: string;
    recommendedAction?: string | null;
    estimatedCost?: number | null;
    technicianId?: string | null;
  },
) {
  return api.post<void>(`api/v1/repairs/${id}/diagnosis`, {
    token,
    body: { ...body, repairTicketId: id },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** The customer said yes. **This is what unlocks the parts store.** */
export function approveEstimate({ token }: Auth, id: string) {
  return api.post<void>(`api/v1/repairs/${id}/approve`, {
    token,
    body: { repairTicketId: id },
  });
}

/** The customer said no. The device goes back untouched. */
export function declineEstimate({ token }: Auth, id: string, reason: string) {
  return api.post<void>(`api/v1/repairs/${id}/decline`, {
    token,
    body: { repairTicketId: id, reason },
  });
}

export function beginTesting({ token }: Auth, id: string) {
  return api.post<void>(`api/v1/repairs/${id}/test`, {
    token,
    body: { repairTicketId: id },
  });
}

export function markReady({ token }: Auth, id: string) {
  return api.post<void>(`api/v1/repairs/${id}/ready`, {
    token,
    body: { repairTicketId: id },
  });
}

/** The customer collects the machine. It does **not** require the bill to be paid. */
export function deliverDevice({ token }: Auth, id: string, collectedBy?: string | null) {
  return api.post<void>(`api/v1/repairs/${id}/deliver`, {
    token,
    body: { repairTicketId: id, collectedBy },
  });
}

export function cancelRepair({ token }: Auth, id: string, reason: string) {
  return api.post<void>(`api/v1/repairs/${id}/cancel`, {
    token,
    body: { repairTicketId: id, reason },
  });
}

// --- Parts, labour and the vendor ---------------------------------------------------------------

/**
 * Fit a part into the customer's machine.
 *
 * **The only call in this module that moves stock** — and it moves it now, not at invoicing: the part is
 * physically inside the machine whether or not anyone ever pays for it. Refused until the customer has
 * approved the estimate.
 */
export function consumePart(
  { token }: Auth,
  id: string,
  body: {
    productId: string;
    warehouseId: string;
    quantity: number;
    serialNumber?: string | null;
    unitPrice?: number | null;
    isChargeable?: boolean | null;
    notes?: string | null;
  },
) {
  return api.post<string>(`api/v1/repairs/${id}/parts`, {
    token,
    body: { ...body, repairTicketId: id },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** Take a part back out and put it on the shelf — a real movement, not an undo. */
export function returnPart({ token }: Auth, partId: string, notes?: string | null) {
  return api.post<void>(`api/v1/repairs/parts/${partId}/return`, {
    token,
    body: { repairPartId: partId, notes },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function logLabour(
  { token }: Auth,
  id: string,
  body: {
    description: string;
    hours: number;
    hourlyRate: number;
    technicianId?: string | null;
    isChargeable?: boolean | null;
  },
) {
  return api.post<string>(`api/v1/repairs/${id}/labour`, {
    token,
    body: { ...body, repairTicketId: id },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/** Send the job out to a third party (§29). No stock moves; a cost lands on the ticket. */
export function sendToVendor(
  { token }: Auth,
  id: string,
  body: {
    vendorSupplierId: string;
    estimatedCost?: number | null;
    currencyCode?: string | null;
    exchangeRate?: number | null;
    expectedAt?: string | null;
    notes?: string | null;
  },
) {
  return api.post<string>(`api/v1/repairs/${id}/outsource`, {
    token,
    body: { ...body, repairTicketId: id },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function receiveFromVendor(
  { token }: Auth,
  outsourcingId: string,
  body: { cost: number; notes?: string | null },
) {
  return api.post<void>(`api/v1/repairs/outsourcing/${outsourcingId}/receive`, {
    token,
    body: { ...body, outsourcingId },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

/**
 * Bill the job.
 *
 * It raises an ordinary sales invoice and moves no stock — the parts left the shelf when they were fitted.
 * A wholly-warranty job has nothing to bill, and the server says so rather than issuing an invoice for
 * zero.
 */
export function billRepair({ token }: Auth, id: string) {
  return api.post<string>(`api/v1/repairs/${id}/invoice`, {
    token,
    body: { repairTicketId: id },
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

// --- Warranties ---------------------------------------------------------------------------------

/**
 * Register a **manufacturer's or a supplier's** warranty.
 *
 * The shop's own is not registered here and the server refuses it: P5 stamps it on the unit at the moment
 * of sale, from the product's warranty months, and a second copy could disagree with the first.
 */
export function registerWarranty(
  { token }: Auth,
  body: {
    warrantyType: RepairWarrantyType;
    productId: string;
    startsOn: string;
    endsOn: string;
    serialNumber?: string | null;
    terms?: string | null;
  },
) {
  return api.post<string>("api/v1/warranties", {
    token,
    body,
    headers: { "Idempotency-Key": newIdempotencyKey() },
  });
}

export function acceptClaim({ token }: Auth, claimId: string, outcome?: string | null) {
  return api.post<void>(`api/v1/warranties/claims/${claimId}/accept`, {
    token,
    body: { warrantyClaimId: claimId, outcome },
  });
}

/**
 * The fault was not covered.
 *
 * **This is the decision that makes the job chargeable** — the parts and labour already booked to it as
 * warranty work become billable, and the shop stops eating them.
 */
export function rejectClaim({ token }: Auth, claimId: string, outcome: string) {
  return api.post<void>(`api/v1/warranties/claims/${claimId}/reject`, {
    token,
    body: { warrantyClaimId: claimId, outcome },
  });
}
