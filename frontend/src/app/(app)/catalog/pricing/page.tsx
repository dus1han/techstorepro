"use client";

import { useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { EntityForm, type FieldSpec } from "@/components/data/entity-form";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { FEATURES, PermissionAction } from "@/types/identity";
import type { BrandDto, CategoryDto, DiscountDto, PriceTierDto, TaxRateDto } from "@/types/catalog";

type Dialog = "category" | "brand" | "taxRate" | "tier" | "discount" | null;

const KEYS = [["categories"], ["brands"], ["tax-rates"], ["price-tiers"], ["discounts"]];

/**
 * The rule tables behind pricing: categories, brands, tax rates, price tiers and discounts.
 *
 * Tax rates and discounts are effective-dated. Editing one never rewrites the past — a new version
 * is written from today, and yesterday's invoice still resolves yesterday's rate.
 */
export default function PricingPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [dialog, setDialog] = useState<Dialog>(null);

  const categories = useApiQuery<CategoryDto[]>(["categories"], "api/v1/categories").data ?? [];
  const brands = useApiQuery<BrandDto[]>(["brands"], "api/v1/brands").data ?? [];
  const taxRates = useApiQuery<TaxRateDto[]>(["tax-rates"], "api/v1/tax-rates").data ?? [];
  const tiers = useApiQuery<PriceTierDto[]>(["price-tiers"], "api/v1/price-tiers").data ?? [];
  const discounts = useApiQuery<DiscountDto[]>(["discounts"], "api/v1/discounts").data ?? [];

  /**
   * Errors are deliberately NOT caught here: <EntityForm> catches the ApiError and binds its
   * field-level messages onto the inputs. Swallowing it here would leave the user staring at a
   * form that quietly did nothing.
   */
  async function post(path: string, body: unknown) {
    await api.post(path, { token: accessToken!, body });

    setDialog(null);
    await Promise.all(KEYS.map((key) => client.invalidateQueries({ queryKey: key })));
  }

  return (
    <div className="mx-auto max-w-5xl space-y-8">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Pricing &amp; classification</h1>
        <p className="mt-1 text-sm text-slate-500">
          Tax rates and discounts are effective-dated: changing one takes effect from now, and never
          restates a document already raised.
        </p>
      </div>

      <Section
        title="Tax rates"
        feature={FEATURES.taxRates}
        onAdd={() => setDialog("taxRate")}
        empty={taxRates.length === 0}
        emptyText="No tax rates. Products will have no tax until one exists."
      >
        {taxRates.map((rate) => (
          <Row
            key={rate.id}
            title={`${rate.name} — ${rate.percent}%`}
            subtitle={`From ${new Date(rate.validFrom).toLocaleDateString()}${
              rate.validTo ? ` to ${new Date(rate.validTo).toLocaleDateString()}` : ""
            }`}
            badges={[
              rate.isDefault ? "default" : null,
              rate.isInForce ? "in force" : "not in force",
            ]}
          />
        ))}
      </Section>

      <Section
        title="Price tiers"
        feature={FEATURES.pricing}
        onAdd={() => setDialog("tier")}
        empty={tiers.length === 0}
        emptyText="No tiers. Every customer pays the product's own selling price."
      >
        {tiers.map((tier) => (
          <Row
            key={tier.id}
            title={tier.name}
            subtitle={`${tier.customerCount} customer${tier.customerCount === 1 ? "" : "s"}`}
            badges={[tier.isDefault ? "default" : null]}
          />
        ))}
      </Section>

      <Section
        title="Discounts"
        feature={FEATURES.discounts}
        onAdd={() => setDialog("discount")}
        empty={discounts.length === 0}
        emptyText="No discount rules."
      >
        {discounts.map((discount) => (
          <Row
            key={discount.id}
            title={`${discount.name} — ${
              discount.method === 1 ? `${discount.value}%` : discount.value.toFixed(2)
            }`}
            subtitle={[
              discount.productName ? `Product: ${discount.productName}` : "All products",
              discount.customerName ? `Customer: ${discount.customerName}` : "All customers",
              discount.maxValue !== null ? `Approval above ${discount.maxValue}` : "No approval ceiling",
            ].join(" · ")}
            badges={[discount.isInForce ? "in force" : "not in force"]}
          />
        ))}
      </Section>

      <Section
        title="Categories"
        feature={FEATURES.categories}
        onAdd={() => setDialog("category")}
        empty={categories.length === 0}
        emptyText="No categories."
      >
        {categories.map((category) => (
          <Row
            key={category.id}
            title={category.parentName ? `${category.parentName} → ${category.name}` : category.name}
            subtitle={`${category.productCount} product${category.productCount === 1 ? "" : "s"}`}
          />
        ))}
      </Section>

      <Section
        title="Brands"
        feature={FEATURES.brands}
        onAdd={() => setDialog("brand")}
        empty={brands.length === 0}
        emptyText="No brands."
      >
        {brands.map((brand) => (
          <Row
            key={brand.id}
            title={brand.name}
            subtitle={`${brand.productCount} product${brand.productCount === 1 ? "" : "s"}`}
          />
        ))}
      </Section>

      {dialog === "taxRate" && (
        <EntityForm
          title="New tax rate"
          fields={[
            { name: "name", label: "Name", required: true },
            { name: "percent", label: "Percent", type: "number", required: true },
            { name: "isDefault", label: "Use as the default rate", type: "checkbox", wide: true },
          ]}
          onClose={() => setDialog(null)}
          onSubmit={(v) => post("api/v1/tax-rates", { ...v, percent: Number(v.percent) })}
        />
      )}

      {dialog === "tier" && (
        <EntityForm
          title="New price tier"
          fields={[
            { name: "name", label: "Name", required: true, placeholder: "Wholesale" },
            { name: "isDefault", label: "Use as the default tier", type: "checkbox", wide: true },
          ]}
          onClose={() => setDialog(null)}
          onSubmit={(v) => post("api/v1/price-tiers", v)}
        />
      )}

      {dialog === "discount" && (
        <EntityForm
          title="New discount"
          fields={discountFields}
          initial={{ method: "1" }}
          onClose={() => setDialog(null)}
          onSubmit={(v) =>
            post("api/v1/discounts", {
              ...v,
              method: Number(v.method),
              value: Number(v.value),
              maxValue: v.maxValue ? Number(v.maxValue) : null,
              productId: null,
              customerId: null,
            })
          }
        />
      )}

      {dialog === "category" && (
        <EntityForm
          title="New category"
          fields={[
            { name: "name", label: "Name", required: true },
            {
              name: "parentId",
              label: "Parent category",
              type: "select",
              options: categories.map((c) => ({ value: c.id, label: c.name })),
            },
          ]}
          onClose={() => setDialog(null)}
          onSubmit={(v) => post("api/v1/categories", { ...v, parentId: v.parentId || null })}
        />
      )}

      {dialog === "brand" && (
        <EntityForm
          title="New brand"
          fields={[{ name: "name", label: "Name", required: true, wide: true }]}
          onClose={() => setDialog(null)}
          onSubmit={(v) => post("api/v1/brands", v)}
        />
      )}
    </div>
  );
}

const discountFields: FieldSpec[] = [
  { name: "name", label: "Name", required: true },
  {
    name: "method",
    label: "Method",
    type: "select",
    required: true,
    options: [
      { value: "1", label: "Percentage" },
      { value: "2", label: "Fixed amount" },
    ],
  },
  { name: "value", label: "Value", type: "number", required: true },
  {
    name: "maxValue",
    label: "Approval ceiling",
    type: "number",
    help: "Above this, a manager must approve. Leave blank for no ceiling.",
  },
];

function Section({
  title,
  feature,
  onAdd,
  empty,
  emptyText,
  children,
}: {
  title: string;
  feature: string;
  onAdd: () => void;
  empty: boolean;
  emptyText: string;
  children: ReactNode;
}) {
  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium uppercase tracking-wider text-slate-400">{title}</h2>

        <Can feature={feature} action={PermissionAction.Create}>
          <button
            onClick={onAdd}
            className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
          >
            Add
          </button>
        </Can>
      </div>

      <div className="divide-y divide-slate-100 rounded-lg border border-slate-200 dark:divide-slate-800 dark:border-slate-800">
        {empty ? <p className="p-4 text-sm text-slate-500">{emptyText}</p> : children}
      </div>
    </section>
  );
}

function Row({
  title,
  subtitle,
  badges = [],
}: {
  title: string;
  subtitle: string;
  badges?: (string | null)[];
}) {
  return (
    <div className="flex items-center justify-between gap-4 p-4">
      <div className="min-w-0">
        <p className="text-sm font-medium">{title}</p>
        <p className="truncate text-xs text-slate-500">{subtitle}</p>
      </div>

      <div className="flex shrink-0 gap-1.5">
        {badges.filter(Boolean).map((badge) => (
          <span
            key={badge}
            className={`rounded px-1.5 py-0.5 text-xs ${
              badge === "in force" || badge === "default"
                ? "bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-400"
                : "bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400"
            }`}
          >
            {badge}
          </span>
        ))}
      </div>
    </div>
  );
}
