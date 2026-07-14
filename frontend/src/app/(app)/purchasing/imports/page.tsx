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
import { addImportCharge, apportionLandedCost, createImportShipment } from "@/features/purchasing/api";
import {
  CHARGE_TYPE_LABELS,
  ImportChargeType,
  ImportShipmentStatus,
  SHIPMENT_STATUS_LABELS,
  type ApportionmentResult,
  type ImportShipment,
} from "@/features/purchasing/types";
import type { PagedResult } from "@/types/api";
import type { Supplier } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

/**
 * Import shipments and landed cost (§26).
 *
 * **Goods and their true cost do not arrive together.** The container is unpacked in March and the
 * clearing agent invoices in April. That single fact is why this screen exists as a separate step from
 * receiving: the receipt has to post when the goods physically arrive — the shop cannot refuse to book
 * stock it can see on the shelf — so the freight, duty and clearing are folded in afterwards, as a
 * revaluation that moves money into stock without inventing a unit.
 *
 * **Apportioning is the most consequential action in purchasing.** Costing is weighted average (D1), so
 * the money it moves does not merely price this container — it feeds the moving average of every product
 * in it and spreads to units that arrived years ago, where it never washes out. It can be done exactly
 * once, and it is gated on Approve rather than Edit for precisely that reason.
 *
 * When the container sold out before its clearing invoice arrived, some of the cost has nowhere to live.
 * That money is **reported, not hidden**: dropping it would overstate margin, and smearing it over
 * whatever else is on the shelf would charge one container's freight to another's goods.
 */
export default function ImportShipmentsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);
  const [charging, setCharging] = useState<ImportShipment | null>(null);
  const [result, setResult] = useState<ApportionmentResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const suppliers =
    useApiQuery<PagedResult<Supplier>>(["suppliers"], "api/v1/suppliers", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];

  // Apportioning revalues stock, so the inventory screens go stale with it.
  const reload = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["import-shipments"] }),
      client.invalidateQueries({ queryKey: ["goods-receipts"] }),
      client.invalidateQueries({ queryKey: ["stock"] }),
      client.invalidateQueries({ queryKey: ["stock-movements"] }),
    ]);

  const columns: Column<ImportShipment>[] = [
    {
      key: "number",
      header: "Shipment",
      render: (s) => (
        <div>
          <p className="font-mono text-xs">{s.number}</p>
          {s.transportDocument && (
            <p className="font-mono text-xs text-slate-500">{s.transportDocument}</p>
          )}
        </div>
      ),
    },
    { key: "supplier", header: "Supplier", render: (s) => s.supplierName },
    {
      key: "status",
      header: "Status",
      render: (s) => (
        <div>
          <StatusBadge label={SHIPMENT_STATUS_LABELS[s.status]} tone={tone(s.status)} />
          <p className="mt-0.5 text-xs text-slate-500">
            {s.receiptCount} {s.receiptCount === 1 ? "receipt" : "receipts"}
          </p>
        </div>
      ),
    },
    {
      key: "charges",
      header: "Charges",
      render: (s) => (
        <div className="space-y-0.5">
          {s.charges.map((charge) => (
            <p key={charge.id} className="text-xs">
              {CHARGE_TYPE_LABELS[charge.type]} <Money amount={charge.amountBase} />
              {charge.currencyCode !== "AED" && (
                <span className="text-slate-500">
                  {" "}
                  ({charge.amount.toLocaleString()} {charge.currencyCode})
                </span>
              )}
            </p>
          ))}

          {s.charges.length === 0 && <span className="text-xs text-slate-400">None yet</span>}
        </div>
      ),
    },
    {
      key: "total",
      header: "Landed cost",
      align: "right",
      render: (s) => (
        <div>
          <Money amount={s.totalCharges} />

          {/* Real money that reached the shipment after its goods had already been sold. It is on the
              document, visible and attributable, rather than quietly dropped. */}
          {s.unabsorbedCost > 0 && (
            <p className="text-xs text-amber-600 dark:text-amber-400">
              <Money amount={s.unabsorbedCost} /> unabsorbed
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
      name: "transportDocument",
      label: "Bill of lading / AWB",
      help: "The transport document. What the shipping line and the clearing agent will both quote.",
    },
    { name: "vesselOrFlight", label: "Vessel or flight" },
    { name: "portOfLoading", label: "Port of loading" },
    { name: "portOfDischarge", label: "Port of discharge" },
    { name: "shippedAt", label: "Shipped", type: "date" },
    { name: "expectedAt", label: "Expected", type: "date" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Import shipments</h1>
          <p className="mt-1 text-sm text-slate-500">
            The container lands in March; the clearing agent invoices in April. Stock posts at the goods
            price, and the freight is folded in afterwards — once.
          </p>
        </div>

        <Can feature={FEATURES.importShipments} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New shipment
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      {result && <ApportionmentReport result={result} onDismiss={() => setResult(null)} />}

      <Can
        feature={FEATURES.importShipments}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view import shipments.</p>}
      >
        <DataTable
          queryKey={["import-shipments"]}
          endpoint="api/v1/import-shipments"
          columns={columns}
          rowKey={(s) => s.id}
          searchPlaceholder="Search shipment number or transport document…"
          emptyMessage="No shipments yet."
          actions={(shipment) => {
            // Charges may be added right up until the container is costed — and not after, because by
            // then the money is already inside the moving average and a late charge would need a second
            // apportionment, which is exactly what must never happen.
            const open =
              shipment.status === ImportShipmentStatus.InTransit ||
              shipment.status === ImportShipmentStatus.Arrived;

            const costable =
              shipment.status === ImportShipmentStatus.Arrived &&
              shipment.charges.length > 0 &&
              shipment.receiptCount > 0;

            return (
              <div className="flex justify-end gap-2">
                {open && (
                  <Can feature={FEATURES.importShipments} action={PermissionAction.Edit}>
                    <button
                      onClick={() => setCharging(shipment)}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Add charge
                    </button>
                  </Can>
                )}

                {costable && (
                  <Can feature={FEATURES.importShipments} action={PermissionAction.Approve}>
                    <button
                      onClick={() => {
                        // It cannot be undone: the money goes into the moving average, and because the
                        // average is moving it never washes back out. Worth one confirmation.
                        const ok = window.confirm(
                          `Fold ${shipment.totalCharges.toLocaleString()} of charges into the cost of ${shipment.number}'s goods?\n\n` +
                            "This feeds the moving average and cannot be undone or repeated.",
                        );

                        if (!ok) return;

                        void run(shipment.id, async () => {
                          const outcome = await apportionLandedCost(
                            { token: accessToken! },
                            shipment.id,
                          );

                          setResult(outcome);
                        });
                      }}
                      disabled={busy === shipment.id}
                      className="rounded-md bg-sky-600 px-2.5 py-1 text-xs font-medium text-white hover:opacity-90 disabled:opacity-40"
                    >
                      {busy === shipment.id ? "Costing…" : "Apportion"}
                    </button>
                  </Can>
                )}
              </div>
            );
          }}
        />
      </Can>

      {creating && (
        <EntityForm
          title="New import shipment"
          fields={fields}
          submitLabel="Create"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await createImportShipment(
              { token: accessToken! },
              {
                supplierId: String(values.supplierId),
                branchId: String(values.branchId),
                transportDocument: values.transportDocument ? String(values.transportDocument) : null,
                vesselOrFlight: values.vesselOrFlight ? String(values.vesselOrFlight) : null,
                portOfLoading: values.portOfLoading ? String(values.portOfLoading) : null,
                portOfDischarge: values.portOfDischarge ? String(values.portOfDischarge) : null,
                shippedAt: values.shippedAt ? new Date(String(values.shippedAt)).toISOString() : null,
                expectedAt: values.expectedAt ? new Date(String(values.expectedAt)).toISOString() : null,
              },
            );

            setCreating(false);
            await reload();
          }}
        />
      )}

      {charging && (
        <EntityForm
          title={`Charge ${charging.number}`}
          fields={[
            {
              name: "type",
              label: "Charge",
              type: "select",
              required: true,
              options: Object.entries(CHARGE_TYPE_LABELS).map(([value, label]) => ({ value, label })),
            },
            { name: "amount", label: "Amount", type: "number", required: true },
            { name: "currencyCode", label: "Currency", placeholder: "AED" },
            {
              name: "exchangeRate",
              label: "Exchange rate",
              type: "number",
              help: "The shipping line bills USD and the customs authority bills AED. A container's charges can only be added up in one currency.",
            },
            { name: "vendor", label: "Billed by" },
            { name: "reference", label: "Their reference" },
            { name: "description", label: "Description", wide: true },
          ]}
          initial={{ currencyCode: "AED", exchangeRate: 1, type: String(ImportChargeType.Freight) }}
          submitLabel="Add charge"
          onClose={() => setCharging(null)}
          onSubmit={async (values) => {
            await addImportCharge({ token: accessToken! }, charging.id, {
              type: Number(values.type) as ImportChargeType,
              amount: Number(values.amount),
              currencyCode: String(values.currencyCode || "AED"),
              exchangeRate: Number(values.exchangeRate) || 1,
              vendor: values.vendor ? String(values.vendor) : null,
              reference: values.reference ? String(values.reference) : null,
              description: values.description ? String(values.description) : null,
            });

            setCharging(null);
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

/**
 * What the apportionment actually did.
 *
 * It is shown rather than swallowed because this is the number that feeds the moving average: the user
 * who just pressed the button is the only person who will ever be in a position to notice that the
 * landed cost came out wrong, and they can only notice it if they are shown it.
 */
function ApportionmentReport({
  result,
  onDismiss,
}: {
  result: ApportionmentResult;
  onDismiss: () => void;
}) {
  return (
    <div className="space-y-3 rounded-lg border border-sky-200 bg-sky-50 p-4 dark:border-sky-900 dark:bg-sky-950">
      <div className="flex items-start justify-between">
        <div>
          <p className="text-sm font-medium text-sky-900 dark:text-sky-200">Landed cost apportioned</p>
          <p className="mt-0.5 text-xs text-sky-800 dark:text-sky-300">
            <Money amount={result.absorbed} /> of <Money amount={result.totalCharges} /> reached the
            stock.
            {result.unabsorbed > 0 && (
              <>
                {" "}
                <Money amount={result.unabsorbed} /> could not — those goods had already been sold, so
                there was nothing left on the shelf to carry it. It is recorded on the shipment.
              </>
            )}
          </p>
        </div>

        <button
          onClick={onDismiss}
          className="shrink-0 rounded-md border border-sky-300 px-2 py-0.5 text-xs text-sky-900 dark:border-sky-800 dark:text-sky-200"
        >
          Dismiss
        </button>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-left text-sky-800 dark:text-sky-300">
            <tr>
              <th className="py-1 pr-4 font-medium">Product</th>
              <th className="py-1 pr-4 text-right font-medium">Qty</th>
              <th className="py-1 pr-4 text-right font-medium">Goods value</th>
              <th className="py-1 pr-4 text-right font-medium">Cost added</th>
              <th className="py-1 text-right font-medium">Landed, each</th>
            </tr>
          </thead>
          <tbody className="text-sky-900 dark:text-sky-100">
            {result.lines.map((line) => (
              <tr key={line.goodsReceiptLineId} className="border-t border-sky-200 dark:border-sky-900">
                <td className="py-1 pr-4">{line.productName}</td>
                <td className="py-1 pr-4 text-right tabular-nums">{line.quantity.toLocaleString()}</td>
                <td className="py-1 pr-4 text-right">
                  <Money amount={line.lineValueBase} />
                </td>
                <td className="py-1 pr-4 text-right">
                  <Money amount={line.apportionedCost} />
                </td>
                <td className="py-1 text-right font-medium">
                  <Money amount={line.landedUnitCost} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function tone(status: ImportShipmentStatus) {
  if (status === ImportShipmentStatus.Costed) return "good" as const;
  if (status === ImportShipmentStatus.Cancelled) return "bad" as const;
  if (status === ImportShipmentStatus.Arrived) return "warn" as const;

  return "neutral" as const;
}
