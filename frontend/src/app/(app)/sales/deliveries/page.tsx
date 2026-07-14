"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { raiseInvoice } from "@/features/sales/api";
import { Money, StatusBadge } from "@/components/ui/money";
import { DELIVERY_STATUS_LABELS, DeliveryStatus, type Delivery } from "@/features/sales/types";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * Deliveries — the goods physically leaving.
 *
 * **This is the only document in sales that moves stock**, and it is where a serial-tracked machine is
 * bound to the sale. The serials shown here are not decoration: two years from now, a warranty claim
 * starts from a laptop on the counter and has to find its way back to this row.
 */
export default function DeliveriesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const columns: Column<Delivery>[] = [
    {
      key: "number",
      header: "Delivery",
      render: (d) => (
        <div>
          <p className="font-mono text-xs">{d.number}</p>
          <p className="text-xs text-slate-500">{new Date(d.deliveredAt).toLocaleString()}</p>
        </div>
      ),
    },
    {
      key: "customer",
      header: "Customer",
      render: (d) => (
        <div>
          <p>{d.customerName}</p>
          {!d.salesOrderId && <p className="text-xs text-slate-500">Counter sale — no order</p>}
        </div>
      ),
    },
    {
      key: "status",
      header: "Status",
      render: (d) => (
        <StatusBadge
          label={DELIVERY_STATUS_LABELS[d.status]}
          tone={d.status === DeliveryStatus.Invoiced ? "good" : d.status === DeliveryStatus.Cancelled ? "bad" : "warn"}
        />
      ),
    },
    {
      key: "goods",
      header: "Goods",
      render: (d) => (
        <div className="space-y-0.5">
          {d.lines.map((line) => (
            <p key={line.id} className="text-xs">
              {line.quantity} × {line.productName}
              {line.serials.length > 0 && (
                <span className="ml-1 font-mono text-slate-500">{line.serials.join(", ")}</span>
              )}
            </p>
          ))}
        </div>
      ),
    },
    {
      key: "cost",
      header: "Cost",
      align: "right",
      // What the goods cost the shop — the moving average at the instant they left. This is the number
      // the invoice snapshots as COGS, and it is why a margin from last year still reads correctly.
      render: (d) => <Money amount={d.costTotal} />,
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Deliveries</h1>
        <p className="mt-1 text-sm text-slate-500">
          The only thing in sales that moves stock — and where the serial is bound to the sale that sent
          it out of the door.
        </p>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <Can
        feature={FEATURES.deliveries}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view deliveries.</p>}
      >
        <DataTable
          queryKey={["deliveries"]}
          endpoint="api/v1/deliveries"
          columns={columns}
          rowKey={(d) => d.id}
          searchPlaceholder="Search delivery number or customer…"
          emptyMessage="Nothing has left the warehouse yet."
          actions={(delivery) => (
            <Can feature={FEATURES.salesInvoices} action={PermissionAction.Create}>
              {delivery.status === DeliveryStatus.Delivered && (
                <button
                  onClick={() => void invoice(delivery)}
                  disabled={busy === delivery.id}
                  className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                >
                  {busy === delivery.id ? "…" : "Invoice"}
                </button>
              )}
            </Can>
          )}
        />
      </Can>
    </div>
  );

  async function invoice(delivery: Delivery) {
    if (!accessToken) return;

    setBusy(delivery.id);
    setError(null);

    try {
      await raiseInvoice({ token: accessToken }, delivery.id);

      await Promise.all([
        client.invalidateQueries({ queryKey: ["deliveries"] }),
        client.invalidateQueries({ queryKey: ["sales-invoices"] }),
      ]);
    } catch (e) {
      // A delivery cannot be billed twice — the server says so, and it is the whole reason the second
      // click is safe.
      setError(e instanceof ApiError ? e.message : "The invoice could not be raised.");
    } finally {
      setBusy(null);
    }
  }
}
