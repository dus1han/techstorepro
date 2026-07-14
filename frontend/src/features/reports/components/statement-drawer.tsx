"use client";

import { useApiQuery } from "@/lib/use-api";
import { Money } from "@/components/ui/money";
import type { CustomerStatement, StatementLine, SupplierStatement } from "@/features/reports/types";

/**
 * A statement of account — the document a shop emails when it wants to be paid, and the one the customer
 * argues with.
 *
 * <b>The opening balance is the whole design.</b> A statement that just listed the documents in a window
 * would be unreadable: the customer would see three invoices and a payment and have no idea how it relates
 * to the figure at the bottom. So it opens with what they owed the moment before the window, walks every
 * movement, and closes on what they owe now — and the running balance down the right-hand side is the
 * column their finance person will actually read.
 *
 * Debit and credit are the customer's-eye view: on a receivables statement a debit is what they owe, on a
 * payables statement it is what the shop has paid. Both are labelled rather than assumed, because getting
 * this backwards is the single most common way a statement gets sent out wrong.
 */

export function CustomerStatementDrawer({
  customerId,
  onClose,
}: {
  customerId: string;
  onClose: () => void;
}) {
  const { data } = useApiQuery<CustomerStatement>(
    ["reports", "customer-statement", customerId],
    `api/v1/reports/customer-statement/${customerId}`,
  );

  return (
    <Drawer onClose={onClose}>
      {!data ? (
        <p className="text-sm text-slate-500">Loading the statement…</p>
      ) : (
        <Statement
          title={data.customerName}
          subtitle="Statement of account"
          currency={data.currencyCode}
          from={data.from}
          to={data.to}
          opening={data.openingBalance}
          lines={data.lines}
          closing={data.closingBalance}
          closingLabel="Balance owing"
          variance={data.variance}
          memo={
            data.storeCredit !== 0
              ? {
                  label: "Store credit held",
                  amount: data.storeCredit,
                  note: "A voucher, not a payment. It comes off the bill on the day it is spent, not before.",
                }
              : null
          }
        />
      )}
    </Drawer>
  );
}

export function SupplierStatementDrawer({
  supplierId,
  onClose,
}: {
  supplierId: string;
  onClose: () => void;
}) {
  const { data } = useApiQuery<SupplierStatement>(
    ["reports", "supplier-statement", supplierId],
    `api/v1/reports/supplier-statement/${supplierId}`,
  );

  return (
    <Drawer onClose={onClose}>
      {!data ? (
        <p className="text-sm text-slate-500">Loading the statement…</p>
      ) : (
        <Statement
          title={data.supplierName}
          subtitle="Statement of account — every line in the company's own money"
          currency={data.currencyCode}
          from={data.from}
          to={data.to}
          opening={data.openingBalance}
          lines={data.lines}
          closing={data.closingBalance}
          closingLabel="Balance owed"
          variance={data.variance}
          memo={null}
        />
      )}
    </Drawer>
  );
}

function Drawer({ children, onClose }: { children: React.ReactNode; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-slate-900/40" onClick={onClose}>
      <div
        className="h-full w-full max-w-3xl overflow-y-auto bg-white p-6 shadow-xl dark:bg-slate-950"
        onClick={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>
  );
}

function Statement({
  title,
  subtitle,
  currency,
  from,
  to,
  opening,
  lines,
  closing,
  closingLabel,
  variance,
  memo,
}: {
  title: string;
  subtitle: string;
  currency: string;
  from: string;
  to: string;
  opening: number;
  lines: StatementLine[];
  closing: number;
  closingLabel: string;
  variance: number | null;
  memo: { label: string; amount: number; note: string } | null;
}) {
  return (
    <>
      <div>
        <h2 className="text-lg font-semibold">{title}</h2>
        <p className="text-sm text-slate-500">{subtitle}</p>
        <p className="mt-1 text-sm text-slate-500">
          {date(from)} to {date(to)}
        </p>
      </div>

      <table className="mt-6 w-full text-sm">
        <thead className="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500 dark:border-slate-800">
          <tr>
            <th className="py-2">Date</th>
            <th className="py-2">Document</th>
            <th className="py-2 text-right">Charges</th>
            <th className="py-2 text-right">Paid</th>
            <th className="py-2 text-right">Balance</th>
          </tr>
        </thead>

        <tbody>
          <tr className="border-b border-slate-100 text-slate-500 dark:border-slate-900">
            <td className="py-2">{date(from)}</td>
            <td className="py-2 italic">Opening balance</td>
            <td />
            <td />
            <td className="py-2 text-right">
              <Money amount={opening} currency={currency} />
            </td>
          </tr>

          {lines.map((line) => (
            <tr
              key={`${line.documentType}-${line.number}`}
              className="border-b border-slate-100 dark:border-slate-900"
            >
              <td className="py-2">{date(line.at)}</td>
              <td className="py-2">
                <span className="font-mono">{line.number}</span>
                <span className="ml-2 text-slate-500">{line.documentType}</span>
                {line.reference && (
                  <span className="ml-2 text-xs text-slate-400">{line.reference}</span>
                )}
              </td>
              <td className="py-2 text-right">
                {line.debit !== 0 ? <Money amount={line.debit} currency={currency} /> : null}
              </td>
              <td className="py-2 text-right">
                {line.credit !== 0 ? <Money amount={line.credit} currency={currency} /> : null}
              </td>
              <td className="py-2 text-right">
                <Money amount={line.runningBalance} currency={currency} />
              </td>
            </tr>
          ))}

          {lines.length === 0 && (
            <tr>
              <td colSpan={5} className="py-6 text-center text-sm text-slate-500">
                Nothing moved on this account in the period.
              </td>
            </tr>
          )}
        </tbody>

        <tfoot>
          <tr className="font-semibold">
            <td className="py-3" colSpan={4}>
              {closingLabel}
            </td>
            <td className="py-3 text-right">
              <Money amount={closing} currency={currency} />
            </td>
          </tr>
        </tfoot>
      </table>

      {memo && (
        <div className="mt-4 rounded-md bg-slate-50 p-3 text-sm dark:bg-slate-900">
          <div className="flex justify-between font-medium">
            <span>{memo.label}</span>
            <Money amount={memo.amount} currency={currency} />
          </div>
          <p className="mt-1 text-xs text-slate-500">{memo.note}</p>
        </div>
      )}

      <Reconciliation variance={variance} />
    </>
  );
}

/**
 * Whether the report managed to reproduce the stored balance.
 *
 * It is shown rather than hidden, and shown even when it is fine, because the balance is a cached figure
 * maintained by hand in a dozen places with no rebuild path. Drift in it is silent and permanent, and the
 * only moment anybody will ever notice is the moment a report like this one goes looking.
 */
export function Reconciliation({ variance }: { variance: number | null }) {
  if (variance === null) {
    return (
      <p className="mt-4 text-xs text-slate-500">
        Backdated — the stored balance is today&rsquo;s figure, so there is nothing here to reconcile it
        against.
      </p>
    );
  }

  if (variance === 0) {
    return (
      <p className="mt-4 text-xs text-emerald-700 dark:text-emerald-400">
        Reconciled — the documents add up to exactly the balance on the account.
      </p>
    );
  }

  return (
    <p role="alert" className="mt-4 text-xs font-medium text-red-700 dark:text-red-400">
      The documents do not add up to the balance on the account: they are out by{" "}
      <Money amount={variance} />. The stored balance has drifted from the documents behind it, and one of
      them is wrong.
    </p>
  );
}

function date(value: string) {
  return new Date(value).toLocaleDateString();
}
