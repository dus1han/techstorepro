"use client";

import { useAuth } from "@/lib/auth-context";

/**
 * P1 dashboard. Deliberately thin: the animated widgets of requirements §36 need sales, stock and
 * repair data, none of which exists until P3–P6. Showing empty charts now would be theatre.
 */
export default function DashboardPage() {
  const { user } = useAuth();

  if (!user) return null;

  return (
    <div className="mx-auto max-w-4xl space-y-8">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">
          {user.companyName}
        </h1>
        <p className="text-sm text-slate-500">
          Signed in as {user.fullName}
          {user.isOwner ? " — owner" : ""}
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <Stat label="Your permissions" value={user.isOwner ? "All" : String(user.permissions.length)} />
        <Stat label="Branches you can access" value={String(user.accessibleBranchIds.length)} />
        <Stat label="Phase" value="P1" />
      </div>

      <div className="rounded-lg border border-slate-200 p-5 dark:border-slate-800">
        <h2 className="text-sm font-medium">What works today</h2>
        <ul className="mt-3 space-y-1.5 text-sm text-slate-600 dark:text-slate-400">
          <li>Companies, branches and warehouses (branch-owned or company-shared)</li>
          <li>Users with per-user, per-feature permissions — no roles</li>
          <li>Effective-dated settings, so changing a rule never rewrites history</li>
          <li>Audit trail with old and new values on every change</li>
        </ul>
        <p className="mt-4 text-sm text-slate-500">
          Sales, inventory and repairs arrive in later phases — see docs/architecture.md §6.
        </p>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-slate-200 p-4 dark:border-slate-800">
      <p className="text-xs uppercase tracking-wider text-slate-400">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums">{value}</p>
    </div>
  );
}
