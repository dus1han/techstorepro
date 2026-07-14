"use client";

import { useState } from "react";
import { Money } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { transferBetweenAccounts } from "@/features/finance/api";
import type { FinancialAccount } from "@/features/finance/types";

const INPUT =
  "w-full rounded-md border border-slate-200 bg-transparent px-3 py-1.5 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300";

/**
 * Bank the till; take a float out to the second shop (§33).
 *
 * Hand-rolled rather than an `<EntityForm>` because of the second amount box. Across a currency boundary
 * the shop types **what the bank actually credited** — USD 1,000 leaves and AED 3,670 arrives — and a flat
 * field list cannot show it appearing and disappearing as the two accounts are chosen. A converted figure
 * would be right to six decimal places and wrong by whatever the bank charged, and the bank statement, not
 * the FX table, is what this account has to reconcile against.
 */
export function TransferDialog({
  accounts,
  token,
  onClose,
  onTransferred,
}: {
  accounts: FinancialAccount[];
  token: string;
  onClose: () => void;
  onTransferred: () => Promise<void>;
}) {
  const [fromAccountId, setFromAccountId] = useState("");
  const [toAccountId, setToAccountId] = useState("");
  const [amountOut, setAmountOut] = useState("");
  const [amountIn, setAmountIn] = useState("");
  const [description, setDescription] = useState("");
  const [reference, setReference] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const from = accounts.find((a) => a.id === fromAccountId);
  const to = accounts.find((a) => a.id === toAccountId);

  const crossCurrency = Boolean(from && to && from.currencyCode !== to.currencyCode);
  const leaving = Number(amountOut) || 0;

  // Only a bank may lend the shop its own money. A drawer cannot pay out notes it does not hold, and the
  // server refuses it — so the button is not offered either.
  const overdrawing = Boolean(from && !from.allowsOverdraft && leaving > from.balance);

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-900/40 p-6">
      <form
        onSubmit={async (event) => {
          event.preventDefault();

          setBusy(true);
          setError(null);

          try {
            await transferBetweenAccounts(
              { token },
              {
                fromAccountId,
                toAccountId,
                amountOut: leaving,
                amountIn: crossCurrency ? Number(amountIn) || 0 : null,
                description: description.trim() || null,
                reference: reference.trim() || null,
              },
            );

            await onTransferred();
          } catch (e) {
            setError(e instanceof ApiError ? e.message : "The money did not move.");
          } finally {
            setBusy(false);
          }
        }}
        className="w-full max-w-lg space-y-5 rounded-lg border border-slate-200 bg-white p-6 shadow-xl dark:border-slate-800 dark:bg-slate-950"
      >
        <div>
          <h2 className="text-lg font-semibold">Move money</h2>
          <p className="mt-1 text-sm text-slate-500">
            Two movements, never one: it leaves one account and arrives in the other, and both statements
            say so.
          </p>
        </div>

        {error && (
          <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block space-y-1">
            <span className="text-sm font-medium">Out of</span>
            <select
              value={fromAccountId}
              onChange={(e) => setFromAccountId(e.target.value)}
              className={INPUT}
            >
              <option value="">Choose…</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name} ({account.currencyCode})
                </option>
              ))}
            </select>
            {from && (
              <span className="block text-xs text-slate-500">
                Holds <Money amount={from.balance} currency={from.currencyCode} />
              </span>
            )}
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Into</span>
            <select value={toAccountId} onChange={(e) => setToAccountId(e.target.value)} className={INPUT}>
              <option value="">Choose…</option>
              {accounts
                .filter((account) => account.id !== fromAccountId)
                .map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.name} ({account.currencyCode})
                  </option>
                ))}
            </select>
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">
              Amount leaving{from && <span className="text-slate-500"> ({from.currencyCode})</span>}
            </span>
            <input
              type="number"
              step="any"
              min="0"
              value={amountOut}
              onChange={(e) => setAmountOut(e.target.value)}
              className={INPUT}
            />
          </label>

          {crossCurrency && (
            <label className="block space-y-1">
              <span className="text-sm font-medium">
                Amount arriving <span className="text-slate-500">({to!.currencyCode})</span>
              </span>
              <input
                type="number"
                step="any"
                min="0"
                value={amountIn}
                onChange={(e) => setAmountIn(e.target.value)}
                className={INPUT}
              />
              <span className="block text-xs text-slate-500">
                What the bank actually credited, not a converted figure. The difference between the two is
                what the conversion cost.
              </span>
            </label>
          )}

          <label className="block space-y-1 sm:col-span-2">
            <span className="text-sm font-medium">What it was</span>
            <input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Banking Saturday's takings"
              className={INPUT}
            />
          </label>

          <label className="block space-y-1 sm:col-span-2">
            <span className="text-sm font-medium">Reference</span>
            <input value={reference} onChange={(e) => setReference(e.target.value)} className={INPUT} />
            <span className="block text-xs text-slate-500">
              The deposit slip or transfer number. What the bank statement is matched on.
            </span>
          </label>
        </div>

        {overdrawing && from && (
          <p className="rounded-md bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:bg-amber-950 dark:text-amber-300">
            {from.name} only holds <Money amount={from.balance} currency={from.currencyCode} />. There are
            no negative notes in a drawer.
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
              || !fromAccountId
              || !toAccountId
              || leaving <= 0
              || overdrawing
              || (crossCurrency && Number(amountIn) <= 0)
            }
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Moving…" : "Move it"}
          </button>
        </div>
      </form>
    </div>
  );
}
