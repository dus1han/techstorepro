"use client";

import { useState } from "react";
import { Can } from "@/components/auth/can";
import { Money, StatusBadge } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import {
  billRepair,
  consumePart,
  logLabour,
  receiveFromVendor,
  returnPart,
  sendToVendor,
} from "@/features/repairs/api";
import {
  OUTSOURCING_STATUS_LABELS,
  OutsourcingStatus,
  REPAIR_STATUS_LABELS,
  RepairTicketStatus,
  WARRANTY_TYPE_LABELS,
  type RepairTicket,
} from "@/features/repairs/types";
import type { PagedResult } from "@/types/api";
import type { Product, Supplier } from "@/types/catalog";
import { FEATURES, PermissionAction } from "@/types/identity";

interface WarehouseOption {
  id: string;
  name: string;
}

const INPUT =
  "w-full rounded-md border border-slate-200 px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-900";

/**
 * The job sheet — one device, one fault, one workshop job (§28).
 *
 * It is hand-rolled rather than an `<EntityForm>` for the same reason the goods receipt is: a repair is a
 * *document with lines*, and a flat field list cannot express parts, labour and a vendor all hanging off
 * one job.
 *
 * The thing worth understanding about this screen is **why the buttons appear when they do**. The parts
 * and labour panels are hidden until the customer has approved the estimate, because the server refuses
 * the call until then — parts fitted to a job the customer then declines are parts the shop has paid for
 * and cannot bill. Showing a button that is going to 422 is a worse experience than not showing it.
 *
 * A warranty job never shows the estimate step at all: there is no price, so there is nobody to agree to
 * it, and the server sends it straight to the bench.
 */
export function JobSheet({
  ticketId,
  onChanged,
  onClose,
}: {
  ticketId: string;
  onChanged: () => Promise<unknown>;
  onClose: () => void;
}) {
  const { accessToken } = useAuth();
  const token = accessToken!;

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  /**
   * The panel reads the job itself rather than being handed a copy from the list.
   *
   * Every action here changes the numbers it is displaying — fitting a part moves the margin, billing it
   * fills in the invoice link, and the status walks forward. A snapshot passed down from the table would
   * be stale the moment the first button was pressed, and the operator would be looking at a job that no
   * longer exists.
   */
  const { data: ticket } = useApiQuery<RepairTicket>(["repairs", ticketId], `api/v1/repairs/${ticketId}`);

  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 200 }).data?.items ?? [];
  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];
  const vendors =
    useApiQuery<PagedResult<Supplier>>(["suppliers"], "api/v1/suppliers", { pageSize: 200 }).data?.items ?? [];

  const [part, setPart] = useState({ productId: "", warehouseId: "", quantity: "1", serialNumber: "" });
  const [labour, setLabour] = useState({ description: "", hours: "", hourlyRate: "" });
  const [vendor, setVendor] = useState({ vendorSupplierId: "", estimatedCost: "", currencyCode: "AED", exchangeRate: "1" });

  if (!ticket) {
    return (
      <div className="fixed inset-0 z-40 flex justify-end bg-slate-900/40" onClick={onClose}>
        <div className="h-full w-full max-w-2xl bg-white p-6 dark:bg-slate-950">
          <p className="text-sm text-slate-500">Loading the job sheet…</p>
        </div>
      </div>
    );
  }

  // The workshop may fit parts and book time while the job is on the bench — and while it is being
  // tested, because a machine that fails its test bench needs another part.
  const workAllowed =
    ticket.status === RepairTicketStatus.InRepair || ticket.status === RepairTicketStatus.Testing;

  const billed = ticket.salesInvoiceId !== null;

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-slate-900/40" onClick={onClose}>
      <div
        className="h-full w-full max-w-2xl overflow-y-auto bg-white p-6 shadow-xl dark:bg-slate-950"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between">
          <div>
            <div className="flex items-center gap-2">
              <h2 className="font-mono text-lg font-semibold">{ticket.number}</h2>
              <StatusBadge label={REPAIR_STATUS_LABELS[ticket.status]} tone={tone(ticket.status)} />

              {ticket.isWarranty && (
                <StatusBadge label={WARRANTY_TYPE_LABELS[ticket.warrantyType]} tone="warn" />
              )}
            </div>

            <p className="mt-1 text-sm text-slate-500">
              {ticket.customerName}
              {ticket.deviceProductName && ` · ${ticket.deviceProductName}`}
              {ticket.deviceSerialNumber && ` · ${ticket.deviceSerialNumber}`}
            </p>
          </div>

          <button onClick={onClose} className="text-sm text-slate-500 hover:text-slate-900 dark:hover:text-slate-100">
            Close
          </button>
        </div>

        {error && (
          <p role="alert" className="mt-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <section className="mt-6 space-y-1">
          <h3 className="text-xs font-medium uppercase tracking-wide text-slate-500">Reported fault</h3>
          <p className="text-sm">{ticket.reportedFault}</p>

          {ticket.accessories && (
            <p className="text-xs text-slate-500">Accessories: {ticket.accessories}</p>
          )}
          {ticket.conditionNotes && (
            <p className="text-xs text-slate-500">Condition: {ticket.conditionNotes}</p>
          )}
        </section>

        {/* The money. On a warranty job the profit is negative, and that is not a bug to hide — the parts
            still left the shelf and the vendor still charged for the board. It is the number that tells
            the shop which product line its warranty is quietly paying for. */}
        <section className="mt-6 rounded-lg border border-slate-200 p-4 dark:border-slate-800">
          <h3 className="text-xs font-medium uppercase tracking-wide text-slate-500">Profitability</h3>

          <dl className="mt-2 grid grid-cols-2 gap-y-1 text-sm">
            <dt className="text-slate-500">Charged</dt>
            <dd className="text-right"><Money amount={ticket.chargeableTotal} /></dd>

            <dt className="text-slate-500">Parts cost</dt>
            <dd className="text-right"><Money amount={ticket.partsCost} /></dd>

            {ticket.outsourcingCost > 0 && (
              <>
                <dt className="text-slate-500">Vendor cost</dt>
                <dd className="text-right"><Money amount={ticket.outsourcingCost} /></dd>
              </>
            )}

            <dt className="border-t border-slate-200 pt-1 font-medium dark:border-slate-800">Gross profit</dt>
            <dd
              className={`border-t border-slate-200 pt-1 text-right font-medium dark:border-slate-800 ${
                ticket.grossProfit < 0 ? "text-red-600 dark:text-red-400" : ""
              }`}
            >
              <Money amount={ticket.grossProfit} />
            </dd>
          </dl>

          {ticket.isWarranty && (
            <p className="mt-2 text-xs text-slate-500">
              A warranty job bills the customer nothing. The parts and the vendor are still costs, and the
              loss above is what this repair is really costing the shop.
            </p>
          )}
        </section>

        {ticket.estimatedCost !== null && (
          <p className="mt-4 text-sm text-slate-500">
            Estimated at <Money amount={ticket.estimatedCost} />
            {ticket.approvedAt && " · approved by the customer"}
          </p>
        )}

        {/* --- Parts ------------------------------------------------------------------------- */}

        <section className="mt-6">
          <h3 className="text-xs font-medium uppercase tracking-wide text-slate-500">Parts fitted</h3>

          {ticket.parts.length === 0 ? (
            <p className="mt-2 text-sm text-slate-500">Nothing fitted yet.</p>
          ) : (
            <table className="mt-2 w-full text-sm">
              <tbody>
                {ticket.parts.map((p) => (
                  <tr key={p.id} className="border-b border-slate-100 dark:border-slate-800">
                    <td className={`py-1.5 ${p.isReturned ? "text-slate-400 line-through" : ""}`}>
                      {p.productName}
                      <span className="ml-1 text-xs text-slate-500">× {p.quantity}</span>
                      {!p.isChargeable && !p.isReturned && (
                        <span className="ml-2 text-xs text-amber-600 dark:text-amber-400">not charged</span>
                      )}
                    </td>
                    <td className="py-1.5 text-right tabular-nums">
                      <Money amount={p.chargeTotal} />
                      <span className="ml-2 text-xs text-slate-500">cost <Money amount={p.costTotal} /></span>
                    </td>
                    <td className="w-16 py-1.5 text-right">
                      {!p.isReturned && !billed && (
                        <Can feature={FEATURES.repairParts} action={PermissionAction.Delete}>
                          <button
                            onClick={() => void run(() => returnPart({ token }, p.id))}
                            disabled={busy}
                            className="text-xs text-slate-500 hover:text-slate-900 disabled:opacity-40 dark:hover:text-slate-100"
                          >
                            Return
                          </button>
                        </Can>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {workAllowed && (
            <Can feature={FEATURES.repairParts} action={PermissionAction.Create}>
              <div className="mt-3 grid grid-cols-4 gap-2">
                <select
                  value={part.productId}
                  onChange={(e) => setPart({ ...part, productId: e.target.value })}
                  className={`${INPUT} col-span-2`}
                >
                  <option value="">Part…</option>
                  {products.map((p) => (
                    <option key={p.id} value={p.id}>{p.sku} — {p.name}</option>
                  ))}
                </select>

                <select
                  value={part.warehouseId}
                  onChange={(e) => setPart({ ...part, warehouseId: e.target.value })}
                  className={INPUT}
                >
                  <option value="">From…</option>
                  {warehouses.map((w) => (
                    <option key={w.id} value={w.id}>{w.name}</option>
                  ))}
                </select>

                <input
                  type="number"
                  min="0"
                  step="any"
                  value={part.quantity}
                  onChange={(e) => setPart({ ...part, quantity: e.target.value })}
                  className={INPUT}
                  aria-label="Quantity"
                />

                <input
                  value={part.serialNumber}
                  onChange={(e) => setPart({ ...part, serialNumber: e.target.value })}
                  placeholder="Serial (if the part has one)"
                  className={`${INPUT} col-span-3`}
                />

                <button
                  onClick={() => {
                    if (!part.productId || !part.warehouseId) {
                      setError("Choose the part and the warehouse it comes off.");
                      return;
                    }

                    void run(async () => {
                      await consumePart({ token }, ticket.id, {
                        productId: part.productId,
                        warehouseId: part.warehouseId,
                        quantity: Number(part.quantity) || 1,
                        serialNumber: part.serialNumber.trim() || null,
                      });

                      setPart({ productId: "", warehouseId: "", quantity: "1", serialNumber: "" });
                    });
                  }}
                  disabled={busy}
                  className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
                >
                  Fit
                </button>
              </div>
            </Can>
          )}
        </section>

        {/* --- Labour ----------------------------------------------------------------------- */}

        <section className="mt-6">
          <h3 className="text-xs font-medium uppercase tracking-wide text-slate-500">Labour</h3>

          {ticket.labour.length === 0 ? (
            <p className="mt-2 text-sm text-slate-500">No time booked yet.</p>
          ) : (
            <table className="mt-2 w-full text-sm">
              <tbody>
                {ticket.labour.map((l) => (
                  <tr key={l.id} className="border-b border-slate-100 dark:border-slate-800">
                    <td className="py-1.5">
                      {l.description}
                      <span className="ml-1 text-xs text-slate-500">
                        {l.hours}h at <Money amount={l.hourlyRate} />
                      </span>
                      {!l.isChargeable && (
                        <span className="ml-2 text-xs text-amber-600 dark:text-amber-400">not charged</span>
                      )}
                    </td>
                    <td className="py-1.5 text-right tabular-nums">
                      <Money amount={l.chargeTotal} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {workAllowed && (
            <Can feature={FEATURES.repairParts} action={PermissionAction.Create}>
              <div className="mt-3 grid grid-cols-4 gap-2">
                <input
                  value={labour.description}
                  onChange={(e) => setLabour({ ...labour, description: e.target.value })}
                  placeholder="What was done"
                  className={`${INPUT} col-span-2`}
                />
                <input
                  type="number"
                  min="0"
                  step="any"
                  value={labour.hours}
                  onChange={(e) => setLabour({ ...labour, hours: e.target.value })}
                  placeholder="Hours"
                  className={INPUT}
                />
                <input
                  type="number"
                  min="0"
                  step="any"
                  value={labour.hourlyRate}
                  onChange={(e) => setLabour({ ...labour, hourlyRate: e.target.value })}
                  placeholder="Rate"
                  className={INPUT}
                />

                <button
                  onClick={() => {
                    if (!labour.description.trim() || !labour.hours) {
                      setError("Say what was done and how long it took.");
                      return;
                    }

                    void run(async () => {
                      await logLabour({ token }, ticket.id, {
                        description: labour.description.trim(),
                        hours: Number(labour.hours),
                        hourlyRate: Number(labour.hourlyRate) || 0,
                      });

                      setLabour({ description: "", hours: "", hourlyRate: "" });
                    });
                  }}
                  disabled={busy}
                  className="col-start-4 rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
                >
                  Book time
                </button>
              </div>
            </Can>
          )}
        </section>

        {/* --- Outsourcing (§29) ------------------------------------------------------------ */}

        <section className="mt-6">
          <h3 className="text-xs font-medium uppercase tracking-wide text-slate-500">Outsourced</h3>

          {ticket.outsourcings.length === 0 ? (
            <p className="mt-2 text-sm text-slate-500">Not sent out.</p>
          ) : (
            <table className="mt-2 w-full text-sm">
              <tbody>
                {ticket.outsourcings.map((o) => (
                  <tr key={o.id} className="border-b border-slate-100 dark:border-slate-800">
                    <td className="py-1.5">
                      {o.vendorName}
                      <StatusBadge
                        label={OUTSOURCING_STATUS_LABELS[o.status]}
                        tone={o.status === OutsourcingStatus.Returned ? "good" : "neutral"}
                      />
                    </td>
                    <td className="py-1.5 text-right tabular-nums">
                      <Money amount={o.costInBaseCurrency} />
                      {o.currencyCode !== "AED" && (
                        <span className="ml-2 text-xs text-slate-500">
                          {o.cost} {o.currencyCode} at {o.exchangeRate}
                        </span>
                      )}
                    </td>
                    <td className="w-20 py-1.5 text-right">
                      {o.status === OutsourcingStatus.Sent && (
                        <Can feature={FEATURES.outsourcing} action={PermissionAction.Edit}>
                          <button
                            onClick={() => {
                              const charged = window.prompt(`What did ${o.vendorName} charge?`, String(o.cost));
                              if (charged === null) return;

                              void run(() =>
                                receiveFromVendor({ token }, o.id, { cost: Number(charged) || 0 }),
                              );
                            }}
                            disabled={busy}
                            className="text-xs text-slate-500 hover:text-slate-900 disabled:opacity-40 dark:hover:text-slate-100"
                          >
                            Received
                          </button>
                        </Can>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {workAllowed && (
            <Can feature={FEATURES.outsourcing} action={PermissionAction.Create}>
              <div className="mt-3 grid grid-cols-4 gap-2">
                <select
                  value={vendor.vendorSupplierId}
                  onChange={(e) => setVendor({ ...vendor, vendorSupplierId: e.target.value })}
                  className={`${INPUT} col-span-2`}
                >
                  <option value="">Vendor…</option>
                  {vendors.map((v) => (
                    <option key={v.id} value={v.id}>{v.name}</option>
                  ))}
                </select>

                <input
                  type="number"
                  min="0"
                  step="any"
                  value={vendor.estimatedCost}
                  onChange={(e) => setVendor({ ...vendor, estimatedCost: e.target.value })}
                  placeholder="Est. cost"
                  className={INPUT}
                />
                <input
                  value={vendor.currencyCode}
                  onChange={(e) => setVendor({ ...vendor, currencyCode: e.target.value.toUpperCase() })}
                  placeholder="AED"
                  className={INPUT}
                  maxLength={3}
                />

                <input
                  type="number"
                  min="0"
                  step="any"
                  value={vendor.exchangeRate}
                  onChange={(e) => setVendor({ ...vendor, exchangeRate: e.target.value })}
                  placeholder="Rate"
                  className={INPUT}
                  aria-label="Exchange rate"
                />

                <button
                  onClick={() => {
                    if (!vendor.vendorSupplierId) {
                      setError("Choose the vendor the job is going to.");
                      return;
                    }

                    void run(async () => {
                      await sendToVendor({ token }, ticket.id, {
                        vendorSupplierId: vendor.vendorSupplierId,
                        estimatedCost: Number(vendor.estimatedCost) || 0,
                        currencyCode: vendor.currencyCode || "AED",
                        exchangeRate: Number(vendor.exchangeRate) || 1,
                      });

                      setVendor({ vendorSupplierId: "", estimatedCost: "", currencyCode: "AED", exchangeRate: "1" });
                    });
                  }}
                  disabled={busy}
                  className="col-start-4 rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
                >
                  Send out
                </button>
              </div>
            </Can>
          )}
        </section>

        {/* --- The bill --------------------------------------------------------------------- */}

        {!ticket.isWarranty && !billed && ticket.chargeableTotal > 0 && (
          <Can feature={FEATURES.repairTickets} action={PermissionAction.Create}>
            <button
              onClick={() => void run(() => billRepair({ token }, ticket.id))}
              disabled={busy}
              className="mt-6 w-full rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white hover:opacity-90 disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
            >
              Raise the bill — <Money amount={ticket.chargeableTotal} /> plus tax
            </button>
          </Can>
        )}

        {billed && (
          <p className="mt-6 rounded-md bg-emerald-50 px-3 py-2 text-sm text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300">
            Invoiced. The bill is an ordinary sales invoice, so it is paid, credited and chased like any
            other — find it on the invoices screen.
          </p>
        )}

        {/* --- The trail -------------------------------------------------------------------- */}

        <section className="mt-6">
          <h3 className="text-xs font-medium uppercase tracking-wide text-slate-500">History</h3>

          <ol className="mt-2 space-y-1 text-xs text-slate-500">
            {ticket.statusHistory.map((h, i) => (
              <li key={i}>
                {new Date(h.changedAt).toLocaleString()} — {REPAIR_STATUS_LABELS[h.toStatus]}
                {h.notes && <span className="text-slate-400"> · {h.notes}</span>}
              </li>
            ))}
          </ol>
        </section>
      </div>
    </div>
  );

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);

    try {
      await action();
      await onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "That did not work.");
    } finally {
      setBusy(false);
    }
  }
}

export function tone(status: RepairTicketStatus) {
  if (status === RepairTicketStatus.Delivered) return "good" as const;
  if (status === RepairTicketStatus.Cancelled) return "bad" as const;
  if (status === RepairTicketStatus.AwaitingApproval) return "warn" as const;
  if (status === RepairTicketStatus.Ready) return "good" as const;

  return "neutral" as const;
}
