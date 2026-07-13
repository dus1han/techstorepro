"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { EntityForm, type FieldSpec } from "@/components/data/entity-form";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { FEATURES, PermissionAction } from "@/types/identity";
import type { CurrencyDto, Supplier } from "@/types/catalog";

const TYPE_LABELS: Record<number, string> = { 1: "Local", 2: "Overseas", 3: "Repair vendor" };

export default function SuppliersPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);

  const currencies = useApiQuery<CurrencyDto[]>(["currencies"], "api/v1/currencies").data ?? [];

  const columns: Column<Supplier>[] = [
    { key: "code", header: "Code", render: (s) => <span className="font-mono text-xs">{s.code}</span> },
    {
      key: "name",
      header: "Name",
      render: (s) => (
        <div>
          <p className="font-medium">{s.name}</p>
          <p className="text-xs text-slate-500">
            {[s.country, s.phone, s.email].filter(Boolean).join(" · ") || "—"}
          </p>
        </div>
      ),
    },
    {
      key: "type",
      header: "Type",
      render: (s) => (
        <span
          className={`text-xs ${s.type === 2 ? "text-blue-600 dark:text-blue-400" : ""}`}
          title={s.type === 2 ? "Foreign currency, freight and customs apply" : undefined}
        >
          {TYPE_LABELS[s.type]}
        </span>
      ),
    },
    { key: "currency", header: "Currency", render: (s) => <span className="font-mono text-xs">{s.defaultCurrency}</span> },
    { key: "leadTime", header: "Lead time", align: "right", render: (s) => `${s.leadTimeDays}d` },
    {
      key: "balance",
      header: "Balance",
      align: "right",
      render: (s) => (
        <span className={s.balance > 0 ? "font-medium text-amber-600 dark:text-amber-400" : "text-slate-400"}>
          {s.balance.toFixed(2)}
        </span>
      ),
    },
  ];

  const fields: FieldSpec[] = [
    { name: "code", label: "Code", required: true },
    { name: "name", label: "Name", required: true },
    {
      name: "type",
      label: "Type",
      type: "select",
      required: true,
      options: [
        { value: "1", label: "Local" },
        { value: "2", label: "Overseas" },
        { value: "3", label: "Repair vendor" },
      ],
      help: "An overseas supplier needs a country — it drives customs and landed cost.",
    },
    {
      name: "defaultCurrency",
      label: "Currency",
      type: "select",
      required: true,
      options: currencies.map((c) => ({ value: c.code, label: `${c.code} — ${c.name}` })),
    },
    { name: "country", label: "Country" },
    { name: "email", label: "Email", type: "email" },
    { name: "phone", label: "Phone" },
    { name: "taxNumber", label: "VAT / tax number" },
    { name: "paymentTermDays", label: "Payment terms (days)", type: "number" },
    { name: "leadTimeDays", label: "Lead time (days)", type: "number" },
    { name: "address", label: "Address", type: "textarea" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Suppliers</h1>
          <p className="mt-1 text-sm text-slate-500">
            Local, overseas and repair vendors. Overseas suppliers drive the import and landed-cost flow.
          </p>
        </div>

        <Can feature={FEATURES.suppliers} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New supplier
          </button>
        </Can>
      </div>

      <DataTable
        queryKey={["suppliers"]}
        endpoint="api/v1/suppliers"
        columns={columns}
        rowKey={(s) => s.id}
        searchPlaceholder="Search name, code or email…"
        emptyMessage="No suppliers yet."
      />

      {creating && (
        <EntityForm
          title="New supplier"
          fields={fields}
          initial={{ type: "1", defaultCurrency: "AED", paymentTermDays: 0, leadTimeDays: 0 }}
          submitLabel="Create supplier"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await api.post("api/v1/suppliers", {
              token: accessToken!,
              body: { ...values, type: Number(values.type) },
            });

            setCreating(false);
            await client.invalidateQueries({ queryKey: ["suppliers"] });
          }}
        />
      )}
    </div>
  );
}
