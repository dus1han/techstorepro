"use client";

import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { FEATURES, PermissionAction } from "@/types/identity";
import type { SerialDto } from "@/types/inventory";

const STATUS_LABELS: Record<number, string> = {
  1: "In stock",
  2: "Reserved",
  3: "In transit",
  4: "Sold",
  5: "In repair",
  6: "Returned",
  7: "Scrapped",
  8: "Returned to supplier",
};

/**
 * Serial numbers — one row per machine.
 *
 * This is the list behind a warranty claim: a customer puts a laptop on the counter, the clerk types
 * the serial, and the row says whether we sold it, where it is, and whether it is still covered.
 */
export default function SerialsPage() {
  const columns: Column<SerialDto>[] = [
    {
      key: "serialNumber",
      header: "Serial",
      render: (s) => <span className="font-mono text-xs">{s.serialNumber}</span>,
    },
    {
      key: "product",
      header: "Product",
      render: (s) => (
        <div>
          <p className="font-medium">{s.productName}</p>
          <p className="font-mono text-xs text-slate-500">{s.sku}</p>
        </div>
      ),
    },
    { key: "status", header: "Status", render: (s) => <span className="text-xs">{STATUS_LABELS[s.status]}</span> },
    {
      key: "warehouse",
      header: "Warehouse",
      render: (s) =>
        // A sold machine is in no warehouse, and saying so is more useful than an empty cell.
        s.warehouseName ?? <span className="text-slate-400">—</span>,
    },
    {
      key: "warranty",
      header: "Warranty",
      render: (s) =>
        s.warrantyUntil === null ? (
          <span className="text-slate-400">—</span>
        ) : (
          <div className="text-xs">
            <p className={s.isUnderWarranty ? "text-emerald-600 dark:text-emerald-400" : "text-slate-500"}>
              {s.isUnderWarranty ? "Covered" : "Expired"}
            </p>
            <p className="tabular-nums text-slate-500">
              {new Date(s.warrantyUntil).toLocaleDateString()}
            </p>
          </div>
        ),
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Serial numbers</h1>
        <p className="mt-1 text-sm text-slate-500">
          Every serial-tracked machine, where it is, and whether it is still under warranty.
        </p>
      </div>

      <Can
        feature={FEATURES.serials}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view serial numbers.</p>}
      >
        <DataTable
          queryKey={["serials"]}
          endpoint="api/v1/inventory/serials"
          columns={columns}
          rowKey={(s) => s.id}
          searchPlaceholder="Search serial, product name or SKU…"
          emptyMessage="No serial-tracked stock yet."
        />
      </Can>
    </div>
  );
}
