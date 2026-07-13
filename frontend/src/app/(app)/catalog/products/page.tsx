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
import type { BrandDto, CategoryDto, Product, TaxRateDto } from "@/types/catalog";

const KIND_LABELS: Record<number, string> = { 1: "Product", 2: "Service", 3: "Spare part" };
const CONDITION_LABELS: Record<number, string> = { 1: "New", 2: "Refurbished" };
const TRACKING_LABELS: Record<number, string> = { 1: "None", 2: "Serial", 3: "Batch" };

export default function ProductsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [creating, setCreating] = useState(false);

  // These populate the dropdowns. A user who cannot view brands still gets the product list — the
  // query simply yields nothing, rather than the page failing whole.
  const categories = useApiQuery<CategoryDto[]>(["categories"], "api/v1/categories").data ?? [];
  const brands = useApiQuery<BrandDto[]>(["brands"], "api/v1/brands").data ?? [];
  const taxRates = useApiQuery<TaxRateDto[]>(["tax-rates"], "api/v1/tax-rates").data ?? [];

  const reload = () => client.invalidateQueries({ queryKey: ["products"] });

  const columns: Column<Product>[] = [
    { key: "sku", header: "SKU", sortBy: "sku", render: (p) => <span className="font-mono text-xs">{p.sku}</span> },
    {
      key: "name",
      header: "Name",
      sortBy: "name",
      render: (p) => (
        <div>
          <p className="font-medium">{p.name}</p>
          <p className="text-xs text-slate-500">
            {[p.brandName, p.model, p.categoryName].filter(Boolean).join(" · ") || "—"}
          </p>
        </div>
      ),
    },
    {
      key: "kind",
      header: "Type",
      render: (p) => (
        <div className="text-xs">
          <p>{KIND_LABELS[p.kind]}</p>
          <p className="text-slate-500">
            {CONDITION_LABELS[p.condition]}
            {p.trackingMode !== 1 && ` · ${TRACKING_LABELS[p.trackingMode]}`}
          </p>
        </div>
      ),
    },
    {
      key: "purchasePrice",
      header: "Cost",
      sortBy: "purchasePrice",
      align: "right",
      render: (p) => p.purchasePrice.toFixed(2),
    },
    {
      key: "sellingPrice",
      header: "Price",
      sortBy: "sellingPrice",
      align: "right",
      render: (p) => p.sellingPrice.toFixed(2),
    },
    {
      key: "margin",
      header: "Margin",
      align: "right",
      render: (p) =>
        p.marginPercent === null ? (
          <span className="text-slate-400">—</span>
        ) : (
          // A negative margin is shown in red rather than hidden. Selling below cost is a decision,
          // and the person making it should see it.
          <span className={p.marginPercent < 0 ? "text-red-600 dark:text-red-400" : ""}>
            {p.marginPercent.toFixed(1)}%
          </span>
        ),
    },
    {
      key: "isActive",
      header: "Status",
      render: (p) =>
        p.isActive ? (
          <span className="text-xs text-emerald-600 dark:text-emerald-400">Active</span>
        ) : (
          <span className="text-xs text-slate-400">Inactive</span>
        ),
    },
  ];

  const fields: FieldSpec[] = [
    { name: "itemCode", label: "Item code", required: true },
    { name: "sku", label: "SKU", required: true, help: "Cannot be changed later — printed labels depend on it." },
    { name: "name", label: "Name", required: true, wide: true },
    { name: "barcode", label: "Barcode" },
    { name: "model", label: "Model" },
    {
      name: "kind",
      label: "Type",
      type: "select",
      required: true,
      options: [
        { value: "1", label: "Product" },
        { value: "2", label: "Service" },
        { value: "3", label: "Spare part" },
      ],
    },
    {
      name: "condition",
      label: "Condition",
      type: "select",
      required: true,
      options: [
        { value: "1", label: "Brand new" },
        { value: "2", label: "Refurbished" },
      ],
    },
    {
      name: "trackingMode",
      label: "Tracking",
      type: "select",
      required: true,
      help: "Serial tracking is what makes a warranty claim answerable later. It cannot be changed afterwards.",
      options: [
        { value: "1", label: "None" },
        { value: "2", label: "Serial" },
        { value: "3", label: "Batch" },
      ],
    },
    {
      name: "categoryId",
      label: "Category",
      type: "select",
      options: categories.map((c) => ({ value: c.id, label: c.name })),
    },
    {
      name: "brandId",
      label: "Brand",
      type: "select",
      options: brands.map((b) => ({ value: b.id, label: b.name })),
    },
    { name: "unit", label: "Unit", required: true },
    { name: "purchasePrice", label: "Purchase price", type: "number", required: true },
    { name: "sellingPrice", label: "Selling price", type: "number", required: true },
    {
      name: "taxRateId",
      label: "Tax rate",
      type: "select",
      options: taxRates.map((t) => ({ value: t.id, label: `${t.name} (${t.percent}%)` })),
    },
    { name: "warrantyMonths", label: "Warranty (months)", type: "number" },
    { name: "reorderLevel", label: "Reorder level", type: "number" },
    { name: "description", label: "Description", type: "textarea" },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Products</h1>
          <p className="mt-1 text-sm text-slate-500">
            Products, services and spare parts. Serial tracking is set once, at creation.
          </p>
        </div>

        <Can feature={FEATURES.products} action={PermissionAction.Create}>
          <button
            onClick={() => setCreating(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            New product
          </button>
        </Can>
      </div>

      <DataTable
        queryKey={["products"]}
        endpoint="api/v1/products"
        columns={columns}
        rowKey={(p) => p.id}
        searchPlaceholder="Search name, SKU, barcode or model…"
        emptyMessage="No products yet."
        actions={(product) => (
          <Can feature={FEATURES.products} action={PermissionAction.Delete}>
            <button
              onClick={() => void remove(product)}
              className="rounded-md border border-slate-200 px-2.5 py-1 text-xs text-red-600 hover:bg-red-50 dark:border-slate-700 dark:hover:bg-red-950"
            >
              Delete
            </button>
          </Can>
        )}
      />

      {creating && (
        <EntityForm
          title="New product"
          fields={fields}
          initial={{ kind: "1", condition: "1", trackingMode: "1", unit: "each" }}
          submitLabel="Create product"
          onClose={() => setCreating(false)}
          onSubmit={async (values) => {
            await api.post("api/v1/products", {
              token: accessToken!,
              body: {
                ...values,
                // The selects hand back strings; the API expects the numeric enum.
                kind: Number(values.kind),
                condition: Number(values.condition),
                trackingMode: Number(values.trackingMode),
                categoryId: values.categoryId || null,
                brandId: values.brandId || null,
                taxRateId: values.taxRateId || null,
                barcode: values.barcode || null,
              },
            });

            setCreating(false);
            await reload();
          }}
        />
      )}
    </div>
  );

  async function remove(product: Product) {
    const reason = window.prompt(`Why are you deleting "${product.name}"?`);
    if (!reason || !accessToken) return;

    await api.delete(`api/v1/products/${product.id}`, { token: accessToken, query: { reason } });
    await reload();
  }
}
