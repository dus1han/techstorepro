/**
 * Money and status, rendered the same way everywhere in the product.
 *
 * Totals are tabular-nums and right-aligned without exception: a column of figures that does not line
 * up is a column nobody can scan, and these are the modules where people scan columns of figures.
 *
 * This lived in `features/sales` until purchasing needed it too. It is shared UI rather than a sales
 * export because a feature module importing another feature module's components is how two modules
 * quietly become one — and P6 (repairs) and P7 (finance) will both want it.
 */

export function Money({ amount, currency = "AED" }: { amount: number; currency?: string }) {
  return (
    <span className="tabular-nums">
      {amount.toLocaleString(undefined, {
        style: "currency",
        currency,
        minimumFractionDigits: 2,
      })}
    </span>
  );
}

type Tone = "neutral" | "good" | "warn" | "bad";

const TONES: Record<Tone, string> = {
  neutral: "bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300",
  good: "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300",
  warn: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300",
  bad: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300",
};

export function StatusBadge({ label, tone = "neutral" }: { label: string; tone?: Tone }) {
  return (
    <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${TONES[tone]}`}>
      {label}
    </span>
  );
}
