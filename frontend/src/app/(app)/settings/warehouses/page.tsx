"use client";

import { useApiQuery } from "@/lib/use-api";

interface WarehouseAccess {
  branchId: string;
  branchName: string;
  canIssue: boolean;
  canReceive: boolean;
}

interface Warehouse {
  id: string;
  name: string;
  code: string;
  type: number;
  branchId: string | null;
  branchName: string | null;
  isShared: boolean;
  sharedWith: WarehouseAccess[];
  isActive: boolean;
}

const TYPE_LABELS: Record<number, string> = {
  1: "Main",
  2: "Shop",
  3: "Repair",
  4: "Returns",
  5: "Faulty",
  6: "Transit",
};

/**
 * Warehouses are where stock actually sits — branches do not hold stock.
 *
 * A warehouse is either owned by one branch (private to it) or shared at company level, in which
 * case the branches allowed to use it are listed explicitly. "Shared" never silently means "any
 * branch may drain it": issue and receive are granted separately, per branch.
 */
export default function WarehousesPage() {
  const { data, error } = useApiQuery<Warehouse[]>(["warehouses"], "api/v1/warehouses");

  const warehouses = data ?? [];

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Warehouses</h1>
        <p className="mt-1 text-sm text-slate-500">
          Stock lives here. A warehouse is owned by a branch, or shared across the company with an
          explicit list of branches that may use it.
        </p>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error.message}
        </p>
      )}

      <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-800">
        <table className="w-full text-sm">
          <thead className="border-b border-slate-200 bg-slate-50 text-left dark:border-slate-800 dark:bg-slate-900">
            <tr>
              <th className="px-4 py-2.5 font-medium">Code</th>
              <th className="px-4 py-2.5 font-medium">Name</th>
              <th className="px-4 py-2.5 font-medium">Type</th>
              <th className="px-4 py-2.5 font-medium">Ownership</th>
            </tr>
          </thead>
          <tbody>
            {warehouses.map((warehouse) => (
              <tr key={warehouse.id} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                <td className="px-4 py-2.5 font-mono text-xs">{warehouse.code}</td>
                <td className="px-4 py-2.5 font-medium">{warehouse.name}</td>
                <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">
                  {TYPE_LABELS[warehouse.type] ?? warehouse.type}
                </td>
                <td className="px-4 py-2.5">
                  {warehouse.isShared ? (
                    <div>
                      <span className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-400">
                        company-shared
                      </span>
                      <p className="mt-1 text-xs text-slate-500">
                        {warehouse.sharedWith.length === 0
                          ? "No branch has been granted access yet."
                          : warehouse.sharedWith
                              .map(
                                (a) =>
                                  `${a.branchName} (${[a.canIssue && "issue", a.canReceive && "receive"]
                                    .filter(Boolean)
                                    .join(" + ")})`,
                              )
                              .join(", ")}
                      </p>
                    </div>
                  ) : (
                    <span className="text-slate-600 dark:text-slate-400">
                      {warehouse.branchName}
                    </span>
                  )}
                </td>
              </tr>
            ))}
            {warehouses.length === 0 && (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-slate-500">
                  No warehouses.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
