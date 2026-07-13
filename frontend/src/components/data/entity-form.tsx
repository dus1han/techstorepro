"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api-client";

/**
 * The form primitive.
 *
 * Its real job is the error path. The API returns RFC 7807 problem details with an `errors` map
 * (field -> messages), and this binds those messages back onto the matching inputs automatically.
 * That means a validation rule is written **once**, on the server, and the form shows it — rather
 * than the rule being retyped in TypeScript where it can silently drift from the rule that is
 * actually enforced.
 *
 * The server is the only authority on what is valid. Client-side `required` attributes are a
 * convenience for the user, not a check anyone relies on.
 */

export interface FieldSpec {
  name: string;
  label: string;
  type?: "text" | "number" | "email" | "select" | "checkbox" | "textarea" | "date";
  options?: { value: string; label: string }[];
  required?: boolean;
  placeholder?: string;
  help?: string;
  /** Full width in the two-column grid. */
  wide?: boolean;
}

interface EntityFormProps {
  title: string;
  fields: FieldSpec[];
  initial?: Record<string, unknown>;
  submitLabel?: string;
  onSubmit: (values: Record<string, unknown>) => Promise<void>;
  onClose: () => void;
}

export function EntityForm({
  title,
  fields,
  initial = {},
  submitLabel = "Save",
  onSubmit,
  onClose,
}: EntityFormProps) {
  const [values, setValues] = useState<Record<string, unknown>>(() => {
    const seeded: Record<string, unknown> = {};

    for (const field of fields) {
      seeded[field.name] =
        initial[field.name] ?? (field.type === "checkbox" ? false : field.type === "number" ? 0 : "");
    }

    return seeded;
  });

  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  /**
   * The API names fields as the C# property does ("Sku", "CreditLimit"), while the form uses
   * camelCase. Matching case-insensitively means neither side has to know about the other's
   * convention — and an unmatched error still surfaces, as a form-level message, rather than
   * vanishing.
   */
  function errorsFor(name: string): string[] {
    const key = Object.keys(fieldErrors).find((k) => k.toLowerCase() === name.toLowerCase());
    return key ? fieldErrors[key] : [];
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();

    setBusy(true);
    setFieldErrors({});
    setError(null);

    try {
      await onSubmit(values);
    } catch (caught) {
      if (caught instanceof ApiError) {
        setFieldErrors(caught.fieldErrors);

        // A business-rule failure (a 400 from a DomainException, a 409 conflict) has no field to
        // hang off. It goes at the top of the form, where it cannot be missed.
        const unmatched = Object.keys(caught.fieldErrors).length === 0;
        setError(unmatched ? caught.message : null);
      } else {
        setError("Could not reach the server.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6">
      <form
        onSubmit={submit}
        className="max-h-[85vh] w-full max-w-2xl overflow-auto rounded-lg border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-950"
      >
        <h2 className="mb-5 text-lg font-semibold">{title}</h2>

        {error && (
          <p role="alert" className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          {fields.map((field) => {
            const errors = errorsFor(field.name);
            const invalid = errors.length > 0;

            return (
              <div
                key={field.name}
                className={`space-y-1.5 ${field.wide || field.type === "textarea" ? "sm:col-span-2" : ""}`}
              >
                <label htmlFor={field.name} className="block text-sm font-medium">
                  {field.label}
                  {field.required && <span className="ml-0.5 text-red-500">*</span>}
                </label>

                <FieldInput
                  field={field}
                  value={values[field.name]}
                  invalid={invalid}
                  onChange={(value) => setValues((current) => ({ ...current, [field.name]: value }))}
                />

                {field.help && !invalid && (
                  <p className="text-xs text-slate-500">{field.help}</p>
                )}

                {errors.map((message) => (
                  <p key={message} className="text-xs text-red-600 dark:text-red-400">
                    {message}
                  </p>
                ))}
              </div>
            );
          })}
        </div>

        <div className="mt-6 flex justify-end gap-2">
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
            {busy ? "Saving…" : submitLabel}
          </button>
        </div>
      </form>
    </div>
  );
}

function FieldInput({
  field,
  value,
  invalid,
  onChange,
}: {
  field: FieldSpec;
  value: unknown;
  invalid: boolean;
  onChange: (value: unknown) => void;
}) {
  const border = invalid
    ? "border-red-400 dark:border-red-600"
    : "border-slate-200 focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300";

  const base = `w-full rounded-md border bg-transparent px-3 py-2 text-sm outline-none ${border}`;

  if (field.type === "checkbox") {
    return (
      <input
        id={field.name}
        type="checkbox"
        checked={Boolean(value)}
        onChange={(e) => onChange(e.target.checked)}
        className="size-4 accent-slate-900 dark:accent-slate-100"
      />
    );
  }

  if (field.type === "select") {
    return (
      <select
        id={field.name}
        value={String(value ?? "")}
        onChange={(e) => onChange(e.target.value)}
        className={base}
      >
        <option value="">—</option>
        {field.options?.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    );
  }

  if (field.type === "textarea") {
    return (
      <textarea
        id={field.name}
        rows={3}
        value={String(value ?? "")}
        placeholder={field.placeholder}
        onChange={(e) => onChange(e.target.value)}
        className={base}
      />
    );
  }

  return (
    <input
      id={field.name}
      type={field.type ?? "text"}
      value={String(value ?? "")}
      placeholder={field.placeholder}
      // Money is kept as a string until it is sent, and parsed once at the boundary. Threading it
      // through React state as a float invites 0.1 + 0.2, which is not a rounding quirk an ERP can
      // wave away.
      step={field.type === "number" ? "any" : undefined}
      onChange={(e) =>
        onChange(field.type === "number" ? (e.target.value === "" ? 0 : Number(e.target.value)) : e.target.value)
      }
      className={base}
    />
  );
}
