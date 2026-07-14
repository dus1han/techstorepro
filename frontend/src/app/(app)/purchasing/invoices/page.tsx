"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { EntityForm, type FieldSpec } from "@/components/data/entity-form";
import { Money, StatusBadge } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import {
  cancelSupplierInvoice,
  postSupplierInvoice,
  recordSupplierInvoice,
} from "@/features/purchasing/api";
import {
  SUPPLIER_INVOICE_STATUS_LABELS,
  SupplierInvoiceStatus,
  type GoodsReceipt,
  type SupplierInvoice,
} from "@/features/purchasing/types";
import type { PagedResult } from "@/types/api";
import type { Supplier } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

/**
 * Supplier invoices — what the supplier is asking to be paid.
 *
 * **This screen does not touch stock.** The goods receipt already moved it, and an invoice that moved it
 * as well would double it. The two documents are deliberately separate because they answer different
 * questions and they genuinely disagree: the goods arrive in March and the invoice in April; three of the
 * ten units were damaged and short-invoiced; the price billed is not the price ordered. A single document
 * would force those disagreements to be resolved silently in favour of whichever arrived last.
 *
 * Linking a receipt is therefore optional — the bill may arrive before the goods, or cover several
 * receipts at once.
 *
 * **Posting is what creates the debt.** A draft is a bill somebody is still checking against the receipt;
 * it owes nothing until it is posted.
 */
export default function SupplierInvoicesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const suppliers =
    useApiQuery<PagedResult<Supplier>>(["suppliers"], "api/v1/suppliers", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const receipts =
    useApiQuery<PagedResult<GoodsReceipt>>(["goods-receipts"], "api/v1/goods-receipts", { pageSize: 200 })
      .data?.items ?? [];

  // Paying changes the supplier's balance, so the payments screen and the supplier list go stale too.
  const reload = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["supplier-invoices"] }),
      client.invalidateQueries({ queryKey: ["suppliers"] }),
    ]);

  const columns: Column<SupplierInvoice>[] = [
    {
      key: "number",
      header: "Invoice",
      render: (i) => (
        <div>
          <p className="font-mono text-xs">{i.number}</p>
          {/* Theirs, not ours — it is what they will quote when they chase payment. */}
          <p className="font-mono text-xs text-slate-500">{i.supplierReference}</p>
        </div>
      ),
    },
    {
      key: "supplier",
      header: "Supplier",
      render: (i) => (
        <div>
          <p>{i.supplierName}</p>
          {i.goodsReceiptNumber && (
            <p className="font-mono text-xs text-slate-500">{i.goodsReceiptNumber}</p>
          )}
        </div>
      ),
    },
    {
      key: "status",
      header: "Status",
      render: (i) => (
        <StatusBadge label={SUPPLIER_INVOICE_STATUS_LABELS[i.status]} tone={tone(i.status)} />
      ),
    },
    {
      key: "due",
      header: "Due",
      render: (i) =>
        i.dueAt ? (
          <span
            className={`text-xs tabular-nums ${
              overdue(i) ? "font-medium text-red-600 dark:text-red-400" : ""
            }`}
          >
            {new Date(i.dueAt).toLocaleDateString()}
          </span>
        ) : (
          <span className="text-slate-400">—</span>
        ),
    },
    {
      key: "outstanding",
      header: "Outstanding",
      align: "right",
      render: (i) => (
        <div>
          <Money amount={i.outstandingAmount} currency={i.currencyCode} />
          <p className="text-xs text-slate-500">
            of <Money amount={i.total} currency={i.currencyCode} />
          </p>
        </div>
      ),
    },
    {
      key: "owed",
      header: "Owed (base)",
      align: "right",
      render: (i) => (
        <div>
          {/* Fixed at the invoice-date rate. This is the number the FX gain is measured against when
              the money finally goes out at a different rate. */}
          <Money amount={i.totalBase} />
          {i.currencyCode !== "AED" && (
            <p className="text-xs text-slate-500">at {i.exchangeRate}</p>
          )}
        </div>
      ),
    },
  ];

  const fields: FieldSpec[] = [
    {
      name: "supplierId",
      label: "Supplier",
      type: "select",
      required: true,
      options: suppliers.map((s) => ({ value: s.id, label: s.name })),
    },
    {
      name: "branchId",
      label: "Branch",
      type: "select",
      required: true,
      options: branches.map((b) => ({ value: b.id, label: b.name })),
    },
    {
      name: "supplierReference",
      label: "Their invoice number",
      required: true,
      help: "Without it nobody can match this to the piece of paper they will chase you with.",
    },
    {
      name: "goodsReceiptId",
      label: "Against a receipt",
      type: "select",
      options: receipts.map((r) => ({
        value: r.id,
        label: `${r.number} — ${r.supplierName}`,
      })),
      help: "Optional — the bill may arrive before the goods, or cover several receipts.",
    },
    { name: "description", label: "Description", required: true, wide: true },
    { name: "quantity", label: "Quantity", type: "number", required: true },
    { name: "unitPrice", label: "Unit price", type: "number", required: true },
    { name: "taxPercent", label: "Tax %", type: "number" },
    { name: "currencyCode", label: "Currency", placeholder: "AED" },
    {
      name: "exchangeRate",
      label: "Exchange rate",
      type: "number",
      help: "The rate on the invoice date. It fixes what the company owes in its own money.",
    },
    { name: "dueAt", label: "Due", type: "date" },
    {
      name: "post",
      label: "Post it now",
      type: "checkbox",
      help: "Posting is what puts the debt on the supplier's balance. Leave it off to check the bill first.",
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Supplier invoices</h1>
          <p className="mt-1 text-sm text-slate-500">
            What the supplier is asking to be paid. It moves no stock — the receipt already did. Posting
            is what creates the debt.
          </p>
        </div>

        <Can feature={FEATURES.supplierInvoices} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Record invoice
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <Can
        feature={FEATURES.supplierInvoices}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view supplier invoices.</p>}
      >
        <DataTable
          queryKey={["supplier-invoices"]}
          endpoint="api/v1/supplier-invoices"
          columns={columns}
          rowKey={(i) => i.id}
          searchPlaceholder="Search our number, theirs, or the supplier…"
          emptyMessage="No supplier invoices yet."
          actions={(invoice) => (
            <div className="flex justify-end gap-2">
              {invoice.status === SupplierInvoiceStatus.Draft && (
                <>
                  <Can feature={FEATURES.supplierInvoices} action={PermissionAction.Approve}>
                    <button
                      onClick={() =>
                        void run(invoice.id, () => postSupplierInvoice({ token: accessToken! }, invoice.id))
                      }
                      disabled={busy === invoice.id}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Post
                    </button>
                  </Can>

                  <Can feature={FEATURES.supplierInvoices} action={PermissionAction.Edit}>
                    <button
                      onClick={() => {
                        const reason = window.prompt("Why is this invoice being cancelled?");
                        if (!reason) return;

                        void run(invoice.id, () =>
                          cancelSupplierInvoice({ token: accessToken! }, invoice.id, reason),
                        );
                      }}
                      disabled={busy === invoice.id}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Cancel
                    </button>
                  </Can>
                </>
              )}

              {/* Paying happens on the payments screen: one transfer usually settles several of these
                  at once, and a per-row "pay" button would quietly encourage paying them one by one. */}
            </div>
          )}
        />
      </Can>

      {creating && (
        <EntityForm
          title="Record a supplier invoice"
          fields={fields}
          initial={{ currencyCode: "AED", exchangeRate: 1, quantity: 1, post: true }}
          submitLabel="Record"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await recordSupplierInvoice(
              { token: accessToken! },
              {
                supplierId: String(values.supplierId),
                branchId: String(values.branchId),
                supplierReference: String(values.supplierReference),
                goodsReceiptId: values.goodsReceiptId ? String(values.goodsReceiptId) : null,
                lines: [
                  {
                    description: String(values.description),
                    quantity: Number(values.quantity),
                    unitPrice: Number(values.unitPrice),
                    taxPercent: Number(values.taxPercent) || 0,
                  },
                ],
                currencyCode: String(values.currencyCode || "AED"),
                exchangeRate: Number(values.exchangeRate) || 1,
                dueAt: values.dueAt ? new Date(String(values.dueAt)).toISOString() : null,
                post: Boolean(values.post),
              },
            );

            setCreating(false);
            await reload();
          }}
        />
      )}
    </div>
  );

  async function run(id: string, action: () => Promise<unknown>) {
    setBusy(id);
    setError(null);

    try {
      await action();
      await reload();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "That did not work.");
    } finally {
      setBusy(null);
    }
  }
}

/** Past its due date and still owing. A paid invoice is never overdue, whatever its date says. */
function overdue(invoice: SupplierInvoice) {
  return (
    invoice.dueAt !== null &&
    invoice.outstandingAmount > 0 &&
    new Date(invoice.dueAt) < new Date()
  );
}

function tone(status: SupplierInvoiceStatus) {
  if (status === SupplierInvoiceStatus.Paid) return "good" as const;
  if (status === SupplierInvoiceStatus.Cancelled) return "bad" as const;
  if (status === SupplierInvoiceStatus.PartiallyPaid) return "warn" as const;

  return "neutral" as const;
}
