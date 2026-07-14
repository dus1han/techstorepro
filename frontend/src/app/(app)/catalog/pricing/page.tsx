"use client";

import { useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { EntityForm, type FieldSpec } from "@/components/data/entity-form";
import { api, ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import type { FinancialAccount } from "@/features/finance/types";
import { PAYMENT_METHOD_KIND_LABELS, PaymentMethodKind } from "@/features/sales/types";
import { FEATURES, PermissionAction } from "@/types/identity";
import type {
  BrandDto,
  CategoryDto,
  DiscountDto,
  PaymentMethodDto,
  PriceTierDto,
  TaxRateDto,
} from "@/types/catalog";

type Dialog = "category" | "brand" | "taxRate" | "tier" | "discount" | "paymentMethod" | null;

const KEYS = [
  ["categories"],
  ["brands"],
  ["tax-rates"],
  ["price-tiers"],
  ["discounts"],
  ["payment-methods"],
];

/**
 * The rule tables behind pricing: categories, brands, tax rates, price tiers and discounts.
 *
 * Tax rates and discounts are effective-dated. Editing one never rewrites the past — a new version
 * is written from today, and yesterday's invoice still resolves yesterday's rate.
 */
export default function PricingPage() {
  const { accessToken, can } = useAuth();
  const client = useQueryClient();

  const [dialog, setDialog] = useState<Dialog>(null);
  const [editingMethod, setEditingMethod] = useState<PaymentMethodDto | null>(null);

  const categories = useApiQuery<CategoryDto[]>(["categories"], "api/v1/categories").data ?? [];
  const brands = useApiQuery<BrandDto[]>(["brands"], "api/v1/brands").data ?? [];
  const taxRates = useApiQuery<TaxRateDto[]>(["tax-rates"], "api/v1/tax-rates").data ?? [];
  const tiers = useApiQuery<PriceTierDto[]>(["price-tiers"], "api/v1/price-tiers").data ?? [];
  const discounts = useApiQuery<DiscountDto[]>(["discounts"], "api/v1/discounts").data ?? [];
  const methods =
    useApiQuery<PaymentMethodDto[]>(["payment-methods"], "api/v1/payment-methods").data ?? [];

  /**
   * The accounts a payment method can point at (P7).
   *
   * Gated on the permission rather than fetched blindly: the accounts endpoint answers to
   * `finance.accounts`, and somebody who may edit payment methods but not see the shop's cash position
   * would otherwise be shown a 403 for a picker they did not ask for.
   */
  const accounts =
    useApiQuery<FinancialAccount[]>(["finance", "accounts"], "api/v1/finance/accounts", undefined, {
      enabled: can(FEATURES.accounts, PermissionAction.View),
    }).data ?? [];

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

  async function reloadMethods() {
    setDialog(null);
    setEditingMethod(null);

    await client.invalidateQueries({ queryKey: ["payment-methods"] });
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
        title="Payment methods"
        feature={FEATURES.paymentMethods}
        onAdd={() => setDialog("paymentMethod")}
        empty={methods.length === 0}
        emptyText="No payment methods. Nothing can be tendered until one exists."
      >
        {methods.map((method) => (
          <Row
            key={method.id}
            title={`${method.name} — ${PAYMENT_METHOD_KIND_LABELS[method.kind as PaymentMethodKind]}`}
            subtitle={[
              // The account is the line worth reading. A method that moves money and names no account is
              // refused at the till — and the shop only finds out with a customer standing in front of it.
              method.kind === PaymentMethodKind.StoreCredit
                ? "No account — store credit is a voucher, not money"
                : (method.financialAccountName ?? "No account — payments through it will be refused"),
              method.requiresReference ? "Reference required" : null,
            ]
              .filter(Boolean)
              .join(" · ")}
            badges={[method.isInForce ? "in force" : "not in force"]}
            action={
              <Can feature={FEATURES.paymentMethods} action={PermissionAction.Edit}>
                <button
                  onClick={() => setEditingMethod(method)}
                  className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                >
                  Edit
                </button>
              </Can>
            }
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

      {(dialog === "paymentMethod" || editingMethod) && (
        <PaymentMethodDialog
          method={editingMethod}
          accounts={accounts}
          token={accessToken!}
          onClose={() => {
            setDialog(null);
            setEditingMethod(null);
          }}
          onSaved={reloadMethods}
        />
      )}
    </div>
  );
}

/**
 * A way for money to arrive, and where it lands when it does (§23, and P7's account behind it).
 *
 * Hand-rolled rather than an `<EntityForm>` because of one rule the picker has to obey as the kind is
 * chosen: <b>store credit must have no account.</b> It is a voucher the shop already owes the customer,
 * not money moving — pointing it at the till would mean the drawer filled up every time somebody spent a
 * credit note, and the cash position would report notes that nobody had handed over. Every other kind wants
 * an account, and a payment through one that has none is refused at the till.
 */
function PaymentMethodDialog({
  method,
  accounts,
  token,
  onClose,
  onSaved,
}: {
  method: PaymentMethodDto | null;
  accounts: FinancialAccount[];
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [name, setName] = useState(method?.name ?? "");
  const [kind, setKind] = useState<PaymentMethodKind>(
    (method?.kind as PaymentMethodKind) ?? PaymentMethodKind.Cash,
  );
  const [requiresReference, setRequiresReference] = useState(method?.requiresReference ?? false);
  const [financialAccountId, setFinancialAccountId] = useState(method?.financialAccountId ?? "");
  const [isActive, setIsActive] = useState(method?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const storeCredit = kind === PaymentMethodKind.StoreCredit;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6">
      <form
        onSubmit={async (event) => {
          event.preventDefault();

          setBusy(true);
          setError(null);

          // Not merely hidden when the kind is store credit — cleared. A method switched to store credit
          // while an account was picked would otherwise carry the account with it, invisibly.
          const accountId = storeCredit ? null : financialAccountId || null;

          try {
            if (method) {
              await api.put(`api/v1/payment-methods/${method.id}`, {
                token,
                body: {
                  id: method.id,
                  name,
                  kind,
                  requiresReference,
                  validTo: method.validTo,
                  isActive,
                  financialAccountId: accountId,
                },
              });
            } else {
              await api.post("api/v1/payment-methods", {
                token,
                body: { name, kind, requiresReference, validFrom: null, financialAccountId: accountId },
              });
            }

            await onSaved();
          } catch (e) {
            setError(e instanceof ApiError ? e.message : "The payment method was not saved.");
          } finally {
            setBusy(false);
          }
        }}
        className="w-full max-w-lg space-y-5 rounded-lg border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-950"
      >
        <h2 className="text-lg font-semibold">{method ? `Edit ${method.name}` : "New payment method"}</h2>

        {error && (
          <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {error}
          </p>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block space-y-1">
            <span className="text-sm font-medium">Name</span>
            <input value={name} onChange={(e) => setName(e.target.value)} className={INPUT} />
          </label>

          <label className="block space-y-1">
            <span className="text-sm font-medium">Kind</span>
            <select
              value={String(kind)}
              onChange={(e) => setKind(Number(e.target.value) as PaymentMethodKind)}
              className={INPUT}
            >
              {Object.entries(PAYMENT_METHOD_KIND_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          <label className="block space-y-1 sm:col-span-2">
            <span className="text-sm font-medium">Money tendered this way lands in</span>
            <select
              value={storeCredit ? "" : financialAccountId}
              disabled={storeCredit}
              onChange={(e) => setFinancialAccountId(e.target.value)}
              className={`${INPUT} disabled:opacity-50`}
            >
              <option value="">—</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name} ({account.currencyCode})
                </option>
              ))}
            </select>

            <span className="block text-xs text-slate-500">
              {storeCredit
                ? "Store credit moves no money — it is a voucher the shop already owes. It takes no account, and the server refuses one."
                : "Without an account, a payment through this method is refused: the money would arrive nowhere and be missed by nobody."}
            </span>
          </label>

          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={requiresReference}
              onChange={(e) => setRequiresReference(e.target.checked)}
              className="size-4 accent-slate-900 dark:accent-slate-100"
            />
            Reference required
          </label>

          {method && (
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="size-4 accent-slate-900 dark:accent-slate-100"
              />
              In use
            </label>
          )}
        </div>

        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-slate-200 px-3 py-1.5 text-sm hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={busy || !name.trim()}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </div>
  );
}

const INPUT =
  "w-full rounded-md border border-slate-200 bg-transparent px-3 py-2 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300";

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
  action,
}: {
  title: string;
  subtitle: string;
  badges?: (string | null)[];
  action?: ReactNode;
}) {
  return (
    <div className="flex items-center justify-between gap-4 p-4">
      <div className="min-w-0">
        <p className="text-sm font-medium">{title}</p>
        <p className="truncate text-xs text-slate-500">{subtitle}</p>
      </div>

      <div className="flex shrink-0 items-center gap-1.5">
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

        {action}
      </div>
    </div>
  );
}
