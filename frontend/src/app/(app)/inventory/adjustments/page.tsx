"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { EntityForm, type FieldSpec } from "@/components/data/entity-form";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import type { PagedResult } from "@/types/api";
import type { Product } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";
import type { AdjustmentDto } from "@/types/inventory";

interface WarehouseOption {
  id: string;
  name: string;
}

const REASON_LABELS: Record<number, string> = {
  1: "Opening stock",
  2: "Damaged",
  3: "Lost",
  4: "Theft",
  5: "Expired",
  6: "Internal use",
  7: "Sample",
  8: "Data correction",
  9: "Other",
};

const REASON_OPTIONS = Object.entries(REASON_LABELS).map(([value, label]) => ({ value, label }));

/**
 * Stock adjustments — writing stock on or off, with a reason.
 *
 * An adjustment posts to the ledger immediately; there is no draft and no approval step. Create is
 * therefore the dangerous grant, not Approve, which is why the button below is gated on it.
 */
export default function AdjustmentsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);

  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 100 }).data?.items ?? [];

  const reload = () => client.invalidateQueries({ queryKey: ["adjustments"] });

  const columns: Column<AdjustmentDto>[] = [
    { key: "number", header: "Number", render: (a) => <span className="font-mono text-xs">{a.number}</span> },
    {
      key: "adjustedAt",
      header: "When",
      render: (a) => (
        <span className="text-xs tabular-nums text-slate-600 dark:text-slate-400">
          {new Date(a.adjustedAt).toLocaleString()}
        </span>
      ),
    },
    { key: "warehouse", header: "Warehouse", render: (a) => a.warehouseName },
    {
      key: "reason",
      header: "Reason",
      render: (a) => (
        <div className="text-xs">
          <p>{REASON_LABELS[a.reason]}</p>
          <p className="text-slate-500">{a.explanation}</p>
        </div>
      ),
    },
    { key: "lines", header: "Lines", align: "right", render: (a) => a.lines.length },
    {
      key: "netValue",
      header: "Net value",
      align: "right",
      // A write-off is a loss, and it is shown as one rather than as an unsigned number.
      render: (a) => (
        <span className={a.netValue < 0 ? "text-red-600 dark:text-red-400" : ""}>{a.netValue.toFixed(2)}</span>
      ),
    },
  ];

  // One line per document, from this screen. The server accepts many — the multi-line counting screen
  // is what the stock-count flow is for — but EntityForm is a flat field grid, and a fake repeater
  // bolted onto it would be a new primitive nobody else uses.
  const fields: FieldSpec[] = [
    {
      name: "warehouseId",
      label: "Warehouse",
      type: "select",
      required: true,
      options: warehouses.map((w) => ({ value: w.id, label: w.name })),
    },
    {
      name: "branchId",
      label: "Branch",
      type: "select",
      required: true,
      help: "The branch whose document numbering the adjustment takes.",
      options: branches.map((b) => ({ value: b.id, label: b.name })),
    },
    { name: "reason", label: "Reason", type: "select", required: true, options: REASON_OPTIONS },
    {
      name: "explanation",
      label: "Explanation",
      type: "textarea",
      required: true,
      help: "Say why the stock is being adjusted: stock does not vanish for no reason.",
    },
    {
      name: "productId",
      label: "Product",
      type: "select",
      required: true,
      options: products.map((p) => ({ value: p.id, label: `${p.sku} — ${p.name}` })),
    },
    {
      name: "quantity",
      label: "Quantity",
      type: "number",
      required: true,
      help: "Signed: positive writes stock on, negative writes it off.",
    },
    {
      name: "unitCost",
      label: "Unit cost",
      type: "number",
      help: "Required when writing stock on — it is what raises the moving average. Ignored on a write-off.",
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Stock adjustments</h1>
          <p className="mt-1 text-sm text-slate-500">
            Writing stock on or off, with a reason. Posts to the ledger immediately.
          </p>
        </div>

        <Can feature={FEATURES.adjustments} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New adjustment
          </button>
        </Can>
      </div>

      <Can
        feature={FEATURES.adjustments}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view adjustments.</p>}
      >
        <DataTable
          queryKey={["adjustments"]}
          endpoint="api/v1/inventory/adjustments"
          columns={columns}
          rowKey={(a) => a.id}
          searchPlaceholder="Search number or explanation…"
          emptyMessage="No adjustments yet."
        />
      </Can>

      {creating && (
        <EntityForm
          title="New adjustment"
          fields={fields}
          initial={{ reason: "2" }}
          submitLabel="Post adjustment"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            const quantity = Number(values.quantity);

            await api.post("api/v1/inventory/adjustments", {
              token: accessToken!,
              body: {
                warehouseId: values.warehouseId,
                branchId: values.branchId,
                // The select hands back a string; the API expects the numeric enum.
                reason: Number(values.reason),
                explanation: values.explanation,
                lines: [
                  {
                    productId: values.productId,
                    quantity,
                    // A write-off is valued at what the warehouse already thinks the stock is worth,
                    // so the cost is not sent at all.
                    unitCost: quantity > 0 ? Number(values.unitCost) : null,
                  },
                ],
              },
            });

            setCreating(false);
            await reload();
          }}
        />
      )}
    </div>
  );
}
