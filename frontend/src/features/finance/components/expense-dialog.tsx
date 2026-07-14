"use client";

import { useState } from "react";
import { Money } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { useApiQuery } from "@/lib/use-api";
import { recordExpense } from "@/features/finance/api";
import type { ExpenseCategory, FinancialAccount } from "@/features/finance/types";
import type { PagedResult } from "@/types/api";
import type { Supplier } from "@/types/catalog";
import type { Branch } from "@/types/identity";

const INPUT =
  "w-full rounded-md border border-slate-200 bg-transparent px-3 py-1.5 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300";

/**
 * The rent, the courier, the clearing agent's fee (§34).
 *
 * <b>Recording it pays it.</b> There is no draft: the money leaves the named account the moment this is
 * submitted, which is why the account is not an afterthought at the bottom of the form but the thing that
 * decides what currency the amount is even in. An expense that named no account would be money that
 * vanished.
 *
 * Hand-rolled rather than an `<EntityForm>` for that reason — the currency of the amount box follows the
 * account picker, and a flat field list cannot say so.
 */
export function ExpenseDialog({
  accounts,
  categories,
  token,
  onClose,
  onRecorded,
}: {
  accounts: FinancialAccount[];
  categories: ExpenseCategory[];
  token: string;
  onClose: () => void;
  onRecorded: () => Promise<void>;
}) {
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const suppliers =
    useApiQuery<PagedResult<Supplier>>(["suppliers"], "api/v1/suppliers", { pageSize: 200 }).data?.items ?? [];

  const [expenseCategoryId, setExpenseCategoryId] = useState("");
  const [branchId, setBranchId] = useState("");
  const [financialAccountId, setFinancialAccountId] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [amount, setAmount] = useState("");
  const [description, setDescription] = useState("");
  const [expenseDate, setExpenseDate] = useState("");
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const account = accounts.find((a) => a.id === financialAccountId);
  const spending = Number(amount) || 0;

  // A retired category still has its old expenses hanging off it, so it stays in the list on screen — but
  // it cannot take a new one, and the server says so.
  const open = categories.filter((category) => category.isActive);

  const overdrawing = Boolean(account && !account.allowsOverdraft && spending > account.balance);

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-900/40 p-6">
      <form
        onSubmit={async (event) => {
          event.preventDefault();

          setBusy(true);
          setError(null);

          try {
            await recordExpense(
              { token },
              {
                expenseCategoryId,
                branchId,
                financialAccountId,
                amount: spending,
                description: description.trim(),
                supplierId: supplierId || null,
                expenseDate: expenseDate ? new Date(expenseDate).toISOString() : null,
                reference: reference.trim() || null,
                notes: notes.trim() || null,
              },
            );

            await onRecorded();
          } catch (e) {
            setError(e instanceof ApiError ? e.message : "The expense was not recorded.");
          } finally {
            setBusy(false);
          }
        }}
        className="w-full max-w-2xl space-y-5 rounded-lg border border-slate-200 bg-white p-6 shadow-xl dark:border-slate-800 dark:bg-slate-950"
      >
        <div>
          <h2 className="text-lg font-semibold">Record an expense</h2>
          <p className="mt-1 text-sm text-slate-500">
            Money that bought no stock. Recording it takes it out of the account — there is nothing left to
            pay afterwards.
          </p>
        </div>

        {error && (
          <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block space-y-1">
            <span className="text-sm font-medium">Category</span>
            <select
              value={expenseCategoryId}
              onChange={(e) => setExpenseCategoryId(e.target.value)}
              className={INPUT}
            >
              <option value="">Choose…</option>
              {open.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Branch</span>
            <select value={branchId} onChange={(e) => setBranchId(e.target.value)} className={INPUT}>
              <option value="">Choose…</option>
              {branches.map((branch) => (
                <option key={branch.id} value={branch.id}>
                  {branch.name}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Paid out of</span>
            <select
              value={financialAccountId}
              onChange={(e) => setFinancialAccountId(e.target.value)}
              className={INPUT}
            >
              <option value="">Choose…</option>
              {accounts.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name} ({a.currencyCode})
                </option>
              ))}
            </select>
            {account && (
              <span className="block text-xs text-slate-500">
                Holds <Money amount={account.balance} currency={account.currencyCode} />
              </span>
            )}
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">
              Amount{account && <span className="text-slate-500"> ({account.currencyCode})</span>}
            </span>
            <input
              type="number"
              step="any"
              min="0"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              className={INPUT}
            />
            <span className="block text-xs text-slate-500">
              In the account&apos;s own money. You cannot spend dollars out of a dirham account — the bank
              converts, and what left the account is dirhams.
            </span>
          </label>

          <label className="block space-y-1 sm:col-span-2">
            <span className="text-sm font-medium">What the money was spent on</span>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="September rent — Deira shop"
              className={INPUT}
            />
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Paid to</span>
            <select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} className={INPUT}>
              <option value="">Nobody the shop knows</option>
              {suppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>
                  {supplier.name}
                </option>
              ))}
            </select>
            {/* Naming them does not put anything on their balance: this is money already gone, not a bill
                outstanding. A bill from a supplier is a supplier invoice, and it ages. */}
            <span className="block text-xs text-slate-500">
              A parking fee has no supplier. Naming one here does not put it on their account.
            </span>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Date</span>
            <input
              type="date"
              value={expenseDate}
              onChange={(e) => setExpenseDate(e.target.value)}
              className={INPUT}
            />
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Reference</span>
            <input value={reference} onChange={(e) => setReference(e.target.value)} className={INPUT} />
            <span className="block text-xs text-slate-500">
              The receipt number. What the bank statement is matched on.
            </span>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Notes</span>
            <input value={notes} onChange={(e) => setNotes(e.target.value)} className={INPUT} />
          </label>
        </div>

        {overdrawing && account && (
          <p className="rounded-md bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:bg-amber-950 dark:text-amber-300">
            {account.name} only holds <Money amount={account.balance} currency={account.currencyCode} />. Pay
            it out of somewhere the money actually is.
          </p>
        )}

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
            disabled={
              busy
              || !expenseCategoryId
              || !branchId
              || !financialAccountId
              || spending <= 0
              || !description.trim()
              || overdrawing
            }
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Paying…" : "Record and pay"}
          </button>
        </div>
      </form>
    </div>
  );
}
