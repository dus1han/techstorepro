"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { FEATURES, PermissionAction, type Setting } from "@/types/identity";
import { Can } from "@/components/auth/can";

/**
 * Effective-dated configuration (requirements §11).
 *
 * Saving here does not overwrite a value — it writes a new *version* from now onwards. A document
 * raised last month still resolves last month's value, which is what General Rule 3 requires and
 * why the screen shows "in force since" rather than "last edited".
 */
export default function ConfigurationPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const { data, error: loadError } = useApiQuery<Setting[]>(["settings"], "api/v1/settings");

  const settings = data ?? [];

  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState<string | null>(null);

  async function save(setting: Setting, value: string) {
    if (!accessToken || value === setting.effectiveValue) return;

    setSaving(setting.key);
    setError(null);

    try {
      await api.put(`api/v1/settings/${setting.key}`, {
        token: accessToken,
        body: { key: setting.key, value },
      });
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Could not save the setting.");
    } finally {
      setSaving(null);

      // Refetch either way: on success to pick up the new "in force since", and on failure to snap
      // the input back to the value that is actually stored rather than leaving a rejected edit on
      // screen looking as though it took.
      await client.invalidateQueries({ queryKey: ["settings"] });
    }
  }

  const modules = [...new Set(settings.map((s) => s.module))];

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Configuration</h1>
        <p className="mt-1 text-sm text-slate-500">
          Business rules, not code. Changing a value here takes effect from now on — documents
          already raised keep the value that was in force when they were raised.
        </p>
      </div>

      {(error ?? loadError) && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error ?? loadError?.message}
        </p>
      )}

      {modules.map((module) => (
        <section key={module} className="space-y-3">
          <h2 className="text-sm font-medium uppercase tracking-wider text-slate-400">{module}</h2>

          <div className="divide-y divide-slate-100 rounded-lg border border-slate-200 dark:divide-slate-800 dark:border-slate-800">
            {settings
              .filter((s) => s.module === module)
              .map((setting) => (
                <div key={setting.key} className="flex items-center justify-between gap-4 p-4">
                  <div className="min-w-0">
                    <p className="text-sm font-medium">{setting.name}</p>
                    <p className="truncate font-mono text-xs text-slate-400">{setting.key}</p>
                    <p className="mt-0.5 text-xs text-slate-500">
                      {setting.isOverridden && setting.validFrom
                        ? `In force since ${new Date(setting.validFrom).toLocaleString()}`
                        : `Platform default (${setting.defaultValue})`}
                    </p>
                  </div>

                  <Can
                    feature={FEATURES.settings}
                    action={PermissionAction.Edit}
                    fallback={
                      <span className="font-mono text-sm tabular-nums">{setting.effectiveValue}</span>
                    }
                  >
                    <SettingInput
                      // Keyed on the value: when the server's value changes, React remounts the
                      // input with the new one. That replaces a useEffect that mirrored a prop into
                      // state — the pattern React (and Next's compiler lint) rightly rejects.
                      key={`${setting.key}:${setting.effectiveValue}`}
                      setting={setting}
                      busy={saving === setting.key}
                      onCommit={(value) => void save(setting, value)}
                    />
                  </Can>
                </div>
              ))}
          </div>
        </section>
      ))}
    </div>
  );
}

function SettingInput({
  setting,
  busy,
  onCommit,
}: {
  setting: Setting;
  busy: boolean;
  onCommit: (value: string) => void;
}) {
  const [value, setValue] = useState(setting.effectiveValue);

  // dataType 4 == Boolean, mirroring SettingDataType.
  if (setting.dataType === 4) {
    return (
      <input
        type="checkbox"
        checked={value === "true"}
        disabled={busy}
        onChange={(e) => {
          const next = String(e.target.checked);
          setValue(next);
          onCommit(next);
        }}
        className="size-4 accent-slate-900 dark:accent-slate-100"
        aria-label={setting.name}
      />
    );
  }

  return (
    <input
      value={value}
      disabled={busy}
      onChange={(e) => setValue(e.target.value)}
      onBlur={() => onCommit(value)}
      aria-label={setting.name}
      className="w-28 rounded-md border border-slate-200 bg-transparent px-2 py-1 text-right font-mono text-sm tabular-nums outline-none focus:border-slate-900 disabled:opacity-50 dark:border-slate-700 dark:focus:border-slate-300"
    />
  );
}
