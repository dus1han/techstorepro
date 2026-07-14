"use client";

import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { Money } from "@/components/ui/money";
import type { CustomerPayment } from "@/features/sales/types";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * Money received from customers (requirements §23).
 *
 * A payment is a header, its **tender lines** and its **allocations**. One sale is settled by cash *and*
 * card; one transfer settles three invoices; one invoice is settled by two instalments. A single payment
 * method and a single invoice id on the header could express none of that — which is why the table shows
 * all three.
 *
 * Money not yet pointed at an invoice is a **credit**, not an error: a deposit, or a customer paying down
 * their account.
 */
export default function PaymentsPage() {
  const columns: Column<CustomerPayment>[] = [
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
    { key: "customer", header: "Customer", render: (p) => p.customerName },
    {
      key: "tender",
      header: "How it arrived",
      render: (p) => (
        <div className="space-y-0.5">
          {p.methods.map((method) => (
            <p key={method.id} className="text-xs">
              {method.paymentMethodName} <Money amount={method.amount} currency={p.currencyCode} />
              {method.reference && <span className="ml-1 font-mono text-slate-500">{method.reference}</span>}
            </p>
          ))}
        </div>
      ),
    },
    {
      key: "against",
      header: "Settled",
      render: (p) => (
        <div className="space-y-0.5">
          {p.allocations.map((allocation) => (
            <p key={allocation.id} className="font-mono text-xs">
              {allocation.invoiceNumber} <Money amount={allocation.amount} currency={p.currencyCode} />
            </p>
          ))}

          {p.unallocatedAmount > 0 && (
            <p className="text-xs text-amber-600 dark:text-amber-400">
              <Money amount={p.unallocatedAmount} currency={p.currencyCode} /> on account
            </p>
          )}
        </div>
      ),
    },
    {
      key: "amount",
      header: "Received",
      align: "right",
      render: (p) => <Money amount={p.amount} currency={p.currencyCode} />,
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Customer payments</h1>
        <p className="mt-1 text-sm text-slate-500">
          Cash and card on one sale; one payment across three invoices. Money with no invoice against it is
          a credit on the account.
        </p>
      </div>

      <Can
        feature={FEATURES.customerPayments}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view payments.</p>}
      >
        <DataTable
          queryKey={["customer-payments"]}
          endpoint="api/v1/customer-payments"
          columns={columns}
          rowKey={(p) => p.id}
          searchPlaceholder="Search payment, customer or reference…"
          emptyMessage="No payments taken yet."
        />
      </Can>
    </div>
  );
}
