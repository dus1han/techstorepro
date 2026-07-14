"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { Money, StatusBadge } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { cancelExpense } from "@/features/finance/api";
import { ExpenseDialog } from "@/features/finance/components/expense-dialog";
import {
  ExpenseStatus,
  type Expense,
  type ExpenseCategory,
  type ExpenseSummary,
  type FinancialAccount,
} from "@/features/finance/types";
import { FEATURES, PermissionAction } from "@/types/identity";

const INPUT =
  "rounded-md border border-slate-200 bg-transparent px-2.5 py-1.5 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300";

/**
 * What the shop spends (requirements §34) — the rent, the courier, the clearing agent.
 *
 * <b>Cancelled expenses are struck through rather than hidden.</b> That is the entire reason cancelling
 * exists instead of deleting: the money left the account and came back, both movements are on the
 * statement, and an expense that quietly disappeared from this list would leave a reader staring at two
 * ledger rows they could no longer explain. The mistake stays visible next to its reversal — which is what
 * an auditor is looking for, and what the person who made it will need when they are asked about it.
 *
 * The by-category summary excludes them, and that is not an inconsistency: the list is a record of what was
 * done, the summary is a record of what it cost. A cancelled expense cost nothing.
 */
export default function ExpensesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();
  const token = accessToken!;

  const [recording, setRecording] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [accountId, setAccountId] = useState("");

  const period = {
    from: from ? new Date(from).toISOString() : undefined,
    to: to ? new Date(to).toISOString() : undefined,
  };

  const accounts =
    useApiQuery<FinancialAccount[]>(["finance", "accounts"], "api/v1/finance/accounts").data ?? [];
  const categories =
    useApiQuery<ExpenseCategory[]>(
      ["finance", "expense-categories"],
      "api/v1/finance/expense-categories",
    ).data ?? [];

  const { data, error: loadError } = useApiQuery<Expense[]>(
    ["finance", "expenses"],
    "api/v1/finance/expenses",
    {
      ...period,
      expenseCategoryId: categoryId || undefined,
      financialAccountId: accountId || undefined,
    },
  );

  const summary = useApiQuery<ExpenseSummary>(
    ["finance", "expense-summary"],
    "api/v1/finance/expenses/summary",
    period,
  ).data;

  const expenses = data ?? [];

  const reload = () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["finance", "expenses"] }),
      client.invalidateQueries({ queryKey: ["finance", "expense-summary"] }),

      // An expense is money leaving an account, so the cash position and every statement went stale with it.
      client.invalidateQueries({ queryKey: ["finance", "accounts"] }),
      client.invalidateQueries({ queryKey: ["finance", "cash-position"] }),
      client.invalidateQueries({ queryKey: ["finance", "account-statement"] }),
    ]);

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Expenses</h1>
          <p className="mt-1 text-sm text-slate-500">
            Money that bought no stock. Recording one pays it — and a mistake is cancelled, never deleted,
            so it stays on the page next to the money coming back.
          </p>
        </div>

        <Can feature={FEATURES.expenses} action={PermissionAction.Create}>
          <button
            onClick={() => setRecording(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Record an expense
          </button>
        </Can>
      </div>

      {(error ?? loadError) && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error ?? loadError?.message}
        </p>
      )}

      <Can
        feature={FEATURES.expenses}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view expenses.</p>}
      >
        <div className="flex flex-wrap items-end gap-3">
          <label className="space-y-1">
            <span className="block text-xs text-slate-500">From</span>
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={INPUT} />
          </label>

          <label className="space-y-1">
            <span className="block text-xs text-slate-500">To</span>
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={INPUT} />
          </label>

          <label className="space-y-1">
            <span className="block text-xs text-slate-500">Category</span>
            <select
              value={categoryId}
              onChange={(e) => setCategoryId(e.target.value)}
              className={INPUT}
            >
              <option value="">All categories</option>
              {categories.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </label>

          <label className="space-y-1">
            <span className="block text-xs text-slate-500">Account</span>
            <select value={accountId} onChange={(e) => setAccountId(e.target.value)} className={INPUT}>
              <option value="">All accounts</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name}
                </option>
              ))}
            </select>
          </label>
        </div>

        {summary && summary.categories.length > 0 && (
          <section className="rounded-lg border border-slate-200 p-4 dark:border-slate-800">
            <div className="flex items-baseline justify-between">
              <div>
                <h2 className="text-sm font-medium">Where it went</h2>
                <p className="text-xs text-slate-500">
                  {new Date(summary.from).toLocaleDateString()} to{" "}
                  {new Date(summary.to).toLocaleDateString()} — cancelled expenses excluded, because the
                  money came back.
                </p>
              </div>

              <p className="text-lg font-semibold">
                <Money amount={summary.totalBase} />
              </p>
            </div>

            <div className="mt-3 space-y-1.5">
              {summary.categories.map((line) => (
                <div key={line.categoryId} className="flex items-center gap-3 text-sm">
                  <span className="w-40 shrink-0 truncate">{line.categoryName}</span>

                  <div className="h-1.5 flex-1 rounded-full bg-slate-100 dark:bg-slate-800">
                    <div
                      className="h-1.5 rounded-full bg-slate-900 dark:bg-slate-100"
                      style={{
                        width: `${summary.totalBase > 0 ? (line.totalBase / summary.totalBase) * 100 : 0}%`,
                      }}
                    />
                  </div>

                  <span className="w-16 shrink-0 text-right text-xs text-slate-500">
                    {line.count} item{line.count === 1 ? "" : "s"}
                  </span>

                  <span className="w-32 shrink-0 text-right">
                    <Money amount={line.totalBase} />
                  </span>
                </div>
              ))}
            </div>
          </section>
        )}

        <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-800">
          <table className="w-full text-sm">
            <thead className="border-b border-slate-200 bg-slate-50 text-left dark:border-slate-800 dark:bg-slate-900">
              <tr>
                <th className="px-4 py-2.5 font-medium">Expense</th>
                <th className="px-4 py-2.5 font-medium">What it was</th>
                <th className="px-4 py-2.5 font-medium">Category</th>
                <th className="px-4 py-2.5 font-medium">Paid out of</th>
                <th className="px-4 py-2.5 text-right font-medium">Amount</th>
                <th className="px-4 py-2.5" />
              </tr>
            </thead>

            <tbody>
              {expenses.map((expense) => {
                const cancelled = expense.status === ExpenseStatus.Cancelled;

                return (
                  <tr
                    key={expense.id}
                    className={`border-b border-slate-100 last:border-0 dark:border-slate-800 ${
                      cancelled ? "text-slate-400 line-through dark:text-slate-500" : ""
                    }`}
                  >
                    <td className="px-4 py-2.5">
                      <p className="font-mono text-xs">{expense.number}</p>
                      <p className="text-xs text-slate-500">
                        {new Date(expense.expenseDate).toLocaleDateString()}
                      </p>
                    </td>

                    <td className="px-4 py-2.5">
                      <p>{expense.description}</p>
                      <p className="text-xs text-slate-500">
                        {[expense.supplierName, expense.reference, expense.branchName]
                          .filter(Boolean)
                          .join(" · ")}
                      </p>

                      {/* Not struck through: the reason is the one part of a cancelled expense that is
                          still true, and it is the only thing here that explains the reversal sitting on
                          the account's statement. */}
                      {cancelled && expense.cancelledReason && (
                        <p className="mt-1 text-xs text-amber-700 no-underline dark:text-amber-400">
                          Cancelled — {expense.cancelledReason}
                        </p>
                      )}
                    </td>

                    <td className="px-4 py-2.5">{expense.categoryName}</td>
                    <td className="px-4 py-2.5">{expense.accountName}</td>

                    <td className="px-4 py-2.5 text-right tabular-nums">
                      <Money amount={expense.amount} currency={expense.currencyCode} />

                      {expense.amount !== expense.amountBase && (
                        <p className="text-xs text-slate-500">
                          <Money amount={expense.amountBase} /> in the company&apos;s money
                        </p>
                      )}
                    </td>

                    <td className="px-4 py-2.5 text-right">
                      {cancelled ? (
                        <StatusBadge label="Cancelled" tone="bad" />
                      ) : (
                        <Can feature={FEATURES.expenses} action={PermissionAction.Delete}>
                          <button
                            disabled={busy === expense.id}
                            onClick={() => {
                              const reason = window.prompt(
                                `Why is ${expense.number} being cancelled? The money goes back into ${expense.accountName}.`,
                              );

                              if (!reason) return;

                              void cancel(expense.id, reason);
                            }}
                            className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                          >
                            Cancel
                          </button>
                        </Can>
                      )}
                    </td>
                  </tr>
                );
              })}

              {expenses.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-slate-500">
                    Nothing spent in this period.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Can>

      {recording && (
        <ExpenseDialog
          accounts={accounts}
          categories={categories}
          token={token}
          onClose={() => setRecording(false)}
          onRecorded={async () => {
            setRecording(false);
            await reload();
          }}
        />
      )}
    </div>
  );

  async function cancel(id: string, reason: string) {
    setBusy(id);
    setError(null);

    try {
      await cancelExpense({ token }, id, reason);
      await reload();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "The expense was not cancelled.");
    } finally {
      setBusy(null);
    }
  }
}
