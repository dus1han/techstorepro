"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { EntityForm, type FieldSpec } from "@/components/data/entity-form";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { acceptQuotation, convertQuotation, createQuotation } from "@/features/sales/api";
import { Money, StatusBadge } from "@/features/sales/components/money";
import { QUOTATION_STATUS_LABELS, QuotationStatus, type Quotation } from "@/features/sales/types";
import type { PagedResult } from "@/types/api";
import type { Customer, Product } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

interface WarehouseOption {
  id: string;
  name: string;
}

/**
 * Quotations — a price, promised for a while.
 *
 * **A quotation reserves nothing.** Quoting ten laptops the shop does not have is legitimate, and holding
 * stock for every speculative quote would empty the warehouse on paper while it sat full. Stock is
 * promised when the customer commits, which is when the order is confirmed.
 *
 * Converting honours the price that was quoted — it does not re-price against today's list. That promise
 * is what a quotation *is*; re-pricing it would make the document a suggestion.
 */
export default function QuotationsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const customers =
    useApiQuery<PagedResult<Customer>>(["customers"], "api/v1/customers", { pageSize: 200 }).data?.items ?? [];
  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];

  const reload = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["quotations"] }),
      client.invalidateQueries({ queryKey: ["sales-orders"] }),
    ]);

  const columns: Column<Quotation>[] = [
    {
      key: "number",
      header: "Quote",
      render: (q) => (
        <div>
          <p className="font-mono text-xs">{q.number}</p>
          <p className="text-xs text-slate-500">{new Date(q.quotedAt).toLocaleDateString()}</p>
        </div>
      ),
    },
    { key: "customer", header: "Customer", render: (q) => q.customerName ?? <span className="text-slate-400">Enquiry</span> },
    {
      key: "status",
      header: "Status",
      render: (q) => <StatusBadge label={QUOTATION_STATUS_LABELS[q.status]} tone={tone(q.status)} />,
    },
    {
      key: "validUntil",
      header: "Valid until",
      render: (q) =>
        q.validUntil ? (
          <span className="text-xs tabular-nums">{new Date(q.validUntil).toLocaleDateString()}</span>
        ) : (
          <span className="text-slate-400">—</span>
        ),
    },
    {
      key: "total",
      header: "Total",
      align: "right",
      render: (q) => <Money amount={q.total} currency={q.currencyCode} />,
    },
  ];

  const fields: FieldSpec[] = [
    {
      name: "customerId",
      label: "Customer",
      type: "select",
      options: customers.map((c) => ({ value: c.id, label: c.name })),
    },
    {
      name: "branchId",
      label: "Branch",
      type: "select",
      required: true,
      options: branches.map((b) => ({ value: b.id, label: b.name })),
    },
    {
      name: "productId",
      label: "Product",
      type: "select",
      required: true,
      options: products.map((p) => ({ value: p.id, label: `${p.sku} — ${p.name}` })),
    },
    { name: "quantity", label: "Quantity", type: "number", required: true },
    { name: "validUntil", label: "Valid until", type: "date" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Quotations</h1>
          <p className="mt-1 text-sm text-slate-500">
            An offer, not a claim on the shelf. Nothing is reserved until the order is confirmed.
          </p>
        </div>

        <Can feature={FEATURES.quotations} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New quotation
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <Can
        feature={FEATURES.quotations}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view quotations.</p>}
      >
        <DataTable
          queryKey={["quotations"]}
          endpoint="api/v1/quotations"
          columns={columns}
          rowKey={(q) => q.id}
          searchPlaceholder="Search quote number or customer…"
          emptyMessage="No quotations yet."
          actions={(quotation) => (
            <div className="flex justify-end gap-2">
              {(quotation.status === QuotationStatus.Draft || quotation.status === QuotationStatus.Sent) && (
                <Can feature={FEATURES.quotations} action={PermissionAction.Edit}>
                  <button
                    onClick={() => void run(quotation.id, () => acceptQuotation({ token: accessToken! }, quotation.id))}
                    disabled={busy === quotation.id}
                    className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                  >
                    Accept
                  </button>
                </Can>
              )}

              {quotation.status === QuotationStatus.Accepted && quotation.customerId && (
                <Can feature={FEATURES.salesOrders} action={PermissionAction.Create}>
                  <button
                    onClick={() =>
                      void run(quotation.id, () =>
                        convertQuotation({ token: accessToken! }, quotation.id, warehouses[0]!.id),
                      )
                    }
                    disabled={busy === quotation.id || warehouses.length === 0}
                    className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                  >
                    Convert to order
                  </button>
                </Can>
              )}
            </div>
          )}
        />
      </Can>

      {creating && (
        <EntityForm
          title="New quotation"
          fields={fields}
          submitLabel="Quote"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await createQuotation(
              { token: accessToken! },
              {
                branchId: String(values.branchId),
                customerId: values.customerId ? String(values.customerId) : null,
                lines: [{ productId: String(values.productId), quantity: Number(values.quantity) }],
                validUntil: values.validUntil ? new Date(String(values.validUntil)).toISOString() : null,
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

function tone(status: QuotationStatus) {
  if (status === QuotationStatus.Accepted || status === QuotationStatus.Converted) return "good" as const;
  if (status === QuotationStatus.Rejected || status === QuotationStatus.Expired) return "bad" as const;

  return "neutral" as const;
}
