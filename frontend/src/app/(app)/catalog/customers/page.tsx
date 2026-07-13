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
import type { Customer, PriceTierDto } from "@/types/catalog";

const TYPE_LABELS: Record<number, string> = { 1: "Walk-in", 2: "Individual", 3: "Corporate" };

export default function CustomersPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);

  const tiers = useApiQuery<PriceTierDto[]>(["price-tiers"], "api/v1/price-tiers").data ?? [];

  const columns: Column<Customer>[] = [
    { key: "code", header: "Code", render: (c) => <span className="font-mono text-xs">{c.code}</span> },
    {
      key: "name",
      header: "Name",
      render: (c) => (
        <div>
          <p className="font-medium">{c.name}</p>
          <p className="text-xs text-slate-500">
            {[c.companyName, c.phone, c.email].filter(Boolean).join(" · ") || "—"}
          </p>
        </div>
      ),
    },
    { key: "type", header: "Type", render: (c) => <span className="text-xs">{TYPE_LABELS[c.type]}</span> },
    { key: "tier", header: "Price tier", render: (c) => c.priceTierName ?? <span className="text-slate-400">Default</span> },
    {
      key: "creditLimit",
      header: "Credit limit",
      align: "right",
      render: (c) =>
        c.creditLimit === 0 ? <span className="text-xs text-slate-400">Cash only</span> : c.creditLimit.toFixed(2),
    },
    {
      key: "balance",
      header: "Balance",
      align: "right",
      render: (c) => (
        // Anything owed is highlighted: a receivable that blends into the page is a receivable
        // nobody chases.
        <span className={c.balance > 0 ? "font-medium text-amber-600 dark:text-amber-400" : "text-slate-400"}>
          {c.balance.toFixed(2)}
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
        { value: "1", label: "Walk-in" },
        { value: "2", label: "Individual" },
        { value: "3", label: "Corporate" },
      ],
      help: "A walk-in cannot be given credit — there is no account to bill.",
    },
    { name: "companyName", label: "Company name" },
    { name: "email", label: "Email", type: "email" },
    { name: "phone", label: "Phone" },
    { name: "taxNumber", label: "VAT / tax number" },
    {
      name: "priceTierId",
      label: "Price tier",
      type: "select",
      options: tiers.map((t) => ({ value: t.id, label: t.name })),
    },
    { name: "creditLimit", label: "Credit limit", type: "number" },
    { name: "paymentTermDays", label: "Payment terms (days)", type: "number" },
    { name: "address", label: "Address", type: "textarea" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Customers</h1>
          <p className="mt-1 text-sm text-slate-500">Walk-in, individual and corporate accounts.</p>
        </div>

        <Can feature={FEATURES.customers} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New customer
          </button>
        </Can>
      </div>

      <DataTable
        queryKey={["customers"]}
        endpoint="api/v1/customers"
        columns={columns}
        rowKey={(c) => c.id}
        searchPlaceholder="Search name, code, phone or email…"
        emptyMessage="No customers yet."
      />

      {creating && (
        <EntityForm
          title="New customer"
          fields={fields}
          initial={{ type: "2", creditLimit: 0, paymentTermDays: 0 }}
          submitLabel="Create customer"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await api.post("api/v1/customers", {
              token: accessToken!,
              body: {
                ...values,
                type: Number(values.type),
                priceTierId: values.priceTierId || null,
              },
            });

            setCreating(false);
            await client.invalidateQueries({ queryKey: ["customers"] });
          }}
        />
      )}
    </div>
  );
}
