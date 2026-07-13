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
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";
import { StockCountStatus, type CountDto } from "@/types/inventory";

interface WarehouseOption {
  id: string;
  name: string;
}

const STATUS_LABELS: Record<number, string> = {
  1: "Counting",
  2: "Pending approval",
  3: "Approved",
  4: "Cancelled",
};

/**
 * Physical stock counts.
 *
 * Approving a count posts its variance as an ordinary adjustment — it is the only place in the module
 * where stock is created or destroyed on somebody's say-so. That is why Approve is a separate
 * permission from counting: the person walking the shelves and the person authorising the write-off
 * need not be the same person.
 */
export default function StockCountsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);

  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];

  const reload = () => client.invalidateQueries({ queryKey: ["stock-counts"] });

  const columns: Column<CountDto>[] = [
    { key: "number", header: "Number", render: (c) => <span className="font-mono text-xs">{c.number}</span> },
    { key: "warehouse", header: "Warehouse", render: (c) => c.warehouseName },
    { key: "status", header: "Status", render: (c) => <span className="text-xs">{STATUS_LABELS[c.status]}</span> },
    {
      key: "startedAt",
      header: "Started",
      render: (c) => (
        <span className="text-xs tabular-nums text-slate-600 dark:text-slate-400">
          {new Date(c.startedAt).toLocaleString()}
        </span>
      ),
    },
    { key: "lines", header: "Lines", align: "right", render: (c) => c.lines.length },
    {
      key: "variance",
      header: "Variance",
      align: "right",
      render: (c) => (
        <div>
          <p className={c.netVarianceValue < 0 ? "text-red-600 dark:text-red-400" : ""}>
            {c.netVarianceValue.toFixed(2)}
          </p>
          <p className="text-xs text-slate-500">
            {c.varianceLineCount} line{c.varianceLineCount === 1 ? "" : "s"}
          </p>
        </div>
      ),
    },
  ];

  const fields: FieldSpec[] = [
    {
      name: "warehouseId",
      label: "Warehouse",
      type: "select",
      required: true,
      help: "One open count per warehouse: two people counting the same shelves would both post the same write-off.",
      options: warehouses.map((w) => ({ value: w.id, label: w.name })),
    },
    {
      name: "branchId",
      label: "Branch",
      type: "select",
      required: true,
      options: branches.map((b) => ({ value: b.id, label: b.name })),
    },
    { name: "notes", label: "Notes", type: "textarea" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Physical stock counts</h1>
          <p className="mt-1 text-sm text-slate-500">
            Counting the shelves. Nothing posts to the ledger until the count is approved.
          </p>
        </div>

        <Can feature={FEATURES.stockCounts} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Start count
          </button>
        </Can>
      </div>

      <Can
        feature={FEATURES.stockCounts}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view stock counts.</p>}
      >
        <DataTable
          queryKey={["stock-counts"]}
          endpoint="api/v1/inventory/counts"
          columns={columns}
          rowKey={(c) => c.id}
          searchPlaceholder="Search count number…"
          emptyMessage="No stock counts yet."
          actions={(count) => (
            <>
              {/* Counting → PendingApproval. Without this the count can never leave Counting, and the
                  Approve button below is unreachable no matter who holds the permission. */}
              {count.status === StockCountStatus.Counting && (
                <Can feature={FEATURES.stockCounts} action={PermissionAction.Edit}>
                  <button
                    onClick={() => void submit(count)}
                    className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                  >
                    Submit
                  </button>
                </Can>
              )}

              {count.status === StockCountStatus.PendingApproval && (
                <Can feature={FEATURES.stockCounts} action={PermissionAction.Approve}>
                  <button
                    onClick={() => void approve(count)}
                    className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                  >
                    Approve
                  </button>
                </Can>
              )}
            </>
          )}
        />
      </Can>

      {creating && (
        <EntityForm
          title="Start stock count"
          fields={fields}
          submitLabel="Start count"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await api.post("api/v1/inventory/counts", {
              token: accessToken!,
              body: {
                warehouseId: values.warehouseId,
                branchId: values.branchId,
                notes: values.notes || null,
              },
            });

            setCreating(false);
            await reload();
          }}
        />
      )}
    </div>
  );

  async function submit(count: CountDto) {
    if (!accessToken) return;

    await api.post(`api/v1/inventory/counts/${count.id}/submit`, { token: accessToken });
    await reload();
  }

  async function approve(count: CountDto) {
    if (!accessToken) return;

    // Approval authorises the write-off the count computed, so it asks first. The server checks the
    // permission again regardless of what this dialog decides.
    const confirmed = window.confirm(
      `Approve ${count.number}? Its variance of ${count.netVarianceValue.toFixed(2)} posts to the ledger as an adjustment.`,
    );

    if (!confirmed) return;

    await api.post(`api/v1/inventory/counts/${count.id}/approve`, { token: accessToken });
    await reload();
  }
}
