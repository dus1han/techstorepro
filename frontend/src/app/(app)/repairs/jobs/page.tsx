"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { Money, StatusBadge } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import {
  approveEstimate,
  beginDiagnosis,
  beginTesting,
  bookInDevice,
  cancelRepair,
  checkWarranty,
  declineEstimate,
  deliverDevice,
  markReady,
  recordDiagnosis,
} from "@/features/repairs/api";
import { JobSheet, tone } from "@/features/repairs/components/job-sheet";
import {
  REPAIR_STATUS_LABELS,
  RepairTicketStatus,
  WARRANTY_TYPE_LABELS,
  type RepairTicket,
  type WarrantyCover,
} from "@/features/repairs/types";
import type { PagedResult } from "@/types/api";
import type { Customer } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

const INPUT =
  "w-full rounded-md border border-slate-200 px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-900";

/**
 * The workshop board (requirements §28).
 *
 * Open jobs first, **promised-date first within them**: the job that is late is the one the shop needs to
 * see, and a list sorted by when things arrived buries it under everything that arrived after it.
 *
 * The buttons on a row are the transitions the job can actually make. This is a state machine, not a
 * status dropdown — a dropdown would let anyone move a job anywhere, and the rule that matters most
 * (nothing is fitted before the customer approves the estimate) would be enforced by whoever remembered.
 */
export default function RepairJobsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();
  const token = accessToken!;

  const [booking, setBooking] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [openOnly, setOpenOnly] = useState(true);

  /**
   * Fitting a part moves stock, so the inventory screens go stale with it — and the job that was billed
   * has just put an invoice on the customer's account.
   */
  const reload = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["repairs"] }),
      client.invalidateQueries({ queryKey: ["warranty-claims"] }),
      client.invalidateQueries({ queryKey: ["stock"] }),
      client.invalidateQueries({ queryKey: ["stock-movements"] }),
      client.invalidateQueries({ queryKey: ["serials"] }),
      client.invalidateQueries({ queryKey: ["sales-invoices"] }),
    ]);

  const columns: Column<RepairTicket>[] = [
    {
      key: "number",
      header: "Job",
      render: (t) => (
        <div>
          <p className="font-mono text-xs">{t.number}</p>
          <p className="text-xs text-slate-500">{new Date(t.receivedAt).toLocaleDateString()}</p>
        </div>
      ),
    },
    {
      key: "customer",
      header: "Customer & device",
      render: (t) => (
        <div>
          <p>{t.customerName}</p>
          <p className="text-xs text-slate-500">
            {t.deviceProductName ?? "Device"}
            {t.deviceSerialNumber && ` · ${t.deviceSerialNumber}`}
          </p>
        </div>
      ),
    },
    {
      key: "fault",
      header: "Fault",
      render: (t) => <span className="text-xs text-slate-500">{t.reportedFault}</span>,
    },
    {
      key: "status",
      header: "Status",
      render: (t) => (
        <div className="flex flex-col items-start gap-1">
          <StatusBadge label={REPAIR_STATUS_LABELS[t.status]} tone={tone(t.status)} />

          {/* Who is paying. It is the first thing anyone needs to know about a job, and it was decided at
              the door by looking the serial up — not by someone ticking a box. */}
          {t.isWarranty && (
            <StatusBadge label={WARRANTY_TYPE_LABELS[t.warrantyType]} tone="warn" />
          )}
        </div>
      ),
    },
    {
      key: "promised",
      header: "Promised",
      render: (t) => {
        if (!t.promisedAt) return <span className="text-xs text-slate-400">—</span>;

        const late =
          new Date(t.promisedAt) < new Date()
          && t.status !== RepairTicketStatus.Delivered
          && t.status !== RepairTicketStatus.Cancelled;

        return (
          <span className={`text-xs ${late ? "font-medium text-red-600 dark:text-red-400" : "text-slate-500"}`}>
            {new Date(t.promisedAt).toLocaleDateString()}
          </span>
        );
      },
    },
    {
      key: "money",
      header: "Profit",
      align: "right",
      render: (t) => (
        <span className={t.grossProfit < 0 ? "text-red-600 dark:text-red-400" : ""}>
          <Money amount={t.grossProfit} />
        </span>
      ),
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Repair jobs</h1>
          <p className="mt-1 text-sm text-slate-500">
            The device on the bench is the customer&apos;s, not stock. What moves is the parts fitted to it —
            and nothing is fitted until the customer has agreed to the price.
          </p>
        </div>

        <Can feature={FEATURES.repairTickets} action={PermissionAction.Create}>
          <button
            onClick={() => setBooking(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Book in a device
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <label className="flex items-center gap-2 text-sm text-slate-500">
        <input
          type="checkbox"
          checked={openOnly}
          onChange={(e) => setOpenOnly(e.target.checked)}
          className="rounded border-slate-300"
        />
        Only jobs still on the bench (the pending-repairs report)
      </label>

      <Can
        feature={FEATURES.repairTickets}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view repair jobs.</p>}
      >
        <DataTable
          queryKey={["repairs", openOnly]}
          endpoint="api/v1/repairs"
          columns={columns}
          filters={{ openOnly }}
          rowKey={(t) => t.id}
          searchPlaceholder="Search job, customer, serial or fault…"
          emptyMessage="Nothing in the workshop."
          actions={(ticket) => (
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setOpen(ticket.id)}
                className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
              >
                Open
              </button>

              {ticket.status === RepairTicketStatus.Received && (
                <Can feature={FEATURES.repairTickets} action={PermissionAction.Edit}>
                  <Action
                    label="Diagnose"
                    busy={busy === ticket.id}
                    onClick={() => void run(ticket.id, () => beginDiagnosis({ token }, ticket.id))}
                  />
                </Can>
              )}

              {ticket.status === RepairTicketStatus.Diagnosing && (
                <Can feature={FEATURES.repairTickets} action={PermissionAction.Edit}>
                  <Action
                    label="Record findings"
                    busy={busy === ticket.id}
                    onClick={() => {
                      const findings = window.prompt("What did you find?");
                      if (!findings) return;

                      // A warranty job needs no estimate — the customer is not paying, so there is nothing
                      // for them to agree to, and the server sends it straight to the bench.
                      const estimate = ticket.isWarranty
                        ? null
                        : window.prompt("What will it cost the customer?");

                      if (!ticket.isWarranty && estimate === null) return;

                      void run(ticket.id, () =>
                        recordDiagnosis({ token }, ticket.id, {
                          findings,
                          estimatedCost: estimate === null ? null : Number(estimate) || 0,
                        }),
                      );
                    }}
                  />
                </Can>
              )}

              {ticket.status === RepairTicketStatus.AwaitingApproval && (
                <>
                  {/* Approve is the customer's yes, not a manager's. It is what unlocks the parts store. */}
                  <Can feature={FEATURES.repairTickets} action={PermissionAction.Approve}>
                    <Action
                      label="Customer approved"
                      busy={busy === ticket.id}
                      onClick={() => void run(ticket.id, () => approveEstimate({ token }, ticket.id))}
                    />

                    <Action
                      label="Declined"
                      busy={busy === ticket.id}
                      onClick={() => {
                        const reason = window.prompt("Why did the customer decline?");
                        if (!reason) return;

                        void run(ticket.id, () => declineEstimate({ token }, ticket.id, reason));
                      }}
                    />
                  </Can>
                </>
              )}

              {ticket.status === RepairTicketStatus.InRepair && (
                <Can feature={FEATURES.repairTickets} action={PermissionAction.Edit}>
                  <Action
                    label="Test"
                    busy={busy === ticket.id}
                    onClick={() => void run(ticket.id, () => beginTesting({ token }, ticket.id))}
                  />
                </Can>
              )}

              {(ticket.status === RepairTicketStatus.Testing
                || ticket.status === RepairTicketStatus.InRepair) && (
                <Can feature={FEATURES.repairTickets} action={PermissionAction.Edit}>
                  <Action
                    label="Ready"
                    busy={busy === ticket.id}
                    onClick={() => void run(ticket.id, () => markReady({ token }, ticket.id))}
                  />
                </Can>
              )}

              {ticket.status === RepairTicketStatus.Ready && (
                <Can feature={FEATURES.repairTickets} action={PermissionAction.Edit}>
                  {/* Handing the machine back does not require the bill to be paid. A shop that held a
                      customer's own laptop hostage over an invoice would be doing exactly that. */}
                  <Action
                    label="Collected"
                    busy={busy === ticket.id}
                    onClick={() => {
                      const by = window.prompt("Who collected it?") ?? null;

                      void run(ticket.id, () => deliverDevice({ token }, ticket.id, by));
                    }}
                  />
                </Can>
              )}

              {!ticket.isWarranty
                && ticket.status !== RepairTicketStatus.Delivered
                && ticket.status !== RepairTicketStatus.Cancelled
                && ticket.parts.length === 0 && (
                <Can feature={FEATURES.repairTickets} action={PermissionAction.Delete}>
                  <Action
                    label="Cancel"
                    busy={busy === ticket.id}
                    onClick={() => {
                      const reason = window.prompt("Why is this job being cancelled?");
                      if (!reason) return;

                      void run(ticket.id, () => cancelRepair({ token }, ticket.id, reason));
                    }}
                  />
                </Can>
              )}
            </div>
          )}
        />
      </Can>

      {booking && (
        <BookInDialog
          onClose={() => setBooking(false)}
          onBooked={async () => {
            setBooking(false);
            await reload();
          }}
        />
      )}

      {open && (
        <JobSheet
          ticketId={open}
          onClose={() => setOpen(null)}
          onChanged={reload}
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

function Action({ label, busy, onClick }: { label: string; busy: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      disabled={busy}
      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
    >
      {label}
    </button>
  );
}

/**
 * Intake.
 *
 * The serial box checks the warranty **as it is typed**, and that is the point of the screen. The clerk
 * does not decide whether a repair is free: the system looks the machine up, finds the sale that put it in
 * the customer's hands, and says so in words the clerk can repeat to the customer standing in front of
 * them. A tickbox here is exactly how a shop ends up billing someone for a repair it had already promised
 * to do for nothing.
 */
function BookInDialog({ onClose, onBooked }: { onClose: () => void; onBooked: () => Promise<void> }) {
  const { accessToken } = useAuth();
  const token = accessToken!;

  const [form, setForm] = useState({
    customerId: "",
    branchId: "",
    deviceSerialNumber: "",
    reportedFault: "",
    accessories: "",
    conditionNotes: "",
    promisedAt: "",
  });

  const [cover, setCover] = useState<WarrantyCover | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const customers =
    useApiQuery<PagedResult<Customer>>(["customers"], "api/v1/customers", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-slate-900/40 p-4" onClick={onClose}>
      <div
        className="w-full max-w-lg space-y-4 rounded-lg bg-white p-6 shadow-xl dark:bg-slate-950"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-lg font-semibold">Book in a device</h2>

        {error && (
          <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="grid grid-cols-2 gap-3">
          <label className="col-span-1 space-y-1 text-sm">
            <span className="text-slate-500">Customer</span>
            <select
              value={form.customerId}
              onChange={(e) => setForm({ ...form, customerId: e.target.value })}
              className={INPUT}
            >
              <option value="">Choose…</option>
              {customers.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </label>

          <label className="col-span-1 space-y-1 text-sm">
            <span className="text-slate-500">Branch</span>
            <select
              value={form.branchId}
              onChange={(e) => setForm({ ...form, branchId: e.target.value })}
              className={INPUT}
            >
              <option value="">Choose…</option>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>{b.name}</option>
              ))}
            </select>
          </label>

          <label className="col-span-2 space-y-1 text-sm">
            <span className="text-slate-500">Serial number</span>
            <input
              value={form.deviceSerialNumber}
              onChange={(e) => setForm({ ...form, deviceSerialNumber: e.target.value })}
              onBlur={async () => {
                const serial = form.deviceSerialNumber.trim();

                if (!serial) {
                  setCover(null);
                  return;
                }

                try {
                  setCover(await checkWarranty({ token }, serial));
                } catch {
                  setCover(null);
                }
              }}
              placeholder="As engraved on the machine"
              className={INPUT}
            />
          </label>

          {/* The answer, in words the clerk can repeat to the customer. A bare boolean starts an
              argument; "Shop warranty, sold on invoice INV-00042, expires 14 Aug 2027" ends one. */}
          {cover && (
            <p
              className={`col-span-2 rounded-md px-3 py-2 text-sm ${
                cover.isCovered
                  ? "bg-emerald-50 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
                  : "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300"
              }`}
            >
              {cover.explanation}
            </p>
          )}

          <label className="col-span-2 space-y-1 text-sm">
            <span className="text-slate-500">What the customer says is wrong</span>
            <textarea
              value={form.reportedFault}
              onChange={(e) => setForm({ ...form, reportedFault: e.target.value })}
              rows={2}
              className={INPUT}
            />
          </label>

          <label className="col-span-1 space-y-1 text-sm">
            <span className="text-slate-500">Accessories</span>
            <input
              value={form.accessories}
              onChange={(e) => setForm({ ...form, accessories: e.target.value })}
              placeholder="Charger, bag…"
              className={INPUT}
            />
          </label>

          <label className="col-span-1 space-y-1 text-sm">
            <span className="text-slate-500">Condition</span>
            <input
              value={form.conditionNotes}
              onChange={(e) => setForm({ ...form, conditionNotes: e.target.value })}
              placeholder="Scratched lid, cracked bezel…"
              className={INPUT}
            />
          </label>

          <label className="col-span-1 space-y-1 text-sm">
            <span className="text-slate-500">Promised by</span>
            <input
              type="date"
              value={form.promisedAt}
              onChange={(e) => setForm({ ...form, promisedAt: e.target.value })}
              className={INPUT}
            />
          </label>
        </div>

        <p className="text-xs text-slate-500">
          The accessories and the condition are written down now, before the shop touches it — which is what
          stops the argument at collection.
        </p>

        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="rounded-md px-3 py-1.5 text-sm text-slate-500 hover:text-slate-900 dark:hover:text-slate-100">
            Cancel
          </button>

          <button
            onClick={async () => {
              if (!form.customerId || !form.branchId || !form.reportedFault.trim()) {
                setError("A job needs a customer, a branch, and the fault the customer reported.");
                return;
              }

              setBusy(true);
              setError(null);

              try {
                await bookInDevice(
                  { token },
                  {
                    customerId: form.customerId,
                    branchId: form.branchId,
                    reportedFault: form.reportedFault.trim(),
                    deviceSerialNumber: form.deviceSerialNumber.trim() || null,
                    accessories: form.accessories.trim() || null,
                    conditionNotes: form.conditionNotes.trim() || null,
                    promisedAt: form.promisedAt ? new Date(form.promisedAt).toISOString() : null,
                  },
                );

                await onBooked();
              } catch (e) {
                setError(e instanceof ApiError ? e.message : "The device could not be booked in.");
              } finally {
                setBusy(false);
              }
            }}
            disabled={busy}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
          >
            Book in
          </button>
        </div>
      </div>
    </div>
  );
}
