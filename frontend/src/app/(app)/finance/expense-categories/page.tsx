"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { EntityForm } from "@/components/data/entity-form";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { createExpenseCategory, updateExpenseCategory } from "@/features/finance/api";
import type { ExpenseCategory } from "@/features/finance/types";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * What kinds of thing the shop spends money on (requirements §34).
 *
 * Reference data the shop owns rather than an enum, because §34's list ends in "other expenses" and a fixed
 * enum would make every shop's fifth category "Other" for ever.
 *
 * A category is retired, never deleted — the expenses already booked against it still have to say what they
 * were for. A retired one takes no new expenses and the server refuses them.
 */
export default function ExpenseCategoriesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();
  const token = accessToken!;

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<ExpenseCategory | null>(null);

  const { data, error } = useApiQuery<ExpenseCategory[]>(
    ["finance", "expense-categories"],
    "api/v1/finance/expense-categories",
  );

  const categories = data ?? [];

  const reload = async () => {
    setCreating(false);
    setEditing(null);

    await client.invalidateQueries({ queryKey: ["finance", "expense-categories"] });
  };

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Expense categories</h1>
          <p className="mt-1 text-sm text-slate-500">
            The buckets the expense report adds up. Retiring one keeps its history and stops it taking
            anything new.
          </p>
        </div>

        <Can feature={FEATURES.expenseCategories} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
          >
            Add
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error.message}
        </p>
      )}

      <Can
        feature={FEATURES.expenseCategories}
        action={PermissionAction.View}
        fallback={
          <p className="text-sm text-slate-500">You do not have permission to view expense categories.</p>
        }
      >
        <div className="divide-y divide-slate-100 rounded-lg border border-slate-200 dark:divide-slate-800 dark:border-slate-800">
          {categories.length === 0 ? (
            <p className="p-4 text-sm text-slate-500">
              No categories. An expense cannot be recorded until there is at least one.
            </p>
          ) : (
            categories.map((category) => (
              <div key={category.id} className="flex items-center justify-between gap-4 p-4">
                <div className="min-w-0">
                  <p className="text-sm font-medium">{category.name}</p>
                  <p className="truncate text-xs text-slate-500">
                    {category.description ?? "—"} · {category.expenseCount} expense
                    {category.expenseCount === 1 ? "" : "s"}
                  </p>
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  {!category.isActive && (
                    <span className="rounded px-1.5 py-0.5 text-xs bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400">
                      retired
                    </span>
                  )}

                  <Can feature={FEATURES.expenseCategories} action={PermissionAction.Edit}>
                    <button
                      onClick={() => setEditing(category)}
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
      </Can>

      {creating && (
        <EntityForm
          title="New expense category"
          fields={[
            { name: "name", label: "Name", required: true, placeholder: "Rent" },
            { name: "description", label: "Description", type: "textarea" },
          ]}
          onClose={() => setCreating(false)}
          onSubmit={async (v) => {
            await createExpenseCategory(
              { token },
              { name: String(v.name), description: (v.description as string) || null },
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
            { name: "description", label: "Description", type: "textarea" },
            {
              name: "isActive",
              label: "In use",
              type: "checkbox",
              help: "Turn it off to retire it. The expenses already booked against it keep it.",
            },
          ]}
          initial={{
            name: editing.name,
            description: editing.description ?? "",
            isActive: editing.isActive,
          }}
          onClose={() => setEditing(null)}
          onSubmit={async (v) => {
            await updateExpenseCategory(
              { token },
              editing.id,
              {
                name: String(v.name),
                description: (v.description as string) || null,
                isActive: Boolean(v.isActive),
              },
            );

            await reload();
          }}
        />
      )}
    </div>
  );
}
