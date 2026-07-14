"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { cancelOrder, confirmOrder, deliver, raiseInvoice } from "@/features/sales/api";
import { Money, StatusBadge } from "@/components/ui/money";
import { ORDER_STATUS_LABELS, SalesOrderStatus, type SalesOrder } from "@/features/sales/types";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * Sales orders.
 *
 * **Confirming is the consequential button on this screen.** It reserves the stock — which is what stops
 * two salespeople promising the same last laptop — and it checks the customer's credit limit, because
 * this is the moment the shop commits goods to someone who has not paid. Discovering at delivery that
 * they are over their limit is discovering it too late: the laptop is already in their car.
 *
 * Delivering is what actually moves the stock, and it is where a serial-tracked machine is picked. This
 * screen delivers whole lines; picking specific serials line by line belongs on a dedicated screen.
 */
export default function SalesOrdersPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const reload = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["sales-orders"] }),
      client.invalidateQueries({ queryKey: ["deliveries"] }),
      client.invalidateQueries({ queryKey: ["sales-invoices"] }),
      client.invalidateQueries({ queryKey: ["stock"] }),
    ]);

  const columns: Column<SalesOrder>[] = [
    {
      key: "number",
      header: "Order",
      render: (o) => (
        <div>
          <p className="font-mono text-xs">{o.number}</p>
          <p className="text-xs text-slate-500">{new Date(o.orderedAt).toLocaleDateString()}</p>
        </div>
      ),
    },
    { key: "customer", header: "Customer", render: (o) => o.customerName },
    {
      key: "status",
      header: "Status",
      render: (o) => (
        <div className="space-y-1">
          <StatusBadge label={ORDER_STATUS_LABELS[o.status]} tone={tone(o.status)} />
          {o.status === SalesOrderStatus.Confirmed && (
            <p className="text-xs text-slate-500">Stock reserved</p>
          )}
        </div>
      ),
    },
    {
      key: "lines",
      header: "Lines",
      render: (o) => (
        <p className="text-xs text-slate-500">
          {o.lines.length} line{o.lines.length === 1 ? "" : "s"}
          {o.lines.some((l) => l.outstandingQuantity > 0) && o.status !== SalesOrderStatus.Draft && (
            <> · {o.lines.reduce((sum, l) => sum + l.outstandingQuantity, 0)} outstanding</>
          )}
        </p>
      ),
    },
    {
      key: "total",
      header: "Total",
      align: "right",
      render: (o) => <Money amount={o.total} currency={o.currencyCode} />,
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Sales orders</h1>
        <p className="mt-1 text-sm text-slate-500">
          Confirming an order reserves the stock and checks the customer&apos;s credit limit. Nothing
          leaves the shelf until it is delivered.
        </p>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <Can
        feature={FEATURES.salesOrders}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view orders.</p>}
      >
        <DataTable
          queryKey={["sales-orders"]}
          endpoint="api/v1/sales-orders"
          columns={columns}
          rowKey={(o) => o.id}
          searchPlaceholder="Search order number or customer…"
          emptyMessage="No orders yet."
          actions={(order) => (
            <div className="flex justify-end gap-2">
              {order.status === SalesOrderStatus.Draft && (
                <Can feature={FEATURES.salesOrders} action={PermissionAction.Edit}>
                  <Action label="Confirm" busy={busy === order.id} onClick={() => void run(order.id, () => confirmOrder({ token: accessToken! }, order.id))} />
                </Can>
              )}

              {(order.status === SalesOrderStatus.Confirmed
                || order.status === SalesOrderStatus.PartiallyDelivered) && (
                <Can feature={FEATURES.deliveries} action={PermissionAction.Create}>
                  <Action
                    label="Deliver & invoice"
                    busy={busy === order.id}
                    onClick={() => void run(order.id, () => deliverAndInvoice(order))}
                  />
                </Can>
              )}

              {order.status !== SalesOrderStatus.Cancelled
                && order.status !== SalesOrderStatus.Delivered
                && !order.lines.some((l) => l.deliveredQuantity > 0) && (
                  <Can feature={FEATURES.salesOrders} action={PermissionAction.Delete}>
                    <Action
                      label="Cancel"
                      busy={busy === order.id}
                      onClick={() =>
                        void run(order.id, () =>
                          cancelOrder({ token: accessToken! }, order.id, "Cancelled from the orders screen"),
                        )
                      }
                    />
                  </Can>
                )}
            </div>
          )}
        />
      </Can>
    </div>
  );

  async function deliverAndInvoice(order: SalesOrder) {
    if (!accessToken) return;

    // Everything still outstanding goes out. A serial-tracked line cannot be delivered this way — the
    // machines have to be picked one by one, and guessing which laptop left would defeat the whole point
    // of tracking them.
    const outstanding = order.lines.filter((l) => l.outstandingQuantity > 0);

    const deliveryId = await deliver(
      { token: accessToken },
      {
        branchId: order.branchId,
        warehouseId: order.warehouseId,
        salesOrderId: order.id,
        lines: outstanding.map((l) => ({
          productId: l.productId!,
          quantity: l.outstandingQuantity,
        })),
      },
    );

    await raiseInvoice({ token: accessToken }, deliveryId);
  }

  async function run(id: string, action: () => Promise<unknown>) {
    setBusy(id);
    setError(null);

    try {
      await action();
      await reload();
    } catch (e) {
      // The server's messages name the actual problem — an unconfirmed order, a customer over their
      // credit limit, a serial-tracked line that needs picking. Show them rather than a generic failure.
      setError(e instanceof ApiError ? e.message : "That did not work.");
    } finally {
      setBusy(null);
    }
  }
}

function tone(status: SalesOrderStatus) {
  if (status === SalesOrderStatus.Delivered) return "good" as const;
  if (status === SalesOrderStatus.PartiallyDelivered) return "warn" as const;
  if (status === SalesOrderStatus.Cancelled) return "bad" as const;

  return "neutral" as const;
}

function Action({ label, busy, onClick }: { label: string; busy: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      disabled={busy}
      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
    >
      {busy ? "…" : label}
    </button>
  );
}
