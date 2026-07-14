"use client";

import { useApiQuery } from "@/lib/use-api";
import { Money } from "@/components/ui/money";
import { SOURCE_LABELS, type AccountStatement } from "@/features/finance/types";

/**
 * One account's history: what it held, what moved it, what it holds.
 *
 * <b>The opening balance is the whole design</b>, exactly as it is on the customer and supplier statements
 * of slice 1: a list of movements with no starting point cannot be reconciled against anything. This one
 * opens with what the account held the moment before the window, walks every movement in and out, and
 * closes on what it holds now.
 *
 * There is no reconciliation footer here, and its absence is the point. A customer's balance is a stored
 * decimal that a report has to prove; an account has no stored balance at all, so the closing figure below
 * <em>is</em> the sum of the rows above it rather than a claim about them. There is nothing for it to
 * disagree with.
 *
 * In and out rather than the signed amount the row is stored as, because that is how a finance person reads
 * a statement — and the API splits it at the edge for the same reason.
 */
export function AccountStatementDrawer({
  accountId,
  onClose,
}: {
  accountId: string;
  onClose: () => void;
}) {
  const { data } = useApiQuery<AccountStatement>(
    ["finance", "account-statement", accountId],
    `api/v1/finance/accounts/${accountId}/statement`,
  );

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-slate-900/40" onClick={onClose}>
      <div
        className="h-full w-full max-w-3xl overflow-y-auto bg-white p-6 shadow-xl dark:bg-slate-950"
        onClick={(e) => e.stopPropagation()}
      >
        {!data ? (
          <p className="text-sm text-slate-500">Loading the statement…</p>
        ) : (
          <>
            <div>
              <h2 className="text-lg font-semibold">{data.accountName}</h2>
              <p className="text-sm text-slate-500">
                Statement of account — every line in {data.currencyCode}, the money this account holds
              </p>
              <p className="mt-1 text-sm text-slate-500">
                {data.from ? `${date(data.from)} to ${date(data.to)}` : `Everything, to ${date(data.to)}`}
              </p>
            </div>

            <table className="mt-6 w-full text-sm">
              <thead className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500 dark:border-slate-800">
                <tr>
                  <th className="py-2">Date</th>
                  <th className="py-2">Movement</th>
                  <th className="py-2 text-right">In</th>
                  <th className="py-2 text-right">Out</th>
                  <th className="py-2 text-right">Balance</th>
                </tr>
              </thead>

              <tbody>
                <tr className="border-b border-slate-100 text-slate-500 dark:border-slate-900">
                  <td className="py-2">{data.from ? date(data.from) : "—"}</td>
                  <td className="py-2 italic">Opening balance</td>
                  <td />
                  <td />
                  <td className="py-2 text-right">
                    <Money amount={data.openingBalance} currency={data.currencyCode} />
                  </td>
                </tr>

                {data.rows.map((row, index) => (
                  <tr
                    // Two movements can share a source, a number and a timestamp — a transfer between two
                    // accounts of the same shop, most obviously — so the position in the ordered list is
                    // the only key that is actually unique.
                    key={`${row.occurredAt}-${index}`}
                    className="border-b border-slate-100 dark:border-slate-900"
                  >
                    <td className="py-2">{date(row.occurredAt)}</td>
                    <td className="py-2">
                      <p>{row.description}</p>
                      <p className="text-xs text-slate-500">
                        {SOURCE_LABELS[row.source]}
                        {row.sourceNumber && <span className="ml-2 font-mono">{row.sourceNumber}</span>}
                        {row.reference && <span className="ml-2 text-slate-400">{row.reference}</span>}
                      </p>
                    </td>
                    <td className="py-2 text-right text-emerald-700 dark:text-emerald-400">
                      {row.in !== 0 ? <Money amount={row.in} currency={data.currencyCode} /> : null}
                    </td>
                    <td className="py-2 text-right text-red-700 dark:text-red-400">
                      {row.out !== 0 ? <Money amount={row.out} currency={data.currencyCode} /> : null}
                    </td>
                    <td className="py-2 text-right">
                      <Money amount={row.runningBalance} currency={data.currencyCode} />
                    </td>
                  </tr>
                ))}

                {data.rows.length === 0 && (
                  <tr>
                    <td colSpan={5} className="py-6 text-center text-sm text-slate-500">
                      Nothing moved through this account in the period.
                    </td>
                  </tr>
                )}
              </tbody>

              <tfoot>
                <tr className="font-semibold">
                  <td className="py-3" colSpan={4}>
                    Balance held
                  </td>
                  <td className="py-3 text-right">
                    <Money amount={data.closingBalance} currency={data.currencyCode} />
                  </td>
                </tr>
              </tfoot>
            </table>
          </>
        )}
      </div>
    </div>
  );
}

function date(value: string) {
  return new Date(value).toLocaleDateString();
}
