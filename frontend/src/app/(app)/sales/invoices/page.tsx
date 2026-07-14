"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { recordPayment } from "@/features/sales/api";
import { Money, StatusBadge } from "@/features/sales/components/money";
import { useApiQuery } from "@/lib/use-api";
import {
  INVOICE_STATUS_LABELS,
  SalesInvoiceStatus,
  type PaymentMethod,
  type SalesInvoice,
} from "@/features/sales/types";
import type { PagedResult } from "@/types/api";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * Sales invoices — what customers owe.
 *
 * An invoice moves no stock: the delivery already did. What it carries is the money, the tax as it stood
 * on the day, and the COGS the ledger valued the goods at — which is why this screen can show a margin
 * per invoice that will still be right in a year, after the moving average has moved on.
 */
export default function InvoicesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [paying, setPaying] = useState<SalesInvoice | null>(null);

  const columns: Column<SalesInvoice>[] = [
    {
      key: "number",
      header: "Invoice",
      render: (i) => (
        <div>
          <p className="font-mono text-xs">{i.number}</p>
          <p className="text-xs text-slate-500">{new Date(i.invoicedAt).toLocaleDateString()}</p>
        </div>
      ),
    },
    { key: "customer", header: "Customer", render: (i) => i.customerName },
    {
      key: "status",
      header: "Status",
      render: (i) => <StatusBadge label={INVOICE_STATUS_LABELS[i.status]} tone={tone(i.status)} />,
    },
    {
      key: "total",
      header: "Total",
      align: "right",
      render: (i) => (
        <div>
          <Money amount={i.total} currency={i.currencyCode} />
          {/* Tax-exclusive pricing (D7): worth showing the split, because the customer will ask. */}
          <p className="text-xs text-slate-500">
            net <Money amount={i.netTotal} currency={i.currencyCode} /> + tax{" "}
            <Money amount={i.taxTotal} currency={i.currencyCode} />
          </p>
        </div>
      ),
    },
    {
      key: "outstanding",
      header: "Outstanding",
      align: "right",
      render: (i) =>
        i.outstandingAmount > 0 ? (
          <Money amount={i.outstandingAmount} currency={i.currencyCode} />
        ) : (
          <span className="text-slate-400">—</span>
        ),
    },
    {
      key: "margin",
      header: "Margin",
      align: "right",
      render: (i) => (
        <div>
          <Money amount={i.grossProfit} currency={i.currencyCode} />
          <p className="text-xs text-slate-500">
            cost <Money amount={i.costTotal} currency={i.currencyCode} />
          </p>
        </div>
      ),
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Sales invoices</h1>
        <p className="mt-1 text-sm text-slate-500">
          The bill. Margin is revenue minus the cost the ledger booked when the goods left — the tax is
          the government&apos;s money, not the shop&apos;s.
        </p>
      </div>

      <Can
        feature={FEATURES.salesInvoices}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view invoices.</p>}
      >
        <DataTable
          queryKey={["sales-invoices"]}
          endpoint="api/v1/sales-invoices"
          columns={columns}
          rowKey={(i) => i.id}
          searchPlaceholder="Search invoice number or customer…"
          emptyMessage="Nothing sold yet."
          actions={(invoice) => (
            <Can feature={FEATURES.customerPayments} action={PermissionAction.Create}>
              {invoice.status !== SalesInvoiceStatus.Paid
                && invoice.status !== SalesInvoiceStatus.Cancelled && (
                  <button
                    onClick={() => setPaying(invoice)}
                    className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                  >
                    Take payment
                  </button>
                )}
            </Can>
          )}
        />
      </Can>

      {paying && (
        <TakePaymentDialog
          invoice={paying}
          onClose={() => setPaying(null)}
          onPaid={async () => {
            setPaying(null);
            await client.invalidateQueries({ queryKey: ["sales-invoices"] });
            await client.invalidateQueries({ queryKey: ["customer-payments"] });
          }}
          token={accessToken}
        />
      )}
    </div>
  );
}

function tone(status: SalesInvoiceStatus) {
  if (status === SalesInvoiceStatus.Paid) return "good" as const;
  if (status === SalesInvoiceStatus.PartiallyPaid) return "warn" as const;
  if (status === SalesInvoiceStatus.Cancelled) return "bad" as const;

  return "neutral" as const;
}

/**
 * Taking money against one invoice.
 *
 * The tender is a list, not a field: one sale is settled by cash *and* card, and §23 asks for exactly
 * that. What is allocated to the invoice is what the invoice is worth — over-tendered cash is change, not
 * a credit.
 */
function TakePaymentDialog({
  invoice,
  token,
  onClose,
  onPaid,
}: {
  invoice: SalesInvoice;
  token: string | null;
  onClose: () => void;
  onPaid: () => Promise<void>;
}) {
  const [tender, setTender] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const methods =
    useApiQuery<PagedResult<PaymentMethod>>(["payment-methods"], "api/v1/payment-methods").data?.items ?? [];

  // What is still owed, not what the invoice was worth. A part-paid invoice must not ask for the whole
  // amount again.
  const outstanding = invoice.outstandingAmount;
  const tendered = Object.values(tender).reduce((sum, a) => sum + (Number(a) || 0), 0);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-md space-y-4 rounded-lg border border-slate-200 bg-white p-5 dark:border-slate-800 dark:bg-slate-950">
        <div>
          <h2 className="text-lg font-semibold">Take payment</h2>
          <p className="text-sm text-slate-500">
            {invoice.number} · {invoice.customerName} · <Money amount={outstanding} currency={invoice.currencyCode} />
          </p>
        </div>

        <div className="space-y-2">
          {methods.map((method) => (
            <div key={method.id} className="flex items-center gap-2">
              <label htmlFor={`pay-${method.id}`} className="w-28 shrink-0 text-sm">
                {method.name}
              </label>
              <input
                id={`pay-${method.id}`}
                type="number"
                min={0}
                step="0.01"
                value={tender[method.id] ?? ""}
                onChange={(e) => setTender((t) => ({ ...t, [method.id]: e.target.value }))}
                placeholder="0.00"
                className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1 text-sm tabular-nums outline-none dark:border-slate-700"
              />
            </div>
          ))}
        </div>

        {error && (
          <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-xs text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2">
          <button
            onClick={onClose}
            className="rounded-md border border-slate-200 px-3 py-1.5 text-sm dark:border-slate-700"
          >
            Cancel
          </button>
          <button
            onClick={() => void pay()}
            disabled={busy || tendered <= 0}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Taking…" : "Take payment"}
          </button>
        </div>
      </div>
    </div>
  );

  async function pay() {
    if (!token) return;

    setBusy(true);
    setError(null);

    const methodsUsed = methods
      .map((method) => ({
        paymentMethodId: method.id,
        amount: Number(tender[method.id] ?? 0),
        reference: method.requiresReference ? "counter" : null,
      }))
      .filter((t) => t.amount > 0);

    try {
      await recordPayment(
        { token },
        {
          customerId: invoice.customerId,
          branchId: invoice.branchId,
          methods: methodsUsed,
          // Never allocate more to the invoice than it is worth. The rest is a credit on the account —
          // the server refuses the alternative, and it is right to.
          allocations: [
            { salesInvoiceId: invoice.id, amount: Math.min(tendered, outstanding) },
          ],
        },
      );

      await onPaid();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "The payment could not be recorded.");
    } finally {
      setBusy(false);
    }
  }
}
