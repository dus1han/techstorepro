"use client";

import { useState } from "react";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { FEATURES, PermissionAction } from "@/types/identity";
import type { StockBalanceDto } from "@/types/inventory";

/**
 * What is on the shelf right now, per warehouse.
 *
 * "Low stock only" is the reorder report of requirements §36 — it is a server-side filter
 * (?lowStock=true), not a filter of the page currently on screen: the twenty products below their
 * reorder level are rarely the twenty-five on page one.
 */
export default function StockPage() {
  const [lowStockOnly, setLowStockOnly] = useState(false);

  const columns: Column<StockBalanceDto>[] = [
    {
      key: "product",
      header: "Product",
      sortBy: "product",
      render: (b) => (
        <div>
          <p className="font-medium">{b.productName}</p>
          {b.isBelowReorderLevel && (
            <p className="text-xs text-amber-600 dark:text-amber-400">
              At or below reorder level ({b.reorderLevel})
            </p>
          )}
        </div>
      ),
    },
    { key: "sku", header: "SKU", render: (b) => <span className="font-mono text-xs">{b.sku}</span> },
    { key: "warehouse", header: "Warehouse", sortBy: "warehouse", render: (b) => b.warehouseName },
    {
      key: "quantity",
      header: "On hand",
      sortBy: "quantity",
      align: "right",
      render: (b) => b.quantity.toFixed(2),
    },
    {
      key: "reserved",
      header: "Reserved",
      align: "right",
      render: (b) =>
        b.reservedQuantity === 0 ? (
          <span className="text-slate-400">—</span>
        ) : (
          b.reservedQuantity.toFixed(2)
        ),
    },
    {
      key: "available",
      header: "Available",
      sortBy: "available",
      align: "right",
      // Available, not on-hand, is the number that answers "can I sell this?" — reserved units are
      // physically there and already promised to someone else.
      render: (b) => (
        <span className={b.availableQuantity <= 0 ? "text-red-600 dark:text-red-400" : ""}>
          {b.availableQuantity.toFixed(2)}
        </span>
      ),
    },
    { key: "averageCost", header: "Avg cost", align: "right", render: (b) => b.averageCost.toFixed(2) },
    { key: "totalValue", header: "Value", sortBy: "value", align: "right", render: (b) => b.totalValue.toFixed(2) },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Stock on hand</h1>
          <p className="mt-1 text-sm text-slate-500">
            Balances per product and warehouse, valued at weighted-average cost.
          </p>
        </div>

        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={lowStockOnly}
            onChange={(event) => setLowStockOnly(event.target.checked)}
            className="size-4 accent-slate-900 dark:accent-slate-100"
          />
          Low stock only
        </label>
      </div>

      <Can
        feature={FEATURES.stock}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view stock.</p>}
      >
        <DataTable
          queryKey={["stock"]}
          endpoint="api/v1/inventory/stock"
          columns={columns}
          filters={{ lowStock: lowStockOnly || undefined }}
          rowKey={(b) => `${b.productId}:${b.warehouseId}`}
          searchPlaceholder="Search name, SKU or barcode…"
          emptyMessage={lowStockOnly ? "Nothing is below its reorder level." : "No stock yet."}
        />
      </Can>
    </div>
  );
}
