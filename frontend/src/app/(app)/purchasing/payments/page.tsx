"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { Money } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { paySupplier } from "@/features/purchasing/api";
import {
  SupplierInvoiceStatus,
  type SupplierInvoice,
  type SupplierPayment,
} from "@/features/purchasing/types";
import type { PaymentMethod } from "@/features/sales/types";
import type { PagedResult } from "@/types/api";
import type { Supplier } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

/**
 * Money paid to suppliers — **a header plus allocations, not a column on an invoice.**
 *
 * One transfer settles three invoices; one invoice is settled by two instalments; a payment may settle
 * nothing at all and sit as an advance. A single `invoice_id` on a payment expresses none of those, and a
 * shop that pays its supplier monthly does all three.
 *
 * **This is also where a foreign-currency purchase settles up with reality.** The invoice fixed the debt
 * in base currency at the invoice-date rate; the money leaves the bank at the payment-date rate; the
 * difference is a realised gain or loss the business made by owing money in a currency it does not hold.
 * The table shows it, because it is real money and it belongs in the P&L — not in the cost of the stock.
 * The laptops did not become cheaper to buy; the currency moved.
 */
export default function SupplierPaymentsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [paying, setPaying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const suppliers =
    useApiQuery<PagedResult<Supplier>>(["suppliers"], "api/v1/suppliers", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const methods = useApiQuery<PaymentMethod[]>(["payment-methods"], "api/v1/payment-methods").data ?? [];
  const invoices =
    useApiQuery<PagedResult<SupplierInvoice>>(["supplier-invoices"], "api/v1/supplier-invoices", {
      pageSize: 200,
    }).data?.items ?? [];

  const columns: Column<SupplierPayment>[] = [
    {
      key: "number",
      header: "Payment",
      render: (p) => (
        <div>
          <p className="font-mono text-xs">{p.number}</p>
          <p className="text-xs text-slate-500">{new Date(p.paidAt).toLocaleDateString()}</p>
        </div>
      ),
    },
    { key: "supplier", header: "Supplier", render: (p) => p.supplierName },
    {
      key: "method",
      header: "How it left",
      render: (p) => (
        <div>
          <p className="text-xs">{p.paymentMethodName}</p>
          {p.reference && <p className="font-mono text-xs text-slate-500">{p.reference}</p>}
        </div>
      ),
    },
    {
      key: "settled",
      header: "Settled",
      render: (p) => (
        <div className="space-y-0.5">
          {p.allocations.map((allocation) => (
            <p key={allocation.id} className="font-mono text-xs">
              {allocation.supplierInvoiceNumber}{" "}
              <Money amount={allocation.amount} currency={p.currencyCode} />
            </p>
          ))}

          {/* Not an error: money paid before the invoice arrived is an advance, and it takes the
              supplier's balance negative — which is exactly what "they owe us" looks like. */}
          {p.unallocatedAmount > 0 && (
            <p className="text-xs text-amber-600 dark:text-amber-400">
              <Money amount={p.unallocatedAmount} currency={p.currencyCode} /> on account
            </p>
          )}
        </div>
      ),
    },
    {
      key: "fx",
      header: "FX gain / loss",
      align: "right",
      render: (p) =>
        p.exchangeGainOrLoss === 0 ? (
          <span className="text-slate-400">—</span>
        ) : (
          <div>
            <span
              className={
                p.exchangeGainOrLoss > 0
                  ? "tabular-nums text-emerald-700 dark:text-emerald-400"
                  : "tabular-nums text-red-700 dark:text-red-400"
              }
            >
              {p.exchangeGainOrLoss > 0 ? "+" : ""}
              <Money amount={p.exchangeGainOrLoss} />
            </span>
            <p className="text-xs text-slate-500">
              booked {p.allocations[0]?.invoiceExchangeRate ?? "—"}, paid {p.exchangeRate}
            </p>
          </div>
        ),
    },
    {
      key: "amount",
      header: "Paid",
      align: "right",
      render: (p) => (
        <div>
          <Money amount={p.amount} currency={p.currencyCode} />
          {p.currencyCode !== "AED" && (
            <p className="text-xs text-slate-500">
              <Money amount={p.amountBase} /> left the bank
            </p>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Supplier payments</h1>
          <p className="mt-1 text-sm text-slate-500">
            One transfer across three invoices; one invoice in two instalments. Paying a foreign invoice
            at a new rate realises a gain or a loss — and it is P&amp;L, not stock cost.
          </p>
        </div>

        <Can feature={FEATURES.supplierPayments} action={PermissionAction.Create}>
          <button
            onClick={() => setPaying(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Pay a supplier
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <Can
        feature={FEATURES.supplierPayments}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view supplier payments.</p>}
      >
        <DataTable
          queryKey={["supplier-payments"]}
          endpoint="api/v1/supplier-payments"
          columns={columns}
          rowKey={(p) => p.id}
          searchPlaceholder="Search payment, supplier or reference…"
          emptyMessage="Nothing paid yet."
        />
      </Can>

      {paying && (
        <PayDialog
          suppliers={suppliers}
          branches={branches}
          methods={methods}
          invoices={invoices}
          onClose={() => setPaying(false)}
          onSubmit={async (body) => {
            setError(null);

            try {
              await paySupplier({ token: accessToken! }, body);
              setPaying(false);

              await Promise.all([
                client.invalidateQueries({ queryKey: ["supplier-payments"] }),
                client.invalidateQueries({ queryKey: ["supplier-invoices"] }),
                client.invalidateQueries({ queryKey: ["suppliers"] }),
              ]);
            } catch (e) {
              setError(e instanceof ApiError ? e.message : "That did not work.");
            }
          }}
        />
      )}
    </div>
  );
}

/**
 * The paying form.
 *
 * Hand-rolled rather than an `<EntityForm>` because the whole point of a payment is that it allocates
 * across *several* invoices, and a flat field list cannot express that.
 */
function PayDialog({
  suppliers,
  branches,
  methods,
  invoices,
  onClose,
  onSubmit,
}: {
  suppliers: Supplier[];
  branches: Branch[];
  methods: PaymentMethod[];
  invoices: SupplierInvoice[];
  onClose: () => void;
  onSubmit: (body: Parameters<typeof paySupplier>[1]) => Promise<void>;
}) {
  const [supplierId, setSupplierId] = useState(suppliers[0]?.id ?? "");
  const [branchId, setBranchId] = useState(branches[0]?.id ?? "");
  const [paymentMethodId, setPaymentMethodId] = useState(methods[0]?.id ?? "");
  const [currencyCode, setCurrencyCode] = useState("AED");
  const [exchangeRate, setExchangeRate] = useState("1");
  const [amount, setAmount] = useState("0");
  const [reference, setReference] = useState("");
  const [allocations, setAllocations] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  /**
   * Only this supplier's unsettled invoices, and only those billed in the currency being paid.
   *
   * The currency filter is not cosmetic: an allocation's amount is in the *invoice's* currency and the
   * payment's amount is in the *payment's*. If they differed, the API's "you allocated more than you
   * paid" check — the one thing standing between the shop and a supplier balance that drifts quietly in
   * its favour — would be comparing dollars against dirhams. The API refuses it; offering it here would
   * only be showing a door that slams.
   */
  const payable = invoices.filter(
    (i) =>
      i.supplierId === supplierId &&
      i.currencyCode === currencyCode &&
      i.outstandingAmount > 0 &&
      (i.status === SupplierInvoiceStatus.Posted || i.status === SupplierInvoiceStatus.PartiallyPaid),
  );

  const allocated = Object.values(allocations).reduce((sum, value) => sum + (Number(value) || 0), 0);
  const paying = Number(amount) || 0;
  const unallocated = paying - allocated;

  const method = methods.find((m) => m.id === paymentMethodId);
  const needsReference = method?.requiresReference && !reference.trim();

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-900/40 p-6">
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          setBusy(true);

          try {
            await onSubmit({
              supplierId,
              branchId,
              paymentMethodId,
              amount: paying,
              currencyCode,
              exchangeRate: Number(exchangeRate) || 1,
              reference: reference || null,
              allocations: Object.entries(allocations)
                .filter(([, value]) => Number(value) > 0)
                .map(([supplierInvoiceId, value]) => ({
                  supplierInvoiceId,
                  amount: Number(value),
                })),
            });
          } finally {
            setBusy(false);
          }
        }}
        className="w-full max-w-2xl space-y-5 rounded-lg border border-slate-200 bg-white p-6 shadow-xl dark:border-slate-800 dark:bg-slate-950"
      >
        <div>
          <h2 className="text-lg font-semibold">Pay a supplier</h2>
          <p className="mt-1 text-sm text-slate-500">
            Point the money at the invoices it settles. Anything left over sits on the account as an
            advance.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block space-y-1">
            <span className="text-sm font-medium">Supplier</span>
            <select
              value={supplierId}
              onChange={(e) => {
                setSupplierId(e.target.value);
                setAllocations({});   // another supplier's invoices are not payable by this money
              }}
              className={INPUT}
            >
              {suppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>
                  {supplier.name}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Branch</span>
            <select value={branchId} onChange={(e) => setBranchId(e.target.value)} className={INPUT}>
              {branches.map((branch) => (
                <option key={branch.id} value={branch.id}>
                  {branch.name}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Method</span>
            <select
              value={paymentMethodId}
              onChange={(e) => setPaymentMethodId(e.target.value)}
              className={INPUT}
            >
              {methods.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.name}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">
              Reference{method?.requiresReference && <span className="text-red-500"> *</span>}
            </span>
            <input value={reference} onChange={(e) => setReference(e.target.value)} className={INPUT} />
            <span className="block text-xs text-slate-500">
              The cheque or transfer number. Without it the bank statement cannot be reconciled.
            </span>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Currency</span>
            <input
              value={currencyCode}
              onChange={(e) => {
                setCurrencyCode(e.target.value.toUpperCase());
                setAllocations({});   // the payable list is currency-scoped; stale picks would not apply
              }}
              maxLength={3}
              className={INPUT}
            />
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Rate today</span>
            <input
              type="number"
              step="0.0001"
              value={exchangeRate}
              onChange={(e) => setExchangeRate(e.target.value)}
              className={INPUT}
            />
            <span className="block text-xs text-slate-500">
              The rate the money actually leaves at — not the invoice&apos;s. The gap is the FX gain.
            </span>
          </label>

          <label className="block space-y-1 sm:col-span-2">
            <span className="text-sm font-medium">Amount paid</span>
            <input
              type="number"
              step="any"
              min="0"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              className={INPUT}
            />
          </label>
        </div>

        <div className="space-y-2">
          <p className="text-sm font-medium">Settles</p>

          {payable.length === 0 && (
            <p className="rounded-md border border-slate-200 px-3 py-2 text-xs text-slate-500 dark:border-slate-800">
              Nothing outstanding for this supplier in {currencyCode}. The money will sit on the account
              as an advance.
            </p>
          )}

          {payable.map((invoice) => (
            <div
              key={invoice.id}
              className="flex items-center gap-3 rounded-md border border-slate-200 px-3 py-2 dark:border-slate-800"
            >
              <div className="min-w-0 flex-1">
                <p className="font-mono text-xs">{invoice.number}</p>
                <p className="text-xs text-slate-500">
                  {invoice.supplierReference} · outstanding{" "}
                  <Money amount={invoice.outstandingAmount} currency={invoice.currencyCode} />
                </p>
              </div>

              <input
                type="number"
                step="any"
                min="0"
                max={invoice.outstandingAmount}
                value={allocations[invoice.id] ?? ""}
                onChange={(e) =>
                  setAllocations((current) => ({ ...current, [invoice.id]: e.target.value }))
                }
                placeholder="0.00"
                aria-label={`Allocate to ${invoice.number}`}
                className={`${INPUT} w-32 shrink-0 text-right`}
              />

              <button
                type="button"
                onClick={() =>
                  setAllocations((current) => ({
                    ...current,
                    [invoice.id]: String(invoice.outstandingAmount),
                  }))
                }
                className="shrink-0 rounded-md border border-slate-200 px-2 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
              >
                All
              </button>
            </div>
          ))}
        </div>

        <div className="space-y-1 border-t border-slate-200 pt-4 text-sm dark:border-slate-800">
          <div className="flex justify-between">
            <span className="text-slate-500">Allocated</span>
            <Money amount={allocated} currency={currencyCode} />
          </div>

          <div className="flex justify-between">
            <span className="text-slate-500">
              {unallocated < 0 ? "Over-allocated" : "Left on account"}
            </span>
            <span className={unallocated < 0 ? "text-red-600 dark:text-red-400" : ""}>
              <Money amount={unallocated} currency={currencyCode} />
            </span>
          </div>
        </div>

        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-slate-200 px-3 py-1.5 text-sm dark:border-slate-700"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy || paying <= 0 || unallocated < 0 || needsReference}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Paying…" : "Pay"}
          </button>
        </div>
      </form>
    </div>
  );
}

const INPUT =
  "w-full rounded-md border border-slate-200 bg-transparent px-3 py-1.5 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300";
