"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { api, ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import {
  ACTION_LABELS,
  FEATURES,
  PermissionAction,
  type CompanyUser,
  type PermissionGrid,
} from "@/types/identity";

const ALL_ACTIONS: PermissionAction[] = [
  PermissionAction.View,
  PermissionAction.Create,
  PermissionAction.Edit,
  PermissionAction.Delete,
  PermissionAction.Approve,
  PermissionAction.Print,
  PermissionAction.Export,
];

export default function UsersPage() {
  const client = useQueryClient();

  const { data, error } = useApiQuery<CompanyUser[]>(["users"], "api/v1/users");

  const users = data ?? [];

  const [selected, setSelected] = useState<string | null>(null);

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Users &amp; permissions</h1>
        <p className="mt-1 text-sm text-slate-500">
          Permissions are granted per user, per feature. There are no roles — see requirements §7.
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
              <th className="px-4 py-2.5 font-medium">Name</th>
              <th className="px-4 py-2.5 font-medium">Email</th>
              <th className="px-4 py-2.5 font-medium">Permissions</th>
              <th className="px-4 py-2.5" />
            </tr>
          </thead>
          <tbody>
            {users.map((user) => (
              <tr key={user.companyUserId} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                <td className="px-4 py-2.5 font-medium">
                  {user.fullName}
                  {user.isOwner && (
                    <span className="ml-2 rounded bg-slate-900 px-1.5 py-0.5 text-xs text-white dark:bg-slate-100 dark:text-slate-900">
                      owner
                    </span>
                  )}
                </td>
                <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">{user.email}</td>
                <td className="px-4 py-2.5 tabular-nums">
                  {user.isOwner ? "All (implicit)" : user.permissionCount}
                </td>
                <td className="px-4 py-2.5 text-right">
                  <Can feature={FEATURES.permissions} action={PermissionAction.Edit}>
                    <button
                      onClick={() => setSelected(user.companyUserId)}
                      disabled={user.isOwner}
                      title={user.isOwner ? "The owner holds every permission implicitly." : undefined}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-slate-800"
                    >
                      Edit permissions
                    </button>
                  </Can>
                </td>
              </tr>
            ))}
            {users.length === 0 && (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-slate-500">
                  No users yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {selected && (
        <PermissionEditor
          companyUserId={selected}
          onClose={() => setSelected(null)}
          onSaved={async () => {
            setSelected(null);
            await client.invalidateQueries({ queryKey: ["users"] });
          }}
        />
      )}
    </div>
  );
}

/**
 * The permission matrix: every feature × every action, ticked per user.
 *
 * The bulk toggles (a whole feature row, a whole action column) are the answer to "must an admin
 * really tick 300 boxes for each new cashier?". They are a **UI affordance only** — each one writes
 * individual grants. Nothing is stored as a reusable bundle, so editing one user never changes
 * another. That is what makes this a literal reading of "no fixed roles" rather than roles wearing
 * a disguise.
 */
function PermissionEditor({
  companyUserId,
  onClose,
  onSaved,
}: {
  companyUserId: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const { accessToken } = useAuth();

  const { data: grid } = useApiQuery<PermissionGrid>(
    ["permissions", companyUserId],
    `api/v1/users/${companyUserId}/permissions`,
  );

  const [granted, setGranted] = useState<Set<string> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const key = (feature: string, action: PermissionAction) => `${feature}:${action}`;

  if (!grid) return null;

  // Seeded from the server's grid on first render after it loads, then owned locally so that
  // ticking a box is instant rather than a round trip.
  const held =
    granted ??
    new Set(
      grid.features.flatMap((f) =>
        f.actions.filter((a) => a.granted).map((a) => key(f.feature, a.action)),
      ),
    );

  const toggle = (feature: string, action: PermissionAction) => {
    const next = new Set(held);
    const id = key(feature, action);

    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }

    setGranted(next);
  };

  /** Bulk toggle: an entire feature's row. Writes individual grants — it is not a stored role. */
  const toggleFeatureRow = (feature: string) => {
    const definition = grid.features.find((f) => f.feature === feature)!;
    const supported = definition.actions.filter((a) => a.supported);
    const allOn = supported.every((a) => held.has(key(feature, a.action)));

    const next = new Set(held);

    for (const action of supported) {
      const id = key(feature, action.action);

      if (allOn) {
        next.delete(id);
      } else {
        next.add(id);
      }
    }

    setGranted(next);
  };

  /** Bulk toggle: an entire action's column, across every feature that supports it. */
  const toggleActionColumn = (action: PermissionAction) => {
    const applicable = grid.features.filter((f) =>
      f.actions.some((a) => a.action === action && a.supported),
    );
    const allOn = applicable.every((f) => held.has(key(f.feature, action)));

    const next = new Set(held);

    for (const feature of applicable) {
      const id = key(feature.feature, action);

      if (allOn) {
        next.delete(id);
      } else {
        next.add(id);
      }
    }

    setGranted(next);
  };

  async function save() {
    if (!accessToken || !grid) return;

    setBusy(true);
    setError(null);

    try {
      // The whole grid is sent, not a diff: a diff would race with a second admin editing the same
      // user and silently merge two conflicting intentions.
      const permissions = grid.features.flatMap((f) =>
        f.actions
          .filter((a) => a.supported)
          .map((a) => ({
            feature: f.feature,
            action: a.action,
            granted: held.has(key(f.feature, a.action)),
          })),
      );

      await api.put(`api/v1/users/${companyUserId}/permissions`, {
        token: accessToken,
        body: { companyUserId, permissions },
      });

      await onSaved();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Could not save permissions.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6">
      <div className="max-h-[85vh] w-full max-w-3xl overflow-auto rounded-lg border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-950">
        <div className="mb-5">
          <h2 className="text-lg font-semibold">{grid.userFullName}</h2>
          <p className="text-sm text-slate-500">{grid.userEmail}</p>
        </div>

        {error && (
          <p role="alert" className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 dark:border-slate-800">
                <th className="py-2 pr-4 text-left font-medium">Feature</th>
                {ALL_ACTIONS.map((action) => (
                  <th key={action} className="px-2 py-2 text-center font-medium">
                    <button
                      onClick={() => toggleActionColumn(action)}
                      className="text-xs text-slate-500 underline-offset-2 hover:underline"
                      title={`Toggle ${ACTION_LABELS[action]} for every feature`}
                    >
                      {ACTION_LABELS[action]}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {grid.features.map((feature) => (
                <tr key={feature.feature} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                  <td className="py-2 pr-4">
                    <button
                      onClick={() => toggleFeatureRow(feature.feature)}
                      className="text-left underline-offset-2 hover:underline"
                      title="Toggle every action for this feature"
                    >
                      {feature.name}
                    </button>
                    <p className="text-xs text-slate-400">{feature.feature}</p>
                  </td>

                  {ALL_ACTIONS.map((action) => {
                    const cell = feature.actions.find((a) => a.action === action);

                    return (
                      <td key={action} className="px-2 py-2 text-center">
                        {cell?.supported ? (
                          <input
                            type="checkbox"
                            checked={held.has(key(feature.feature, action))}
                            onChange={() => toggle(feature.feature, action)}
                            className="size-4 accent-slate-900 dark:accent-slate-100"
                            aria-label={`${ACTION_LABELS[action]} ${feature.name}`}
                          />
                        ) : (
                          // Not every action makes sense for every feature — the audit log cannot be
                          // created, only viewed and exported. An unsupported action is shown as a
                          // dash rather than an unchecked box, so it does not read as "revoked".
                          <span className="text-slate-300 dark:text-slate-700">—</span>
                        )}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="mt-6 flex justify-end gap-2">
          <button
            onClick={onClose}
            className="rounded-md border border-slate-200 px-3 py-1.5 text-sm hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
          >
            Cancel
          </button>
          <button
            onClick={() => void save()}
            disabled={busy}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Saving…" : "Save permissions"}
          </button>
        </div>
      </div>
    </div>
  );
}
