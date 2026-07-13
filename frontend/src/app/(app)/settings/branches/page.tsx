"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { api, ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";
import type { PagedResult } from "@/types/api";

export default function BranchesPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const { data, error: loadError } = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches");

  const branches = data?.items ?? [];

  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const reload = () => client.invalidateQueries({ queryKey: ["branches"] });

  async function remove(branch: Branch) {
    if (!accessToken) return;

    // Requirements §10 makes the reason mandatory, and the API rejects an empty one — so the UI
    // asks for it rather than letting the user discover the rule as a 400.
    const reason = window.prompt(`Why are you deleting "${branch.name}"?`);
    if (!reason) return;

    try {
      await api.delete(`api/v1/branches/${branch.id}`, {
        token: accessToken,
        query: { reason },
      });
      await reload();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Could not delete the branch.");
    }
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Branches</h1>
          <p className="mt-1 text-sm text-slate-500">
            Shops and repair centres. Stock lives in warehouses, not branches.
          </p>
        </div>

        <Can feature={FEATURES.branches} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New branch
          </button>
        </Can>
      </div>

      {(error ?? loadError) && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error ?? loadError?.message}
        </p>
      )}

      <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-800">
        <table className="w-full text-sm">
          <thead className="border-b border-slate-200 bg-slate-50 text-left dark:border-slate-800 dark:bg-slate-900">
            <tr>
              <th className="px-4 py-2.5 font-medium">Code</th>
              <th className="px-4 py-2.5 font-medium">Name</th>
              <th className="px-4 py-2.5 font-medium">Default warehouse</th>
              <th className="px-4 py-2.5" />
            </tr>
          </thead>
          <tbody>
            {branches.map((branch) => (
              <tr key={branch.id} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                <td className="px-4 py-2.5 font-mono text-xs">{branch.code}</td>
                <td className="px-4 py-2.5 font-medium">
                  {branch.name}
                  {branch.isDefault && (
                    <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-400">
                      default
                    </span>
                  )}
                </td>
                <td className="px-4 py-2.5 text-slate-600 dark:text-slate-400">
                  {branch.defaultWarehouseName ?? "—"}
                </td>
                <td className="px-4 py-2.5 text-right">
                  <Can feature={FEATURES.branches} action={PermissionAction.Delete}>
                    <button
                      onClick={() => void remove(branch)}
                      disabled={branch.isDefault}
                      title={branch.isDefault ? "The default branch cannot be deleted." : undefined}
                      className="rounded-md border border-slate-200 px-2.5 py-1 text-xs text-red-600 hover:bg-red-50 disabled:opacity-40 dark:border-slate-700 dark:hover:bg-red-950"
                    >
                      Delete
                    </button>
                  </Can>
                </td>
              </tr>
            ))}
            {branches.length === 0 && (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-slate-500">
                  No branches.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {creating && (
        <CreateBranch
          onClose={() => setCreating(false)}
          onCreated={async () => {
            setCreating(false);
            await reload();
          }}
        />
      )}
    </div>
  );
}

function CreateBranch({ onClose, onCreated }: { onClose: () => void; onCreated: () => Promise<void> }) {
  const { accessToken } = useAuth();

  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!accessToken) return;

    setBusy(true);
    setFieldErrors({});
    setError(null);

    try {
      await api.post("api/v1/branches", { token: accessToken, body: { name, code } });
      await onCreated();
    } catch (caught) {
      if (caught instanceof ApiError) {
        // The API returns RFC 7807 with an `errors` map, so server-side validation lands on the
        // right field automatically instead of being retyped as a client-side rule that can drift.
        setFieldErrors(caught.fieldErrors);
        setError(Object.keys(caught.fieldErrors).length === 0 ? caught.message : null);
      } else {
        setError("Could not create the branch.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6">
      <form
        onSubmit={submit}
        className="w-full max-w-sm space-y-4 rounded-lg border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-950"
      >
        <h2 className="text-lg font-semibold">New branch</h2>

        <Field label="Name" value={name} onChange={setName} errors={fieldErrors.Name} />
        <Field label="Code" value={code} onChange={setCode} errors={fieldErrors.Code} />

        {error && (
          <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-slate-200 px-3 py-1.5 text-sm hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Creating…" : "Create"}
          </button>
        </div>
      </form>
    </div>
  );
}

function Field({
  label,
  value,
  onChange,
  errors,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  errors?: string[];
}) {
  return (
    <div className="space-y-1.5">
      <label className="text-sm font-medium">{label}</label>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-md border border-slate-200 bg-transparent px-3 py-2 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300"
      />
      {errors?.map((message) => (
        <p key={message} className="text-xs text-red-600 dark:text-red-400">
          {message}
        </p>
      ))}
    </div>
  );
}
