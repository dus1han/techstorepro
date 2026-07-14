"use client";

import { Can } from "@/components/auth/can";
import { PosTerminal } from "@/features/sales/components/pos-terminal";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * The till (requirements §22).
 *
 * One call, one transaction, three documents: the goods leave, the bill is raised, the money is taken.
 * At a counter those are a single act — a customer who has walked out with a laptop has not "maybe" paid
 * for it. If the card is declined, the laptop is still in stock and no invoice is chasing anybody.
 */
export default function PosPage() {
  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Point of sale</h1>
        <p className="mt-1 text-sm text-slate-500">
          Scan, take the money, hand over the goods. Prices are shown before tax; the tax is added on the
          discounted line.
        </p>
      </div>

      <Can
        feature={FEATURES.salesInvoices}
        action={PermissionAction.Create}
        fallback={<p className="text-sm text-slate-500">You do not have permission to sell.</p>}
      >
        <PosTerminal />
      </Can>
    </div>
  );
}
