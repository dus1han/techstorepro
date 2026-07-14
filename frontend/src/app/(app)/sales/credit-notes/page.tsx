"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { issueCreditNote } from "@/features/sales/api";
import { Money, StatusBadge } from "@/components/ui/money";
import {
  REFUND_LABELS,
  RefundMethod,
  SalesInvoiceStatus,
  type CreditNote,
  type SalesInvoice,
} from "@/features/sales/types";
import type { PagedResult } from "@/types/api";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * Returns and credit notes (requirements §24).
 *
 * **A credit note is the only thing in sales that puts stock back** — and it is not a cancelled invoice.
 * Cancelling paperwork does not un-deliver goods, and a paid invoice cannot be cancelled at all.
 *
 * Returned serials go to `Returned`, never straight back to `InStock`: a machine that came back is
 * inspected before it is sold to somebody else.
 */
export default function CreditNotesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [returning, setReturning] = useState(false);

  const columns: Column<CreditNote>[] = [
    {
      key: "number",
      header: "Credit note",
      render: (c) => (
        <div>
          <p className="font-mono text-xs">{c.number}</p>
          <p className="text-xs text-slate-500">{new Date(c.issuedAt).toLocaleDateString()}</p>
        </div>
      ),
    },
    {
      key: "customer",
      header: "Customer",
      render: (c) => (
        <div>
          <p>{c.customerName}</p>
          <p className="font-mono text-xs text-slate-500">against {c.invoiceNumber}</p>
        </div>
      ),
    },
    { key: "reason", header: "Reason", render: (c) => <span className="text-sm">{c.reason}</span> },
    {
      key: "refund",
      header: "Refund",
      render: (c) => (
        <div className="space-y-1">
          <StatusBadge
            label={REFUND_LABELS[c.refundMethod]}
            tone={c.refundMethod === RefundMethod.StoreCredit ? "warn" : "neutral"}
          />
          {/* Goods that did not come back to the shelf: faulty, or nothing physical returned at all. */}
          {c.lines.some((l) => !l.restockedToShelf) && (
            <p className="text-xs text-slate-500">not restocked</p>
          )}
        </div>
      ),
    },
    {
      key: "total",
      header: "Credited",
      align: "right",
      render: (c) => <Money amount={c.total} currency={c.currencyCode} />,
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Returns &amp; credit notes</h1>
          <p className="mt-1 text-sm text-slate-500">
            The customer gets back what they were charged — including the tax, at the rate they were
            charged it. A returned machine comes back for inspection, not straight onto the shelf.
          </p>
        </div>

        <Can feature={FEATURES.creditNotes} action={PermissionAction.Create}>
          <button
            onClick={() => setReturning(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Take a return
          </button>
        </Can>
      </div>

      <Can
        feature={FEATURES.creditNotes}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view credit notes.</p>}
      >
        <DataTable
          queryKey={["credit-notes"]}
          endpoint="api/v1/credit-notes"
          columns={columns}
          rowKey={(c) => c.id}
          searchPlaceholder="Search credit note, customer or invoice…"
          emptyMessage="Nothing has come back yet."
        />
      </Can>

      {returning && (
        <ReturnDialog
          token={accessToken}
          onClose={() => setReturning(false)}
          onDone={async () => {
            setReturning(false);
            await Promise.all([
              client.invalidateQueries({ queryKey: ["credit-notes"] }),
              client.invalidateQueries({ queryKey: ["sales-invoices"] }),
              client.invalidateQueries({ queryKey: ["stock"] }),
            ]);
          }}
        />
      )}
    </div>
  );
}

/**
 * Taking a return against an invoice.
 *
 * The refund method matters more than it looks. **Cash back against an invoice that was never paid is
 * refused** — it would hand the customer the shop's own money, and they would still owe for the goods
 * they kept. Offsetting against their balance is the right answer there, and the server says so.
 */
function ReturnDialog({
  token,
  onClose,
  onDone,
}: {
  token: string | null;
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const [invoiceId, setInvoiceId] = useState("");
  const [lineId, setLineId] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [refund, setRefund] = useState<RefundMethod>(RefundMethod.OffsetAgainstBalance);
  const [restock, setRestock] = useState(true);
  const [reason, setReason] = useState("");
  const [serials, setSerials] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const invoices =
    useApiQuery<PagedResult<SalesInvoice>>(["sales-invoices"], "api/v1/sales-invoices", { pageSize: 100 })
      .data?.items ?? [];

  const invoice = invoices.find((i) => i.id === invoiceId);
  const line = invoice?.lines.find((l) => l.id === lineId);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-lg space-y-4 rounded-lg border border-slate-200 bg-white p-5 dark:border-slate-800 dark:bg-slate-950">
        <h2 className="text-lg font-semibold">Take a return</h2>

        <Field label="Invoice">
          <select
            value={invoiceId}
            onChange={(e) => {
              setInvoiceId(e.target.value);
              setLineId("");
            }}
            className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1.5 text-sm dark:border-slate-700"
          >
            <option value="">Select…</option>
            {invoices
              .filter((i) => i.status !== SalesInvoiceStatus.Cancelled)
              .map((i) => (
                <option key={i.id} value={i.id}>
                  {i.number} — {i.customerName}
                </option>
              ))}
          </select>
        </Field>

        {invoice && (
          <Field label="Line">
            <select
              value={lineId}
              onChange={(e) => setLineId(e.target.value)}
              className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1.5 text-sm dark:border-slate-700"
            >
              <option value="">Select…</option>
              {invoice.lines.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.quantity} × {l.description}
                </option>
              ))}
            </select>
          </Field>
        )}

        <div className="grid grid-cols-2 gap-3">
          <Field label="Quantity">
            <input
              type="number"
              min={1}
              max={line?.quantity ?? 1}
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
              className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1.5 text-sm tabular-nums dark:border-slate-700"
            />
          </Field>

          <Field label="Refund as">
            <select
              value={refund}
              onChange={(e) => setRefund(Number(e.target.value) as RefundMethod)}
              className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1.5 text-sm dark:border-slate-700"
            >
              {Object.entries(REFUND_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </Field>
        </div>

        {line && line.serials.length > 0 && (
          <Field label="Serial numbers coming back">
            <input
              value={serials}
              onChange={(e) => setSerials(e.target.value)}
              placeholder={line.serials.join(", ")}
              className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1.5 font-mono text-xs dark:border-slate-700"
            />
          </Field>
        )}

        <Field label="Reason">
          <input
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Money is going back — somebody will ask why"
            className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1.5 text-sm dark:border-slate-700"
          />
        </Field>

        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={restock} onChange={(e) => setRestock(e.target.checked)} />
          Put the goods back into stock
          <span className="text-xs text-slate-500">(uncheck for faulty goods — the money still goes back)</span>
        </label>

        {error && (
          <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-xs text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="rounded-md border border-slate-200 px-3 py-1.5 text-sm dark:border-slate-700">
            Cancel
          </button>
          <button
            onClick={() => void submit()}
            disabled={busy || !invoiceId || !lineId || !reason.trim()}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Crediting…" : "Issue credit note"}
          </button>
        </div>
      </div>
    </div>
  );

  async function submit() {
    if (!token) return;

    setBusy(true);
    setError(null);

    const returned = serials
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);

    try {
      await issueCreditNote(
        { token },
        {
          salesInvoiceId: invoiceId,
          lines: [
            {
              salesInvoiceLineId: lineId,
              quantity: Number(quantity),
              serialNumbers: returned.length > 0 ? returned : null,
              restock,
            },
          ],
          refund,
          reason,
        },
      );

      await onDone();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "The credit note could not be issued.");
    } finally {
      setBusy(false);
    }
  }
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-1 text-xs font-medium uppercase tracking-wider text-slate-400">{label}</p>
      {children}
    </div>
  );
}
