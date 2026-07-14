"use client";

import { useState } from "react";
import { Can } from "@/components/auth/can";
import { Money, StatusBadge } from "@/components/ui/money";
import { useApiQuery } from "@/lib/use-api";
import { FEATURES } from "@/types/identity";
import { PermissionAction } from "@/types/identity";
import { Reconciliation, CustomerStatementDrawer } from "@/features/reports/components/statement-drawer";
import type { ReceivablesAgeing } from "@/features/reports/types";

/**
 * Who owes the shop money, and how late they are.
 *
 * <b>The oldest column is the one the screen is built around.</b> Debt does not get more collectable with
 * age, and the rows are sorted by what is owed rather than by name for the same reason: this is a screen
 * somebody opens to decide who to telephone this morning, not to look a customer up.
 *
 * The "credits" column is the one people ask about. It is money the shop is holding that no invoice has
 * claimed — a payment taken on account before the invoice existed, or an invoice credited past zero. It
 * comes off the debt, and it is shown separately rather than netted into the buckets because a customer
 * who owes 10,000 on a ninety-day invoice and is sitting on a 9,000 advance is a completely different
 * conversation from one who owes 1,000.
 */
export default function ReceivablesPage() {
  const [statementFor, setStatementFor] = useState<string | null>(null);

  const { data, isLoading, error } = useApiQuery<ReceivablesAgeing>(
    ["reports", "receivables-ageing"],
    "api/v1/reports/receivables-ageing",
  );

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Receivables</h1>
        <p className="text-sm text-slate-500">
          What every customer owes, aged by how far past due it is — and proved against the balance on
          their account.
        </p>
      </div>

      {error && (
        <p role="alert" className="text-sm text-red-600">
          {error.message}
        </p>
      )}

      <Can
        feature={FEATURES.receivables}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view receivables.</p>}
      >
        {isLoading && <p className="text-sm text-slate-500">Working out who owes what…</p>}

        {data && data.rows.length === 0 && (
          <p className="text-sm text-slate-500">Nobody owes the shop anything. Enjoy it.</p>
        )}

        {data && data.rows.length > 0 && (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500 dark:border-slate-800">
                  <tr>
                    <th className="py-2">Customer</th>
                    <th className="py-2 text-right">Not due</th>
                    <th className="py-2 text-right">1–30</th>
                    <th className="py-2 text-right">31–60</th>
                    <th className="py-2 text-right">61–90</th>
                    <th className="py-2 text-right">90+</th>
                    <th className="py-2 text-right">Credits</th>
                    <th className="py-2 text-right">Owed</th>
                    <th className="py-2" />
                  </tr>
                </thead>

                <tbody>
                  {data.rows.map((row) => (
                    <tr key={row.customerId} className="border-b border-slate-100 dark:border-slate-900">
                      <td className="py-2">
                        <div className="flex items-center gap-2">
                          <span>{row.customerName}</span>
                          {row.days90Plus > 0 && <StatusBadge label="90+ days" tone="bad" />}
                          {row.variance !== null && row.variance !== 0 && (
                            <StatusBadge label="Does not reconcile" tone="warn" />
                          )}
                        </div>

                        {row.storeCredit !== 0 && (
                          <p className="text-xs text-slate-400">
                            Holds <Money amount={row.storeCredit} currency={data.currencyCode} /> in store
                            credit
                          </p>
                        )}
                      </td>

                      <Bucket amount={row.current} currency={data.currencyCode} />
                      <Bucket amount={row.days1To30} currency={data.currencyCode} />
                      <Bucket amount={row.days31To60} currency={data.currencyCode} />
                      <Bucket amount={row.days61To90} currency={data.currencyCode} />
                      <Bucket amount={row.days90Plus} currency={data.currencyCode} danger />

                      <td className="py-2 text-right text-slate-500">
                        {row.credits !== 0 ? (
                          <Money amount={row.credits} currency={data.currencyCode} />
                        ) : null}
                      </td>

                      <td className="py-2 text-right font-semibold">
                        <Money amount={row.netReceivable} currency={data.currencyCode} />
                      </td>

                      <td className="py-2 text-right">
                        <button
                          type="button"
                          className="text-xs text-slate-500 underline underline-offset-2 hover:text-slate-900 dark:hover:text-slate-100"
                          onClick={() => setStatementFor(row.customerId)}
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
                      <Money amount={data.totals.credits} currency={data.currencyCode} />
                    </td>
                    <td className="py-3 text-right">
                      <Money amount={data.totals.netReceivable} currency={data.currencyCode} />
                    </td>
                    <td />
                  </tr>
                </tfoot>
              </table>
            </div>

            <Reconciliation variance={data.totals.variance} />
          </>
        )}
      </Can>

      {statementFor && (
        <CustomerStatementDrawer customerId={statementFor} onClose={() => setStatementFor(null)} />
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
