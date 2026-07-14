"use client";

import { useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { sellAtCounter, type TenderInput } from "@/features/sales/api";
import { lineNet, lineTax, round } from "@/features/sales/money-preview";
import { Money } from "@/features/sales/components/money";
import type { CounterSaleResult, PaymentMethod } from "@/features/sales/types";
import type { PagedResult } from "@/types/api";
import { TrackingMode, type Customer, type Product, type TaxRateDto } from "@/types/catalog";
import type { Branch } from "@/types/identity";

interface WarehouseOption {
  id: string;
  name: string;
}

interface CartLine {
  key: string;
  product: Product;
  quantity: number;
  discountPercent: number;
  /** One per unit, for a serial-tracked product. The delivery binds these to the sale. */
  serials: string[];
  taxPercent: number;
}

/**
 * The till (requirements §22).
 *
 * **Barcode-first.** The scan box holds focus and every scan adds a line; the cashier's hands never
 * have to leave the scanner. Everything else on the screen is a fallback for when the barcode is
 * missing or rubbed off.
 *
 * The sale posts as **one call** — goods out, bill raised, money taken, in a single transaction. If the
 * card is declined the laptop is still in stock and no invoice is chasing anybody. The request carries an
 * `Idempotency-Key`, so the impatient second tap gets the first tap's receipt rather than selling the
 * laptop twice.
 */
export function PosTerminal() {
  const { accessToken } = useAuth();
  const client = useQueryClient();
  const scanRef = useRef<HTMLInputElement>(null);

  const [scan, setScan] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [customerId, setCustomerId] = useState("");
  const [branchId, setBranchId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [tender, setTender] = useState<Record<string, string>>({});
  const [receipt, setReceipt] = useState<CounterSaleResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 200 }).data?.items ?? [];
  const customers =
    useApiQuery<PagedResult<Customer>>(["customers"], "api/v1/customers", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];
  const methods =
    useApiQuery<PagedResult<PaymentMethod>>(["payment-methods"], "api/v1/payment-methods").data?.items ?? [];
  const taxRates =
    useApiQuery<PagedResult<TaxRateDto>>(["tax-rates"], "api/v1/tax-rates").data?.items ?? [];

  const totals = useMemo(() => {
    const net = cart.reduce(
      (sum, line) => sum + lineNet(line.quantity, line.product.sellingPrice, line.discountPercent),
      0,
    );

    const tax = cart.reduce((sum, line) => {
      const lineAmount = lineNet(line.quantity, line.product.sellingPrice, line.discountPercent);

      return sum + lineTax(lineAmount, line.taxPercent);
    }, 0);

    return { net: round(net), tax: round(tax), total: round(net + tax) };
  }, [cart]);

  const tendered = round(
    Object.values(tender).reduce((sum, amount) => sum + (Number(amount) || 0), 0),
  );

  const change = round(Math.max(0, tendered - totals.total));
  const short = round(Math.max(0, totals.total - tendered));

  const defaultBranch = branchId || branches[0]?.id || "";
  const defaultWarehouse = warehouseId || warehouses[0]?.id || "";

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_22rem]">
      <div className="space-y-4">
        <div className="rounded-lg border border-slate-200 p-4 dark:border-slate-800">
          <label htmlFor="scan" className="text-xs font-medium uppercase tracking-wider text-slate-400">
            Scan or type a barcode, SKU or name
          </label>

          <input
            id="scan"
            ref={scanRef}
            value={scan}
            onChange={(e) => setScan(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                addByScan();
              }
            }}
            autoFocus
            placeholder="Scan…"
            className="mt-2 w-full rounded-md border border-slate-200 bg-transparent px-3 py-2 font-mono text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300"
          />

          {scan.trim().length > 1 && matches(scan).length > 0 && (
            <ul className="mt-2 max-h-40 overflow-y-auto text-sm">
              {matches(scan).slice(0, 6).map((product) => (
                <li key={product.id}>
                  <button
                    onClick={() => add(product)}
                    className="flex w-full items-center justify-between rounded-md px-2 py-1.5 text-left hover:bg-slate-100 dark:hover:bg-slate-800"
                  >
                    <span>
                      <span className="font-mono text-xs text-slate-500">{product.sku}</span> {product.name}
                    </span>
                    <Money amount={product.sellingPrice} />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-800">
          <table className="w-full text-sm">
            <thead className="border-b border-slate-200 bg-slate-50 text-left dark:border-slate-800 dark:bg-slate-900">
              <tr>
                <th className="px-3 py-2 font-medium">Item</th>
                <th className="px-3 py-2 font-medium">Qty</th>
                <th className="px-3 py-2 font-medium">Disc %</th>
                <th className="px-3 py-2 text-right font-medium">Line</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>

            <tbody>
              {cart.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-3 py-10 text-center text-slate-500">
                    Nothing scanned yet.
                  </td>
                </tr>
              )}

              {cart.map((line) => {
                const net = lineNet(line.quantity, line.product.sellingPrice, line.discountPercent);
                const needsSerials = line.product.trackingMode === TrackingMode.Serial;
                const serialsShort = needsSerials && line.serials.length !== line.quantity;

                return (
                  <tr key={line.key} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                    <td className="px-3 py-2">
                      <p className="font-medium">{line.product.name}</p>
                      <p className="text-xs text-slate-500">
                        <Money amount={line.product.sellingPrice} /> · {line.taxPercent}% tax
                      </p>

                      {needsSerials && (
                        <div className="mt-1.5">
                          <input
                            value={line.serials.join(", ")}
                            onChange={(e) =>
                              update(line.key, {
                                serials: e.target.value
                                  .split(",")
                                  .map((s) => s.trim())
                                  .filter(Boolean),
                              })
                            }
                            placeholder="Scan each serial, comma separated"
                            aria-label={`Serial numbers for ${line.product.name}`}
                            className={`w-64 rounded-md border bg-transparent px-2 py-1 font-mono text-xs outline-none ${
                              serialsShort
                                ? "border-amber-500"
                                : "border-slate-200 dark:border-slate-700"
                            }`}
                          />
                          {serialsShort && (
                            <p className="mt-1 text-xs text-amber-600 dark:text-amber-400">
                              {line.serials.length} of {line.quantity} serials — this is the machine that
                              goes out of the door, and the warranty follows it.
                            </p>
                          )}
                        </div>
                      )}
                    </td>

                    <td className="px-3 py-2">
                      <input
                        type="number"
                        min={1}
                        value={line.quantity}
                        onChange={(e) => update(line.key, { quantity: Math.max(1, Number(e.target.value)) })}
                        aria-label={`Quantity of ${line.product.name}`}
                        className="w-16 rounded-md border border-slate-200 bg-transparent px-2 py-1 text-sm tabular-nums outline-none dark:border-slate-700"
                      />
                    </td>

                    <td className="px-3 py-2">
                      <input
                        type="number"
                        min={0}
                        max={100}
                        value={line.discountPercent}
                        onChange={(e) =>
                          update(line.key, {
                            discountPercent: Math.min(100, Math.max(0, Number(e.target.value))),
                          })
                        }
                        aria-label={`Discount on ${line.product.name}`}
                        className="w-16 rounded-md border border-slate-200 bg-transparent px-2 py-1 text-sm tabular-nums outline-none dark:border-slate-700"
                      />
                    </td>

                    <td className="px-3 py-2 text-right tabular-nums">
                      <Money amount={net} />
                    </td>

                    <td className="px-3 py-2 text-right">
                      <button
                        onClick={() => setCart((lines) => lines.filter((l) => l.key !== line.key))}
                        aria-label={`Remove ${line.product.name}`}
                        className="rounded-md border border-slate-200 px-2 py-0.5 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      <aside className="space-y-4">
        <div className="space-y-3 rounded-lg border border-slate-200 p-4 dark:border-slate-800">
          <Select label="Customer" value={customerId} onChange={setCustomerId} options={customers.map((c) => ({ value: c.id, label: c.name }))} />
          <Select label="Branch" value={defaultBranch} onChange={setBranchId} options={branches.map((b) => ({ value: b.id, label: b.name }))} />
          <Select label="Warehouse" value={defaultWarehouse} onChange={setWarehouseId} options={warehouses.map((w) => ({ value: w.id, label: w.name }))} />
        </div>

        <div className="space-y-2 rounded-lg border border-slate-200 p-4 text-sm dark:border-slate-800">
          <Row label="Net" value={<Money amount={totals.net} />} />
          {/* Prices are tax-exclusive (D7): the tax is added here, on the discounted net. */}
          <Row label="Tax" value={<Money amount={totals.tax} />} />
          <div className="border-t border-slate-200 pt-2 dark:border-slate-700">
            <Row label={<span className="font-semibold">Total</span>} value={<span className="text-lg font-semibold"><Money amount={totals.total} /></span>} />
          </div>
        </div>

        <div className="space-y-3 rounded-lg border border-slate-200 p-4 dark:border-slate-800">
          <p className="text-xs font-medium uppercase tracking-wider text-slate-400">Tender</p>

          {methods.map((method) => (
            <div key={method.id} className="flex items-center gap-2">
              <label htmlFor={`tender-${method.id}`} className="w-28 shrink-0 text-sm">
                {method.name}
              </label>
              <input
                id={`tender-${method.id}`}
                type="number"
                min={0}
                step="0.01"
                value={tender[method.id] ?? ""}
                onChange={(e) => setTender((t) => ({ ...t, [method.id]: e.target.value }))}
                placeholder="0.00"
                className="w-full rounded-md border border-slate-200 bg-transparent px-2 py-1 text-sm tabular-nums outline-none dark:border-slate-700"
              />
            </div>
          ))}

          {/* Cash is over-tendered constantly: the customer hands over 200 for a 168 sale. */}
          <div className="space-y-1 border-t border-slate-200 pt-2 text-sm dark:border-slate-700">
            <Row label="Tendered" value={<Money amount={tendered} />} />
            {change > 0 && <Row label="Change" value={<span className="font-semibold text-emerald-600 dark:text-emerald-400"><Money amount={change} /></span>} />}
            {short > 0 && <Row label="Short" value={<span className="font-semibold text-red-600 dark:text-red-400"><Money amount={short} /></span>} />}
          </div>

          <button
            onClick={() => void complete()}
            disabled={!canComplete()}
            className="w-full rounded-md bg-slate-900 px-3 py-2 text-sm font-semibold text-white disabled:opacity-40 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Taking payment…" : "Complete sale"}
          </button>

          {short > 0 && cart.length > 0 && (
            <p className="text-xs text-slate-500">
              A counter sale is paid for at the counter. Take the balance, or raise it as a credit sale
              against the customer&apos;s account.
            </p>
          )}

          {error && (
            <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-xs text-red-700 dark:bg-red-950 dark:text-red-300">
              {error}
            </p>
          )}
        </div>

        {receipt && (
          <div className="space-y-1 rounded-lg border border-emerald-300 bg-emerald-50 p-4 text-sm dark:border-emerald-900 dark:bg-emerald-950">
            <p className="font-semibold text-emerald-900 dark:text-emerald-200">Sold — {receipt.invoiceNumber}</p>
            {/* The server's figures, not the preview's. It is the authority on what was charged. */}
            <Row label="Total" value={<Money amount={receipt.total} />} />
            <Row label="Paid" value={<Money amount={receipt.paid} />} />
            <Row label="Change" value={<Money amount={receipt.change} />} />
          </div>
        )}
      </aside>
    </div>
  );

  function matches(term: string) {
    const q = term.trim().toLowerCase();

    return products.filter(
      (p) =>
        p.barcode?.toLowerCase() === q
        || p.sku.toLowerCase().includes(q)
        || p.name.toLowerCase().includes(q),
    );
  }

  function taxPercentFor(product: Product) {
    const own = taxRates.find((r) => r.id === product.taxRateId);
    if (own) return own.percent;

    // No rate on the product: fall back to the company's default, exactly as the server does. A company
    // with no rates configured at all sells at zero tax, and that is a legitimate answer, not an error.
    return taxRates.find((r) => r.isDefault)?.percent ?? 0;
  }

  function addByScan() {
    const found = matches(scan);

    if (found.length === 0) return;

    add(found[0]);
  }

  function add(product: Product) {
    setCart((lines) => {
      const existing = lines.find(
        (l) => l.product.id === product.id && product.trackingMode !== TrackingMode.Serial,
      );

      // Scanning the same cable twice bumps the quantity. Scanning two laptops cannot: each is a
      // distinct machine with its own serial, so each gets its own line.
      if (existing) {
        return lines.map((l) =>
          l.key === existing.key ? { ...l, quantity: l.quantity + 1 } : l,
        );
      }

      return [
        ...lines,
        {
          key: crypto.randomUUID(),
          product,
          quantity: 1,
          discountPercent: 0,
          serials: [],
          taxPercent: taxPercentFor(product),
        },
      ];
    });

    setScan("");
    scanRef.current?.focus();
  }

  function update(key: string, patch: Partial<CartLine>) {
    setCart((lines) => lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  function canComplete() {
    if (busy || cart.length === 0 || !customerId || !defaultBranch || !defaultWarehouse) return false;

    // Every serial-tracked unit must be identified. Without it the shop cannot say which machine it
    // sold, and a warranty claim two years from now has nothing to follow back.
    const serialsComplete = cart.every(
      (l) => l.product.trackingMode !== TrackingMode.Serial || l.serials.length === l.quantity,
    );

    return serialsComplete && tendered >= totals.total;
  }

  async function complete() {
    if (!accessToken || !canComplete()) return;

    setBusy(true);
    setError(null);

    const methodsUsed: TenderInput[] = methods
      .map((method) => ({
        paymentMethodId: method.id,
        amount: Number(tender[method.id] ?? 0),
        reference: method.requiresReference ? "counter" : null,
      }))
      .filter((t) => t.amount > 0);

    try {
      const result = await sellAtCounter(
        { token: accessToken },
        {
          customerId,
          branchId: defaultBranch,
          warehouseId: defaultWarehouse,
          lines: cart.map((l) => ({
            productId: l.product.id,
            quantity: l.quantity,
            serialNumbers: l.serials.length > 0 ? l.serials : null,
            discountPercent: l.discountPercent,
          })),
          methods: methodsUsed,
        },
      );

      setReceipt(result);
      setCart([]);
      setTender({});

      // Stock moved, so every stock-facing list is now stale.
      await Promise.all([
        client.invalidateQueries({ queryKey: ["stock"] }),
        client.invalidateQueries({ queryKey: ["sales-invoices"] }),
        client.invalidateQueries({ queryKey: ["deliveries"] }),
        client.invalidateQueries({ queryKey: ["customer-payments"] }),
      ]);

      scanRef.current?.focus();
    } catch (e) {
      // The server refused, which means nothing happened: the goods are still in stock and there is no
      // invoice chasing anybody. Show what it said — the messages name the actual problem.
      setError(e instanceof ApiError ? e.message : "The sale could not be completed.");
    } finally {
      setBusy(false);
    }
  }
}

function Row({ label, value }: { label: React.ReactNode; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-slate-500">{label}</span>
      {value}
    </div>
  );
}

function Select({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
}) {
  const id = `pos-${label.toLowerCase()}`;

  return (
    <div>
      <label htmlFor={id} className="text-xs font-medium uppercase tracking-wider text-slate-400">
        {label}
      </label>
      <select
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="mt-1 w-full rounded-md border border-slate-200 bg-transparent px-2 py-1.5 text-sm outline-none dark:border-slate-700"
      >
        <option value="">Select…</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}
