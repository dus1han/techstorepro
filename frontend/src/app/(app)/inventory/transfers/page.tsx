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
import { TransferStatus, type TransferDto } from "@/types/inventory";

interface WarehouseOption {
  id: string;
  name: string;
}

const STATUS_LABELS: Record<number, string> = {
  1: "Draft",
  2: "In transit",
  3: "Received",
  4: "Cancelled",
};

/**
 * Stock transfers between warehouses.
 *
 * Nothing moves when the paperwork is typed. Stock leaves the source when the van is loaded (Ship)
 * and arrives when somebody signs for it (Receive) — in between it is in transit, owned by neither
 * end and sellable from neither.
 */
export default function TransfersPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);

  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 100 }).data?.items ?? [];

  const reload = () => client.invalidateQueries({ queryKey: ["transfers"] });

  const columns: Column<TransferDto>[] = [
    { key: "number", header: "Number", render: (t) => <span className="font-mono text-xs">{t.number}</span> },
    {
      key: "route",
      header: "Route",
      render: (t) => (
        <div>
          <p className="font-medium">
            {t.fromWarehouseName} → {t.toWarehouseName}
          </p>
          <p className="text-xs text-slate-500">
            {t.lines.length} line{t.lines.length === 1 ? "" : "s"}
          </p>
        </div>
      ),
    },
    {
      key: "status",
      header: "Status",
      render: (t) => (
        <div className="text-xs">
          <p>{STATUS_LABELS[t.status]}</p>
          {/* A short delivery is recorded, not rounded away: somebody has to explain the units. */}
          {t.hasShortfall && t.status === TransferStatus.Received && (
            <p className="text-red-600 dark:text-red-400">Short delivery</p>
          )}
        </div>
      ),
    },
    {
      key: "shippedAt",
      header: "Shipped",
      render: (t) =>
        t.shippedAt ? (
          <span className="text-xs tabular-nums">{new Date(t.shippedAt).toLocaleString()}</span>
        ) : (
          <span className="text-slate-400">—</span>
        ),
    },
    {
      key: "receivedAt",
      header: "Received",
      render: (t) =>
        t.receivedAt ? (
          <span className="text-xs tabular-nums">{new Date(t.receivedAt).toLocaleString()}</span>
        ) : (
          <span className="text-slate-400">—</span>
        ),
    },
  ];

  const fields: FieldSpec[] = [
    {
      name: "fromWarehouseId",
      label: "From warehouse",
      type: "select",
      required: true,
      options: warehouses.map((w) => ({ value: w.id, label: w.name })),
    },
    {
      name: "toWarehouseId",
      label: "To warehouse",
      type: "select",
      required: true,
      options: warehouses.map((w) => ({ value: w.id, label: w.name })),
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
    { name: "notes", label: "Notes", type: "textarea" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Stock transfers</h1>
          <p className="mt-1 text-sm text-slate-500">
            Moving stock between warehouses. A draft moves nothing until it ships.
          </p>
        </div>

        <Can feature={FEATURES.transfers} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New transfer
          </button>
        </Can>
      </div>

      <Can
        feature={FEATURES.transfers}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view transfers.</p>}
      >
        <DataTable
          queryKey={["transfers"]}
          endpoint="api/v1/inventory/transfers"
          columns={columns}
          rowKey={(t) => t.id}
          searchPlaceholder="Search transfer number…"
          emptyMessage="No transfers yet."
          actions={(transfer) => (
            <Can feature={FEATURES.transfers} action={PermissionAction.Edit}>
              {transfer.status === TransferStatus.Draft && (
                <button
                  onClick={() => void ship(transfer)}
                  className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                >
                  Ship
                </button>
              )}
              {transfer.status === TransferStatus.InTransit && (
                <button
                  onClick={() => void receive(transfer)}
                  className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                >
                  Receive
                </button>
              )}
            </Can>
          )}
        />
      </Can>

      {creating && (
        <EntityForm
          title="New transfer"
          fields={fields}
          submitLabel="Create transfer"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await api.post("api/v1/inventory/transfers", {
              token: accessToken!,
              body: {
                fromWarehouseId: values.fromWarehouseId,
                toWarehouseId: values.toWarehouseId,
                branchId: values.branchId,
                lines: [{ productId: values.productId, quantity: Number(values.quantity) }],
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

  async function ship(transfer: TransferDto) {
    if (!accessToken) return;

    await api.post(`api/v1/inventory/transfers/${transfer.id}/ship`, { token: accessToken });
    await reload();
  }

  async function receive(transfer: TransferDto) {
    if (!accessToken) return;

    // No body means "all of it arrived", which is the overwhelmingly common case. A short delivery is
    // declared line by line, which this screen does not yet do.
    await api.post(`api/v1/inventory/transfers/${transfer.id}/receive`, { token: accessToken });
    await reload();
  }
}
