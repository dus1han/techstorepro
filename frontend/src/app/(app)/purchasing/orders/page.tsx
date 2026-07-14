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
import { approvePurchaseOrder, cancelPurchaseOrder, createPurchaseOrder } from "@/features/purchasing/api";
import { PO_STATUS_LABELS, PurchaseOrderStatus, type PurchaseOrder } from "@/features/purchasing/types";
import type { PagedResult } from "@/types/api";
import type { Product, Supplier } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

interface WarehouseOption {
  id: string;
  name: string;
}

/**
 * Purchase orders — **optional**, and the system means it.
 *
 * A shop that drives to the wholesaler and comes back with a box has no order and never will, so the
 * direct purchase (supplier → goods receipt → stock) is a first-class path and nothing on this screen is
 * on the critical route to receiving goods. What the order is for is the case where it earns its keep:
 * agreeing a price and a quantity before the goods exist, so what arrives can be checked against what
 * was agreed.
 *
 * **Approving is what commits the company's money**, which is why it is a separate permission from
 * creating — the person who chooses the supplier need not be the person who signs for it. It is also the
 * gate on receiving: goods cannot be posted against a draft.
 *
 * An order moves no stock. Only the goods receipt does.
 */
export default function PurchaseOrdersPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const suppliers =
    useApiQuery<PagedResult<Supplier>>(["suppliers"], "api/v1/suppliers", { pageSize: 200 }).data?.items ?? [];
  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];

  const reload = () => client.invalidateQueries({ queryKey: ["purchase-orders"] });

  const columns: Column<PurchaseOrder>[] = [
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
    { key: "supplier", header: "Supplier", render: (o) => o.supplierName },
    {
      key: "status",
      header: "Status",
      render: (o) => <StatusBadge label={PO_STATUS_LABELS[o.status]} tone={tone(o.status)} />,
    },
    {
      key: "progress",
      header: "Received",
      render: (o) => {
        const ordered = o.lines.reduce((sum, l) => sum + l.quantity, 0);
        const received = o.lines.reduce((sum, l) => sum + l.receivedQuantity, 0);

        return (
          <span className="text-xs tabular-nums text-slate-500">
            {received.toLocaleString()} of {ordered.toLocaleString()}
          </span>
        );
      },
    },
    {
      key: "total",
      header: "Total",
      align: "right",
      render: (o) => (
        <div>
          <Money amount={o.total} currency={o.currencyCode} />

          {/* An overseas order is still a commitment in the company's own money, and that is the
              number the shop budgets against. Showing only the foreign total would hide it. */}
          {o.currencyCode !== "AED" && (
            <p className="text-xs text-slate-500">
              <Money amount={o.totalBase} /> at {o.exchangeRate}
            </p>
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
      name: "warehouseId",
      label: "Deliver to",
      type: "select",
      required: true,
      options: warehouses.map((w) => ({ value: w.id, label: w.name })),
      help: "Where the goods are expected to land, so the receipt has a default.",
    },
    {
      name: "productId",
      label: "Product",
      type: "select",
      required: true,
      options: products.map((p) => ({ value: p.id, label: `${p.sku} — ${p.name}` })),
    },
    { name: "quantity", label: "Quantity", type: "number", required: true },
    { name: "unitPrice", label: "Unit price", type: "number", required: true },
    {
      name: "currencyCode",
      label: "Currency",
      type: "text",
      placeholder: "AED",
      help: "The supplier's currency — an overseas supplier bills in USD and the books stay in AED.",
    },
    {
      name: "exchangeRate",
      label: "Exchange rate",
      type: "number",
      help: "The rate agreed when the order was placed. The receipt takes its own rate on the day the goods land.",
    },
    { name: "expectedAt", label: "Expected", type: "date" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Purchase orders</h1>
          <p className="mt-1 text-sm text-slate-500">
            Optional, and meant it — a direct purchase needs no order. Approving one commits the money;
            receiving is what moves the stock.
          </p>
        </div>

        <Can feature={FEATURES.purchaseOrders} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New order
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <Can
        feature={FEATURES.purchaseOrders}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view purchase orders.</p>}
      >
        <DataTable
          queryKey={["purchase-orders"]}
          endpoint="api/v1/purchase-orders"
          columns={columns}
          rowKey={(o) => o.id}
          searchPlaceholder="Search order number or supplier…"
          emptyMessage="No purchase orders yet."
          actions={(order) => (
            <div className="flex justify-end gap-2">
              {order.status === PurchaseOrderStatus.Draft && (
                <>
                  {/* Approve is where the money is committed — hence its own permission. */}
                  <Can feature={FEATURES.purchaseOrders} action={PermissionAction.Approve}>
                    <button
                      onClick={() => void run(order.id, () => approvePurchaseOrder({ token: accessToken! }, order.id))}
                      disabled={busy === order.id}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Approve
                    </button>
                  </Can>

                  <Can feature={FEATURES.purchaseOrders} action={PermissionAction.Edit}>
                    <button
                      onClick={() => {
                        const reason = window.prompt("Why is this order being cancelled?");
                        if (!reason) return;

                        void run(order.id, () => cancelPurchaseOrder({ token: accessToken! }, order.id, reason));
                      }}
                      disabled={busy === order.id}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Cancel
                    </button>
                  </Can>
                </>
              )}

              {/* Once goods have arrived there is nothing to do here: the stock is on the shelf and the
                  supplier will invoice for it. Receiving happens on the goods-receipts screen, against
                  this order. */}
            </div>
          )}
        />
      </Can>

      {creating && (
        <EntityForm
          title="New purchase order"
          fields={fields}
          initial={{ currencyCode: "AED", exchangeRate: 1 }}
          submitLabel="Raise draft"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await createPurchaseOrder(
              { token: accessToken! },
              {
                supplierId: String(values.supplierId),
                branchId: String(values.branchId),
                warehouseId: String(values.warehouseId),
                lines: [
                  {
                    productId: String(values.productId),
                    quantity: Number(values.quantity),
                    unitPrice: Number(values.unitPrice),
                  },
                ],
                currencyCode: String(values.currencyCode || "AED"),
                exchangeRate: Number(values.exchangeRate) || 1,
                expectedAt: values.expectedAt ? new Date(String(values.expectedAt)).toISOString() : null,
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

function tone(status: PurchaseOrderStatus) {
  if (status === PurchaseOrderStatus.Received) return "good" as const;
  if (status === PurchaseOrderStatus.Cancelled) return "bad" as const;
  if (status === PurchaseOrderStatus.PartiallyReceived) return "warn" as const;

  return "neutral" as const;
}
