"use client";

import { useState } from "react";
import { Can } from "@/components/auth/can";
import { Money, StatusBadge } from "@/components/ui/money";
import { useApiQuery } from "@/lib/use-api";
import { FEATURES, PermissionAction } from "@/types/identity";
import { Reconciliation, SupplierStatementDrawer } from "@/features/reports/components/statement-drawer";
import type { PayablesAgeing } from "@/features/reports/types";

/**
 * What the shop owes, and to whom.
 *
 * <b>The buckets are in the company's own money, and the invoice list is not.</b> That is deliberate and it
 * is the whole difficulty of this screen. A supplier in Shenzhen will telephone about USD 1,000; the shop's
 * cash-flow question is about AED 3,670. Show only the first and nobody can add the column up. Show only
 * the second and the shop cannot recognise the invoice being chased. So the totals are base currency and
 * every foreign invoice carries its own figure and the rate beneath it.
 *
 * The rate is the one the invoice was booked at, not today's — see PayablesQueries. An open payable is
 * never revalued, so the number on this screen does not drift as the currency moves. It changes when the
 * debt is settled, and not before.
 */
export default function PayablesPage() {
  const [statementFor, setStatementFor] = useState<string | null>(null);

  const { data, isLoading, error } = useApiQuery<PayablesAgeing>(
    ["reports", "payables-ageing"],
    "api/v1/reports/payables-ageing",
  );

  const foreign = data?.invoices.filter((i) => i.exchangeRate !== 1) ?? [];

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Payables</h1>
        <p className="text-sm text-slate-500">
          What the shop owes its suppliers, aged — every total in the company&rsquo;s own money, at the rate
          each invoice was booked at.
        </p>
      </div>

      {error && (
        <p role="alert" className="text-sm text-red-600">
          {error.message}
        </p>
      )}

      <Can
        feature={FEATURES.payables}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view payables.</p>}
      >
        {isLoading && <p className="text-sm text-slate-500">Working out what is owed…</p>}

        {data && data.rows.length === 0 && (
          <p className="text-sm text-slate-500">The shop owes nobody anything.</p>
        )}

        {data && data.rows.length > 0 && (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500 dark:border-slate-800">
                  <tr>
                    <th className="py-2">Supplier</th>
                    <th className="py-2 text-right">Not due</th>
                    <th className="py-2 text-right">1–30</th>
                    <th className="py-2 text-right">31–60</th>
                    <th className="py-2 text-right">61–90</th>
                    <th className="py-2 text-right">90+</th>
                    <th className="py-2 text-right">Advances</th>
                    <th className="py-2 text-right">Owed</th>
                    <th className="py-2" />
                  </tr>
                </thead>

                <tbody>
                  {data.rows.map((row) => (
                    <tr key={row.supplierId} className="border-b border-slate-100 dark:border-slate-900">
                      <td className="py-2">
                        <div className="flex items-center gap-2">
                          <span>{row.supplierName}</span>
                          {row.days90Plus > 0 && <StatusBadge label="90+ days" tone="bad" />}
                          {row.variance !== null && row.variance !== 0 && (
                            <StatusBadge label="Does not reconcile" tone="warn" />
                          )}
                        </div>
                      </td>

                      <Bucket amount={row.current} currency={data.currencyCode} />
                      <Bucket amount={row.days1To30} currency={data.currencyCode} />
                      <Bucket amount={row.days31To60} currency={data.currencyCode} />
                      <Bucket amount={row.days61To90} currency={data.currencyCode} />
                      <Bucket amount={row.days90Plus} currency={data.currencyCode} danger />

                      <td className="py-2 text-right text-slate-500">
                        {row.advances !== 0 ? (
                          <Money amount={row.advances} currency={data.currencyCode} />
                        ) : null}
                      </td>

                      <td className="py-2 text-right font-semibold">
                        <Money amount={row.netPayable} currency={data.currencyCode} />
                      </td>

                      <td className="py-2 text-right">
                        <button
                          type="button"
                          className="text-xs text-slate-500 underline underline-offset-2 hover:text-slate-900 dark:hover:text-slate-100"
                          onClick={() => setStatementFor(row.supplierId)}
                        >
                          Statement
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>

                <tfoot className="font-semibold">
                  <tr>
                    <td className="py-3">Total</td>
                    <Bucket amount={data.totals.current} currency={data.currencyCode} />
                    <Bucket amount={data.totals.days1To30} currency={data.currencyCode} />
                    <Bucket amount={data.totals.days31To60} currency={data.currencyCode} />
                    <Bucket amount={data.totals.days61To90} currency={data.currencyCode} />
                    <Bucket amount={data.totals.days90Plus} currency={data.currencyCode} danger />
                    <td className="py-3 text-right">
                      <Money amount={data.totals.advances} currency={data.currencyCode} />
                    </td>
                    <td className="py-3 text-right">
                      <Money amount={data.totals.netPayable} currency={data.currencyCode} />
                    </td>
                    <td />
                  </tr>
                </tfoot>
              </table>
            </div>

            <Reconciliation variance={data.totals.variance} />

            {foreign.length > 0 && (
              <div>
                <h2 className="text-sm font-semibold">Owed in a foreign currency</h2>
                <p className="text-sm text-slate-500">
                  What the supplier will actually ask for, and what it costs the shop at the rate the
                  invoice was booked at.
                </p>

                <table className="mt-3 w-full text-sm">
                  <thead className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500 dark:border-slate-800">
                    <tr>
                      <th className="py-2">Invoice</th>
                      <th className="py-2">Supplier</th>
                      <th className="py-2 text-right">Due</th>
                      <th className="py-2 text-right">They will ask for</th>
                      <th className="py-2 text-right">It costs the shop</th>
                    </tr>
                  </thead>

                  <tbody>
                    {foreign.map((invoice) => (
                      <tr
                        key={invoice.invoiceId}
                        className="border-b border-slate-100 dark:border-slate-900"
                      >
                        <td className="py-2 font-mono">{invoice.number}</td>
                        <td className="py-2">{invoice.supplierName}</td>
                        <td className="py-2 text-right">
                          {invoice.daysOverdue > 0 ? (
                            <span className="text-red-700 dark:text-red-400">
                              {invoice.daysOverdue} days late
                            </span>
                          ) : (
                            <span className="text-slate-500">
                              in {-invoice.daysOverdue} days
                            </span>
                          )}
                        </td>
                        <td className="py-2 text-right">
                          <Money amount={invoice.outstandingAmount} currency={invoice.currencyCode} />
                        </td>
                        <td className="py-2 text-right">
                          <Money amount={invoice.outstandingBase} currency={data.currencyCode} />
                          <p className="text-xs text-slate-400">at {invoice.exchangeRate}</p>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </Can>

      {statementFor && (
        <SupplierStatementDrawer supplierId={statementFor} onClose={() => setStatementFor(null)} />
      )}
    </div>
  );
}

function Bucket({
  amount,
  currency,
  danger = false,
}: {
  amount: number;
  currency: string;
  danger?: boolean;
}) {
  return (
    <td
      className={`py-2 text-right ${
        danger && amount > 0 ? "font-medium text-red-700 dark:text-red-400" : ""
      }`}
    >
      {amount !== 0 ? <Money amount={amount} currency={currency} /> : <span className="text-slate-300">—</span>}
    </td>
  );
}
