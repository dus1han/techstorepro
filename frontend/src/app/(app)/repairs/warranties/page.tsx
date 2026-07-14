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
import { acceptClaim, checkWarranty, registerWarranty, rejectClaim } from "@/features/repairs/api";
import {
  CLAIM_STATUS_LABELS,
  RepairWarrantyType,
  WARRANTY_TYPE_LABELS,
  WarrantyClaimStatus,
  type Warranty,
  type WarrantyClaim,
  type WarrantyCover,
} from "@/features/repairs/types";
import type { PagedResult } from "@/types/api";
import type { Product } from "@/types/catalog";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * Warranties and the claims made on them (requirements §30).
 *
 * **The shop's own warranty is not on this screen, and cannot be added to it.** P5 already computes it at
 * the moment of sale and stamps it on the unit, from the product's warranty months — so registering it
 * again here would give the same machine two expiry dates that could disagree, and the shop would believe
 * whichever one it happened to read. What is registered here is somebody *else's* promise: a
 * manufacturer's three years, a supplier's twelve months on an imported batch. Those are terms on a piece
 * of paper and nobody can compute them.
 *
 * The claims table is the one that earns its keep. It answers the question the shop's margin depends on
 * and which nothing else can: **which products keep coming back, and what is that costing us?**
 */
export default function WarrantiesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();
  const token = accessToken!;

  const [registering, setRegistering] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 200 }).data?.items ?? [];

  const reload = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["warranties"] }),
      client.invalidateQueries({ queryKey: ["warranty-claims"] }),
      client.invalidateQueries({ queryKey: ["repairs"] }),
    ]);

  const warrantyColumns: Column<Warranty>[] = [
    {
      key: "type",
      header: "Cover",
      render: (w) => <StatusBadge label={WARRANTY_TYPE_LABELS[w.warrantyType]} tone="neutral" />,
    },
    {
      key: "product",
      header: "Product & serial",
      render: (w) => (
        <div>
          <p>{w.productName}</p>
          {w.serialNumber && <p className="font-mono text-xs text-slate-500">{w.serialNumber}</p>}
        </div>
      ),
    },
    {
      key: "period",
      header: "Covered",
      render: (w) => {
        const expired = new Date(w.endsOn) < new Date();

        return (
          <span className={`text-xs ${expired ? "text-slate-400" : "text-slate-600 dark:text-slate-300"}`}>
            {new Date(w.startsOn).toLocaleDateString()} – {new Date(w.endsOn).toLocaleDateString()}
            {expired && " (expired)"}
          </span>
        );
      },
    },
    {
      key: "claims",
      header: "Open claims",
      align: "right",
      render: (w) => <span className="tabular-nums">{w.openClaims || "—"}</span>,
    },
  ];

  const claimColumns: Column<WarrantyClaim>[] = [
    {
      key: "product",
      header: "Machine",
      render: (c) => (
        <div>
          <p>{c.productName}</p>
          {c.serialNumber && <p className="font-mono text-xs text-slate-500">{c.serialNumber}</p>}
        </div>
      ),
    },
    {
      key: "job",
      header: "Job",
      render: (c) =>
        c.repairTicketNumber
          ? <span className="font-mono text-xs">{c.repairTicketNumber}</span>
          : <span className="text-xs text-slate-400">—</span>,
    },
    {
      key: "type",
      header: "Cover",
      render: (c) => <StatusBadge label={WARRANTY_TYPE_LABELS[c.warrantyType]} tone="neutral" />,
    },
    {
      key: "status",
      header: "Status",
      render: (c) => (
        <StatusBadge
          label={CLAIM_STATUS_LABELS[c.status]}
          tone={
            c.status === WarrantyClaimStatus.Accepted
              ? "good"
              : c.status === WarrantyClaimStatus.Rejected
                ? "bad"
                : "warn"
          }
        />
      ),
    },
    {
      key: "cost",
      header: "Cost to the shop",
      align: "right",
      render: (c) => (
        <div>
          <Money amount={c.costToShop} />
          {c.outcome && <p className="text-xs text-slate-500">{c.outcome}</p>}
        </div>
      ),
    },
  ];

  const fields: FieldSpec[] = [
    {
      name: "warrantyType",
      label: "Whose warranty",
      type: "select",
      required: true,
      options: [
        { value: String(RepairWarrantyType.Manufacturer), label: "Manufacturer" },
        { value: String(RepairWarrantyType.Supplier), label: "Supplier" },
      ],
      help: "The shop's own is set by the sale, from the product's warranty months. It is not registered here.",
    },
    {
      name: "productId",
      label: "Product",
      type: "select",
      required: true,
      options: products.map((p) => ({ value: p.id, label: `${p.sku} — ${p.name}` })),
    },
    {
      name: "serialNumber",
      label: "Serial number",
      type: "text",
      help: "A warranty is a promise about a specific machine. Without a serial nothing can be matched to it.",
      wide: true,
    },
    { name: "startsOn", label: "Starts", type: "date", required: true },
    { name: "endsOn", label: "Ends", type: "date", required: true },
    { name: "terms", label: "Terms", type: "textarea", wide: true },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-8">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Warranties &amp; claims</h1>
          <p className="mt-1 text-sm text-slate-500">
            The shop&apos;s own warranty is derived from the sale and needs no record here. What is registered
            is somebody else&apos;s promise — a manufacturer&apos;s, or a supplier&apos;s.
          </p>
        </div>

        <Can feature={FEATURES.warranties} action={PermissionAction.Create}>
          <button
            onClick={() => setRegistering(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Register a warranty
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <CounterCheck />

      <Can
        feature={FEATURES.warranties}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view warranties.</p>}
      >
        <section className="space-y-3">
          <h2 className="text-sm font-medium">Registered warranties</h2>

          <DataTable
            queryKey={["warranties"]}
            endpoint="api/v1/warranties"
            columns={warrantyColumns}
            rowKey={(w) => w.id}
            searchPlaceholder="Search serial or product…"
            emptyMessage="No manufacturer or supplier warranties registered."
          />
        </section>

        <section className="space-y-3">
          <div>
            <h2 className="text-sm font-medium">Claims</h2>
            <p className="text-xs text-slate-500">
              Rejecting a claim is what makes the job chargeable — the parts and labour already booked to it
              as warranty work become billable, and the shop stops eating them.
            </p>
          </div>

          <DataTable
            queryKey={["warranty-claims"]}
            endpoint="api/v1/warranties/claims"
            columns={claimColumns}
            rowKey={(c) => c.id}
            searchPlaceholder="Search serial, product or job…"
            emptyMessage="No claims yet."
            actions={(claim) =>
              claim.status !== WarrantyClaimStatus.Open ? null : (
                <div className="flex justify-end gap-2">
                  <Can feature={FEATURES.warranties} action={PermissionAction.Approve}>
                    <button
                      onClick={() => void run(claim.id, () => acceptClaim({ token }, claim.id, "Honoured."))}
                      disabled={busy === claim.id}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Honour
                    </button>

                    <button
                      onClick={() => {
                        // A refusal with no reason is a dispute. The customer is about to be charged for a
                        // repair they believed was free.
                        const outcome = window.prompt("Why is this claim being refused?");
                        if (!outcome) return;

                        void run(claim.id, () => rejectClaim({ token }, claim.id, outcome));
                      }}
                      disabled={busy === claim.id}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Refuse
                    </button>
                  </Can>
                </div>
              )
            }
          />
        </section>
      </Can>

      {registering && (
        <EntityForm
          title="Register a warranty"
          fields={fields}
          submitLabel="Register"
          onClose={() => setRegistering(false)}
          onSubmit={async (values) => {
            await registerWarranty(
              { token },
              {
                warrantyType: Number(values.warrantyType) as RepairWarrantyType,
                productId: String(values.productId),
                startsOn: String(values.startsOn),
                endsOn: String(values.endsOn),
                serialNumber: values.serialNumber ? String(values.serialNumber) : null,
                terms: values.terms ? String(values.terms) : null,
              },
            );

            setRegistering(false);
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
 * "Is this machine still under warranty?" — asked from the counter, before anything is booked in, because
 * the customer is standing there and wants to know.
 *
 * It creates no job sheet, on purpose: a customer who hears the answer and decides to take the machine
 * home should not leave a ticket behind them.
 */
function CounterCheck() {
  const { accessToken } = useAuth();

  const [serial, setSerial] = useState("");
  const [cover, setCover] = useState<WarrantyCover | null>(null);
  const [busy, setBusy] = useState(false);

  return (
    <section className="rounded-lg border border-slate-200 p-4 dark:border-slate-800">
      <h2 className="text-sm font-medium">Check a machine</h2>

      <div className="mt-3 flex gap-2">
        <input
          value={serial}
          onChange={(e) => setSerial(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") void check();
          }}
          placeholder="Scan or type the serial number"
          className="flex-1 rounded-md border border-slate-200 px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-900"
        />

        <button
          onClick={() => void check()}
          disabled={busy || !serial.trim()}
          className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
        >
          Check
        </button>
      </div>

      {cover && (
        <p
          className={`mt-3 rounded-md px-3 py-2 text-sm ${
            cover.isCovered
              ? "bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
              : "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300"
          }`}
        >
          {cover.explanation}
        </p>
      )}
    </section>
  );

  async function check() {
    if (!serial.trim()) return;

    setBusy(true);

    try {
      setCover(await checkWarranty({ token: accessToken! }, serial.trim()));
    } catch {
      setCover(null);
    } finally {
      setBusy(false);
    }
  }
}
