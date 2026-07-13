"use client";

import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { FEATURES, PermissionAction } from "@/types/identity";
import type { StockMovementDto } from "@/types/inventory";

const TYPE_LABELS: Record<number, string> = {
  1: "Opening balance",
  2: "Receipt",
  3: "Sale",
  4: "Sale return",
  5: "Purchase return",
  6: "Transfer out",
  7: "Transfer in",
  8: "Adjustment in",
  9: "Adjustment out",
  10: "Repair consumption",
  11: "Repair return",
  12: "Count adjustment in",
  13: "Count adjustment out",
};

const REFERENCE_LABELS: Record<number, string> = {
  1: "Opening",
  2: "Goods receipt",
  3: "Invoice",
  4: "Delivery",
  5: "Credit note",
  6: "Transfer",
  7: "Adjustment",
  8: "Stock count",
  9: "Repair ticket",
  10: "Purchase return",
};

/**
 * The stock card: every movement, in order, with the running balance after each.
 *
 * This is the screen someone opens when a balance looks wrong — the row at which the balance stopped
 * matching expectation is the answer. It is read-only at every permission level, by design: stock
 * only ever moves through a document.
 */
export default function StockMovementsPage() {
  const columns: Column<StockMovementDto>[] = [
    {
      key: "occurredAt",
      header: "When",
      render: (m) => (
        <span className="text-xs tabular-nums text-slate-600 dark:text-slate-400">
          {new Date(m.occurredAt).toLocaleString()}
        </span>
      ),
    },
    {
      key: "product",
      header: "Product",
      render: (m) => (
        <div>
          <p className="font-medium">{m.productName}</p>
          <p className="font-mono text-xs text-slate-500">
            {m.serialNumber ? `${m.sku} · ${m.serialNumber}` : m.sku}
          </p>
        </div>
      ),
    },
    { key: "warehouse", header: "Warehouse", render: (m) => m.warehouseName },
    { key: "type", header: "Type", render: (m) => <span className="text-xs">{TYPE_LABELS[m.type]}</span> },
    {
      key: "quantity",
      header: "Quantity",
      align: "right",
      // Quantities are stored signed, so the sign *is* the direction. Showing it is what makes the
      // running balance in the next column add up on the page.
      render: (m) => (
        <span className={m.quantity < 0 ? "text-red-600 dark:text-red-400" : "text-emerald-600 dark:text-emerald-400"}>
          {m.quantity > 0 ? `+${m.quantity.toFixed(2)}` : m.quantity.toFixed(2)}
        </span>
      ),
    },
    { key: "unitCost", header: "Unit cost", align: "right", render: (m) => m.unitCost.toFixed(2) },
    { key: "balanceAfter", header: "Balance after", align: "right", render: (m) => m.balanceAfter.toFixed(2) },
    {
      key: "reference",
      header: "Reference",
      render: (m) => (
        <div className="text-xs">
          <p>{REFERENCE_LABELS[m.referenceType]}</p>
          {m.referenceNumber && <p className="font-mono text-slate-500">{m.referenceNumber}</p>}
        </div>
      ),
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Stock movements</h1>
        <p className="mt-1 text-sm text-slate-500">
          The ledger. Every movement carries the balance and the average cost it left behind.
        </p>
      </div>

      <Can
        feature={FEATURES.stockMovements}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view stock movements.</p>}
      >
        <DataTable
          queryKey={["stock-movements"]}
          endpoint="api/v1/inventory/movements"
          columns={columns}
          rowKey={(m) => m.id}
          emptyMessage="No stock has moved yet."
        />
      </Can>
    </div>
  );
}
