"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { EntityForm } from "@/components/data/entity-form";
import { Money, StatusBadge } from "@/components/ui/money";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { openAccount, updateAccount } from "@/features/finance/api";
import { AccountStatementDrawer } from "@/features/finance/components/account-statement-drawer";
import { TransferDialog } from "@/features/finance/components/transfer-dialog";
import {
  FinancialAccountKind,
  type CashPosition,
  type FinancialAccount,
} from "@/features/finance/types";
import type { PagedResult } from "@/types/api";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

/**
 * What the shop actually holds, right now (requirements §33).
 *
 * <b>The total at the top is the number this screen exists for.</b> An owner opens it to answer one
 * question — how much money is there — and everything below is that number taken apart into figures
 * somebody can physically point at: this till, that bank account. It totals in the base currency because a
 * dirham till and a dollar account cannot be added up any other way.
 *
 * No balance on this screen is stored anywhere. Every one of them is the sum of the movements behind it,
 * which is why "why does the till say 4,300?" is answered by opening the statement rather than by an
 * argument.
 */
export default function AccountsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();
  const token = accessToken!;

  const [dialog, setDialog] = useState<"open" | "transfer" | null>(null);
  const [editing, setEditing] = useState<FinancialAccount | null>(null);
  const [statementFor, setStatementFor] = useState<string | null>(null);

  const position = useApiQuery<CashPosition>(["finance", "cash-position"], "api/v1/reports/cash-position").data;

  const { data, error } = useApiQuery<FinancialAccount[]>(
    ["finance", "accounts"],
    "api/v1/finance/accounts",
    { includeInactive: true },
  );

  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];

  const accounts = data ?? [];

  // Only an open account can take money, and only an account holding money can give it away.
  const transferable = accounts.filter((account) => account.isActive);

  const reload = async () => {
    setDialog(null);
    setEditing(null);

    await Promise.all([
      client.invalidateQueries({ queryKey: ["finance", "accounts"] }),
      client.invalidateQueries({ queryKey: ["finance", "cash-position"] }),
      client.invalidateQueries({ queryKey: ["finance", "account-statement"] }),
    ]);
  };

  return (
    <div className="mx-auto max-w-5xl space-y-8">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Cash &amp; bank</h1>
          <p className="mt-1 text-sm text-slate-500">
            Every figure here is the sum of the movements behind it. There is no balance to adjust, and
            nothing to reconcile it against — the rows <em>are</em> the balance.
          </p>
        </div>

        <div className="flex gap-2">
          <Can feature={FEATURES.accounts} action={PermissionAction.Approve}>
            <button
              onClick={() => setDialog("transfer")}
              className="rounded-md border border-slate-200 px-3 py-1.5 text-sm hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
            >
              Move money
            </button>
          </Can>

          <Can feature={FEATURES.accounts} action={PermissionAction.Create}>
            <button
              onClick={() => setDialog("open")}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
            >
              Open an account
            </button>
          </Can>
        </div>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error.message}
        </p>
      )}

      <Can
        feature={FEATURES.accounts}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view accounts.</p>}
      >
        {position && (
          <section className="rounded-lg border border-slate-200 p-6 dark:border-slate-800">
            <p className="text-xs font-medium uppercase tracking-wider text-slate-400">Cash position</p>

            <p className="mt-1 text-4xl font-semibold tracking-tight">
              <Money amount={position.totalBase} currency={position.baseCurrency} />
            </p>

            <div className="mt-4 flex gap-8 text-sm">
              <div>
                <p className="text-slate-500">In the drawers</p>
                <p className="font-medium">
                  <Money amount={position.cashTotalBase} currency={position.baseCurrency} />
                </p>
              </div>

              <div>
                <p className="text-slate-500">At the bank</p>
                <p className="font-medium">
                  <Money amount={position.bankTotalBase} currency={position.baseCurrency} />
                </p>
              </div>
            </div>

            <p className="mt-4 text-xs text-slate-500">
              As of {new Date(position.asOf).toLocaleString()}, totalled in {position.baseCurrency}. Closed
              accounts are not in it — they hold nothing.
            </p>
          </section>
        )}

        <Group
          title="Cash"
          subtitle="Tills, petty cash, the safe. Money the shop can hold."
          accounts={accounts.filter((a) => a.kind === FinancialAccountKind.Cash)}
          empty="No cash accounts. The till has nowhere to put the takings."
          onStatement={setStatementFor}
          onEdit={setEditing}
        />

        <Group
          title="Bank"
          subtitle="Money somebody else holds for the shop."
          accounts={accounts.filter((a) => a.kind === FinancialAccountKind.Bank)}
          empty="No bank accounts."
          onStatement={setStatementFor}
          onEdit={setEditing}
        />
      </Can>

      {dialog === "open" && (
        <EntityForm
          title="Open an account"
          fields={[
            { name: "name", label: "Name", required: true, placeholder: "Deira till" },
            {
              name: "kind",
              label: "Kind",
              type: "select",
              required: true,
              options: [
                { value: String(FinancialAccountKind.Cash), label: "Cash" },
                { value: String(FinancialAccountKind.Bank), label: "Bank" },
              ],
            },
            {
              name: "branchId",
              label: "Branch",
              type: "select",
              options: branches.map((branch) => ({ value: branch.id, label: branch.name })),
              help: "Leave blank for a company-wide account — the bank account every branch pays into.",
            },
            {
              name: "currencyCode",
              label: "Currency",
              placeholder: "AED",
              help: "The money this account holds. It cannot be changed afterwards: it would restate every movement.",
            },
            { name: "bankName", label: "Bank" },
            { name: "accountNumber", label: "Account number / IBAN" },
            {
              name: "openingBalance",
              label: "Opening balance",
              type: "number",
              help: "What was already in it. It becomes the first movement on the statement, not a number sitting outside the ledger.",
            },
            { name: "openedAt", label: "On", type: "date" },
            {
              name: "allowsOverdraft",
              label: "May go overdrawn",
              type: "checkbox",
              help: "A bank may lend the shop money. A drawer may not — there are no negative notes.",
            },
            { name: "notes", label: "Notes", type: "textarea" },
          ]}
          initial={{ kind: String(FinancialAccountKind.Cash) }}
          submitLabel="Open it"
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            await openAccount(
              { token },
              {
                name: String(v.name),
                kind: Number(v.kind),
                branchId: (v.branchId as string) || null,
                currencyCode: (v.currencyCode as string) || null,
                bankName: (v.bankName as string) || null,
                accountNumber: (v.accountNumber as string) || null,
                allowsOverdraft: Boolean(v.allowsOverdraft),
                openingBalance: Number(v.openingBalance) || 0,
                openedAt: v.openedAt ? new Date(String(v.openedAt)).toISOString() : null,
                notes: (v.notes as string) || null,
              },
            );

            await reload();
          }}
        />
      )}

      {editing && (
        <EntityForm
          title={`Edit ${editing.name}`}
          fields={[
            { name: "name", label: "Name", required: true },
            { name: "bankName", label: "Bank" },
            { name: "accountNumber", label: "Account number / IBAN" },
            { name: "allowsOverdraft", label: "May go overdrawn", type: "checkbox" },
            {
              name: "isActive",
              label: "Open",
              type: "checkbox",
              help: "Closing an account that still holds money is refused: the money would vanish from the cash position while still being in the drawer.",
            },
            { name: "notes", label: "Notes", type: "textarea" },
          ]}
          // The currency and the kind are absent on purpose, not forgotten: an AED till holding 4,300 does
          // not become a USD till holding 4,300 because somebody changed a dropdown.
          initial={{
            name: editing.name,
            bankName: editing.bankName ?? "",
            accountNumber: editing.accountNumber ?? "",
            allowsOverdraft: editing.allowsOverdraft,
            isActive: editing.isActive,
            notes: editing.notes ?? "",
          }}
          onClose={() => setEditing(null)}
          onSubmit={async (v) => {
            await updateAccount(
              { token },
              editing.id,
              {
                name: String(v.name),
                bankName: (v.bankName as string) || null,
                accountNumber: (v.accountNumber as string) || null,
                allowsOverdraft: Boolean(v.allowsOverdraft),
                isActive: Boolean(v.isActive),
                notes: (v.notes as string) || null,
              },
            );

            await reload();
          }}
        />
      )}

      {dialog === "transfer" && (
        <TransferDialog
          accounts={transferable}
          token={token}
          onClose={() => setDialog(null)}
          onTransferred={reload}
        />
      )}

      {statementFor && (
        <AccountStatementDrawer accountId={statementFor} onClose={() => setStatementFor(null)} />
      )}
    </div>
  );
}

function Group({
  title,
  subtitle,
  accounts,
  empty,
  onStatement,
  onEdit,
}: {
  title: string;
  subtitle: string;
  accounts: FinancialAccount[];
  empty: string;
  onStatement: (id: string) => void;
  onEdit: (account: FinancialAccount) => void;
}) {
  return (
    <section className="space-y-3">
      <div>
        <h2 className="text-sm font-medium uppercase tracking-wider text-slate-400">{title}</h2>
        <p className="text-xs text-slate-500">{subtitle}</p>
      </div>

      <div className="divide-y divide-slate-100 rounded-lg border border-slate-200 dark:divide-slate-800 dark:border-slate-800">
        {accounts.length === 0 ? (
          <p className="p-4 text-sm text-slate-500">{empty}</p>
        ) : (
          accounts.map((account) => (
            <div key={account.id} className="flex items-center justify-between gap-4 p-4">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <p className="text-sm font-medium">{account.name}</p>
                  {!account.isActive && <StatusBadge label="Closed" tone="neutral" />}
                  {account.allowsOverdraft && <StatusBadge label="Overdraft" tone="warn" />}
                  {account.balance < 0 && <StatusBadge label="Overdrawn" tone="bad" />}
                </div>

                <p className="truncate text-xs text-slate-500">
                  {[
                    account.branchName ?? "Company-wide",
                    account.bankName,
                    account.accountNumber,
                  ]
                    .filter(Boolean)
                    .join(" · ")}
                </p>
              </div>

              <div className="flex shrink-0 items-center gap-4">
                <div className="text-right">
                  <p className="font-medium">
                    <Money amount={account.balance} currency={account.currencyCode} />
                  </p>

                  {/* Only worth the line when the two differ. On an account already in base currency they
                      are the same number, and printing it twice would just be noise. */}
                  {account.balance !== account.balanceBase && (
                    <p className="text-xs text-slate-500">
                      <Money amount={account.balanceBase} /> in the company&apos;s money
                    </p>
                  )}
                </div>

                <button
                  type="button"
                  onClick={() => onStatement(account.id)}
                  className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                >
                  Statement
                </button>

                <Can feature={FEATURES.accounts} action={PermissionAction.Edit}>
                  <button
                    type="button"
                    onClick={() => onEdit(account)}
                    className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                  >
                    Edit
                  </button>
                </Can>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}
