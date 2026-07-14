"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Can } from "@/components/auth/can";
import { DataTable, type Column } from "@/components/data/data-table";
import { Money } from "@/components/ui/money";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";
import { useApiQuery } from "@/lib/use-api";
import { receiveGoods, type ReceiveLineInput } from "@/features/purchasing/api";
import type { GoodsReceipt, ImportShipment, PurchaseOrder } from "@/features/purchasing/types";
import { ImportShipmentStatus, PurchaseOrderStatus } from "@/features/purchasing/types";
import type { PagedResult } from "@/types/api";
import { TrackingMode, type Product, type Supplier } from "@/types/catalog";
import { FEATURES, PermissionAction, type Branch } from "@/types/identity";

interface WarehouseOption {
  id: string;
  name: string;
}

interface DraftLine {
  productId: string;
  quantity: string;
  unitPrice: string;
  /** Comma-separated, one per unit. Only asked for when the product is serial-tracked. */
  serials: string;
}

/**
 * Goods receipts — **the document that moves stock**, and the only one in purchasing that does.
 *
 * It works with or without a purchase order (§25) and with or without an import shipment. A direct
 * purchase passes neither: the shop drove to the wholesaler and came back with a box, and forcing an
 * order would only produce fakes raised after the fact, which look real and are therefore worse than
 * none.
 *
 * **Serials are captured here, at the door** — not at the sale. That is what makes a warranty claim
 * answerable two years later: the serial ties the laptop on the counter back to the container it arrived
 * in, the supplier who sent it, and what it actually cost.
 *
 * For an import, this posts at the *goods* price only. The freight and duty may not have been invoiced
 * yet, and the shop cannot refuse to book stock it can physically see. The landed cost is folded in
 * afterwards, on the imports screen.
 */
export default function GoodsReceiptsPage() {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  const [receiving, setReceiving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const suppliers =
    useApiQuery<PagedResult<Supplier>>(["suppliers"], "api/v1/suppliers", { pageSize: 200 }).data?.items ?? [];
  const products =
    useApiQuery<PagedResult<Product>>(["products"], "api/v1/products", { pageSize: 200 }).data?.items ?? [];
  const branches = useApiQuery<PagedResult<Branch>>(["branches"], "api/v1/branches").data?.items ?? [];
  const warehouses = useApiQuery<WarehouseOption[]>(["warehouses"], "api/v1/warehouses").data ?? [];

  const orders =
    useApiQuery<PagedResult<PurchaseOrder>>(["purchase-orders"], "api/v1/purchase-orders", { pageSize: 200 })
      .data?.items ?? [];
  const shipments =
    useApiQuery<PagedResult<ImportShipment>>(["import-shipments"], "api/v1/import-shipments", { pageSize: 200 })
      .data?.items ?? [];

  // Only an approved order can take delivery — a draft is refused by the API, and offering it here would
  // be showing the user a door that slams in their face.
  const receivableOrders = orders.filter(
    (o) => o.status === PurchaseOrderStatus.Approved || o.status === PurchaseOrderStatus.PartiallyReceived,
  );

  // A costed container is closed: goods added now would carry none of its freight, and the ones already
  // received would carry all of it — the same container costed two different ways.
  const openShipments = shipments.filter(
    (s) => s.status === ImportShipmentStatus.InTransit || s.status === ImportShipmentStatus.Arrived,
  );

  const columns: Column<GoodsReceipt>[] = [
    {
      key: "number",
      header: "Receipt",
      render: (r) => (
        <div>
          <p className="font-mono text-xs">{r.number}</p>
          <p className="text-xs text-slate-500">{new Date(r.receivedAt).toLocaleDateString()}</p>
        </div>
      ),
    },
    {
      key: "supplier",
      header: "Supplier",
      render: (r) => (
        <div>
          <p>{r.supplierName}</p>
          {r.supplierReference && (
            <p className="font-mono text-xs text-slate-500">{r.supplierReference}</p>
          )}
        </div>
      ),
    },
    {
      key: "against",
      header: "Against",
      render: (r) => (
        <div className="space-y-0.5 text-xs">
          {r.purchaseOrderNumber && <p className="font-mono">{r.purchaseOrderNumber}</p>}
          {r.importShipmentNumber && (
            <p className="font-mono text-sky-700 dark:text-sky-400">{r.importShipmentNumber}</p>
          )}

          {/* Not an omission — §25's direct-purchase flow is a first-class path. */}
          {!r.purchaseOrderNumber && !r.importShipmentNumber && (
            <span className="text-slate-400">Direct purchase</span>
          )}
        </div>
      ),
    },
    { key: "warehouse", header: "Into", render: (r) => <span className="text-xs">{r.warehouseName}</span> },
    {
      key: "lines",
      header: "Goods",
      render: (r) => (
        <div className="space-y-0.5">
          {r.lines.map((line) => (
            <div key={line.id} className="text-xs">
              <span className="tabular-nums">{line.quantity.toLocaleString()}</span> ×{" "}
              <span className="font-mono">{line.productSku}</span>

              {/* The landed cost, once the container has been costed. It differs from the goods price,
                  and it is the number that actually fed the moving average. */}
              {line.apportionedCost > 0 && (
                <span className="ml-1 text-sky-700 dark:text-sky-400">
                  landed <Money amount={line.landedUnitCost} /> each
                </span>
              )}

              {line.serials.length > 0 && (
                <p className="font-mono text-[10px] text-slate-500">{line.serials.join(", ")}</p>
              )}
            </div>
          ))}
        </div>
      ),
    },
    {
      key: "total",
      header: "Goods value",
      align: "right",
      render: (r) => (
        <div>
          <Money amount={r.goodsTotal} currency={r.currencyCode} />
          {r.currencyCode !== "AED" && (
            <p className="text-xs text-slate-500">
              <Money amount={r.goodsTotalBase} /> at {r.exchangeRate}
            </p>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Goods receipts</h1>
          <p className="mt-1 text-sm text-slate-500">
            The document that moves stock — and where the serial binds. An order is optional; a direct
            purchase is a first-class path.
          </p>
        </div>

        <Can feature={FEATURES.goodsReceipts} action={PermissionAction.Create}>
          <button
            onClick={() => setReceiving(true)}
            className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 dark:bg-slate-100 dark:text-slate-900"
          >
            Receive goods
          </button>
        </Can>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      <Can
        feature={FEATURES.goodsReceipts}
        action={PermissionAction.View}
        fallback={<p className="text-sm text-slate-500">You do not have permission to view goods receipts.</p>}
      >
        <DataTable
          queryKey={["goods-receipts"]}
          endpoint="api/v1/goods-receipts"
          columns={columns}
          rowKey={(r) => r.id}
          searchPlaceholder="Search receipt, supplier or their reference…"
          emptyMessage="Nothing received yet."
        />
      </Can>

      {receiving && (
        <ReceiveDialog
          products={products}
          suppliers={suppliers}
          branches={branches}
          warehouses={warehouses}
          orders={receivableOrders}
          shipments={openShipments}
          onClose={() => setReceiving(false)}
          onSubmit={async (body) => {
            setError(null);

            try {
              await receiveGoods({ token: accessToken! }, body);
              setReceiving(false);

              // The receipt moved stock, so the inventory screens are stale too — and the order it was
              // received against has just ticked forward.
              await Promise.all([
                client.invalidateQueries({ queryKey: ["goods-receipts"] }),
                client.invalidateQueries({ queryKey: ["purchase-orders"] }),
                client.invalidateQueries({ queryKey: ["import-shipments"] }),
                client.invalidateQueries({ queryKey: ["stock"] }),
                client.invalidateQueries({ queryKey: ["stock-movements"] }),
                client.invalidateQueries({ queryKey: ["serials"] }),
              ]);
            } catch (e) {
              setError(e instanceof ApiError ? e.message : "That did not work.");
            }
          }}
        />
      )}
    </div>
  );
}

/**
 * The receiving form.
 *
 * It is hand-rolled rather than an `<EntityForm>` because a receipt is a *document with lines*, and each
 * line may need a serial per unit. A flat field list cannot express that.
 */
function ReceiveDialog({
  products,
  suppliers,
  branches,
  warehouses,
  orders,
  shipments,
  onClose,
  onSubmit,
}: {
  products: Product[];
  suppliers: Supplier[];
  branches: Branch[];
  warehouses: WarehouseOption[];
  orders: PurchaseOrder[];
  shipments: ImportShipment[];
  onClose: () => void;
  onSubmit: (body: Parameters<typeof receiveGoods>[1]) => Promise<void>;
}) {
  const [supplierId, setSupplierId] = useState(suppliers[0]?.id ?? "");
  const [branchId, setBranchId] = useState(branches[0]?.id ?? "");
  const [warehouseId, setWarehouseId] = useState(warehouses[0]?.id ?? "");
  const [purchaseOrderId, setPurchaseOrderId] = useState("");
  const [importShipmentId, setImportShipmentId] = useState("");
  const [currencyCode, setCurrencyCode] = useState("AED");
  const [exchangeRate, setExchangeRate] = useState("1");
  const [supplierReference, setSupplierReference] = useState("");
  const [busy, setBusy] = useState(false);

  const [lines, setLines] = useState<DraftLine[]>([
    { productId: products[0]?.id ?? "", quantity: "1", unitPrice: "0", serials: "" },
  ]);

  function update(index: number, patch: Partial<DraftLine>) {
    setLines((current) => current.map((line, i) => (i === index ? { ...line, ...patch } : line)));
  }

  /**
   * Picking an order fills the receipt in from it — supplier, warehouse, currency and the outstanding
   * lines at the agreed price. That is the entire point of having raised the order: what arrives is
   * checked against what was agreed, rather than retyped from the delivery note and hoped over.
   */
  function adoptOrder(id: string) {
    setPurchaseOrderId(id);

    const order = orders.find((o) => o.id === id);
    if (!order) return;

    setSupplierId(order.supplierId);
    setBranchId(order.branchId);
    setWarehouseId(order.warehouseId);
    setCurrencyCode(order.currencyCode);
    setExchangeRate(String(order.exchangeRate));

    setLines(
      order.lines
        .filter((line) => line.outstandingQuantity > 0)
        .map((line) => ({
          productId: line.productId,
          quantity: String(line.outstandingQuantity),
          unitPrice: String(line.unitPrice),
          serials: "",
        })),
    );
  }

  const total = lines.reduce(
    (sum, line) => sum + (Number(line.quantity) || 0) * (Number(line.unitPrice) || 0),
    0,
  );

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-900/40 p-6">
      <form
        onSubmit={async (event) => {
          event.preventDefault();
          setBusy(true);

          const order = orders.find((o) => o.id === purchaseOrderId);

          try {
            await onSubmit({
              supplierId,
              branchId,
              warehouseId,
              purchaseOrderId: purchaseOrderId || null,
              importShipmentId: importShipmentId || null,
              currencyCode,
              exchangeRate: Number(exchangeRate) || 1,
              supplierReference: supplierReference || null,
              lines: lines.map((line): ReceiveLineInput => {
                const serials = line.serials
                  .split(",")
                  .map((s) => s.trim())
                  .filter(Boolean);

                return {
                  productId: line.productId,
                  quantity: Number(line.quantity),
                  unitPrice: Number(line.unitPrice),

                  // Tie the line back to the order line it fulfils, so the order can tick itself off.
                  // Matched on product: an order carries one line per product, so this is unambiguous.
                  purchaseOrderLineId:
                    order?.lines.find((l) => l.productId === line.productId)?.id ?? null,

                  serialNumbers: serials.length > 0 ? serials : null,
                };
              }),
            });
          } finally {
            setBusy(false);
          }
        }}
        className="w-full max-w-3xl space-y-5 rounded-lg border border-slate-200 bg-white p-6 shadow-xl dark:border-slate-800 dark:bg-slate-950"
      >
        <div>
          <h2 className="text-lg font-semibold">Receive goods</h2>
          <p className="mt-1 text-sm text-slate-500">
            This puts the stock on the shelf. Serial numbers are captured now, at the door.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Against an order" help="Optional — a direct purchase has none.">
            <select
              value={purchaseOrderId}
              onChange={(e) => adoptOrder(e.target.value)}
              className={SELECT}
            >
              <option value="">Direct purchase — no order</option>
              {orders.map((order) => (
                <option key={order.id} value={order.id}>
                  {order.number} — {order.supplierName}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Import shipment" help="Set it if these goods came in a container whose freight is still to come.">
            <select
              value={importShipmentId}
              onChange={(e) => setImportShipmentId(e.target.value)}
              className={SELECT}
            >
              <option value="">Not an import</option>
              {shipments.map((shipment) => (
                <option key={shipment.id} value={shipment.id}>
                  {shipment.number} — {shipment.supplierName}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Supplier" required>
            <select
              value={supplierId}
              onChange={(e) => setSupplierId(e.target.value)}
              required
              className={SELECT}
            >
              {suppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>
                  {supplier.name}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Branch" required>
            <select value={branchId} onChange={(e) => setBranchId(e.target.value)} required className={SELECT}>
              {branches.map((branch) => (
                <option key={branch.id} value={branch.id}>
                  {branch.name}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Into warehouse" required>
            <select
              value={warehouseId}
              onChange={(e) => setWarehouseId(e.target.value)}
              required
              className={SELECT}
            >
              {warehouses.map((warehouse) => (
                <option key={warehouse.id} value={warehouse.id}>
                  {warehouse.name}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Their delivery note" help="So a query can be answered without ringing them.">
            <input
              value={supplierReference}
              onChange={(e) => setSupplierReference(e.target.value)}
              className={INPUT}
            />
          </Field>

          <Field label="Currency">
            <input
              value={currencyCode}
              onChange={(e) => setCurrencyCode(e.target.value.toUpperCase())}
              maxLength={3}
              className={INPUT}
            />
          </Field>

          <Field label="Exchange rate" help="The rate on the day the goods landed — this is what values the stock.">
            <input
              type="number"
              step="0.0001"
              value={exchangeRate}
              onChange={(e) => setExchangeRate(e.target.value)}
              className={INPUT}
            />
          </Field>
        </div>

        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <p className="text-sm font-medium">Lines</p>
            <button
              type="button"
              onClick={() =>
                setLines((current) => [
                  ...current,
                  { productId: products[0]?.id ?? "", quantity: "1", unitPrice: "0", serials: "" },
                ])
              }
              className="rounded-md border border-slate-200 px-2.5 py-1 text-xs hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
            >
              Add line
            </button>
          </div>

          {lines.map((line, index) => {
            const product = products.find((p) => p.id === line.productId);
            const serialTracked = product?.trackingMode === TrackingMode.Serial;
            const expected = Number(line.quantity) || 0;
            const given = line.serials.split(",").map((s) => s.trim()).filter(Boolean).length;

            return (
              <div
                key={index}
                className="space-y-2 rounded-md border border-slate-200 p-3 dark:border-slate-800"
              >
                <div className="grid gap-2 sm:grid-cols-[1fr_6rem_7rem_2rem]">
                  <select
                    value={line.productId}
                    onChange={(e) => update(index, { productId: e.target.value, serials: "" })}
                    className={SELECT}
                  >
                    {products.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.sku} — {p.name}
                      </option>
                    ))}
                  </select>

                  <input
                    type="number"
                    step="any"
                    min="0"
                    value={line.quantity}
                    onChange={(e) => update(index, { quantity: e.target.value })}
                    aria-label="Quantity"
                    className={INPUT}
                  />

                  <input
                    type="number"
                    step="any"
                    min="0"
                    value={line.unitPrice}
                    onChange={(e) => update(index, { unitPrice: e.target.value })}
                    aria-label="Unit price"
                    className={INPUT}
                  />

                  <button
                    type="button"
                    onClick={() => setLines((current) => current.filter((_, i) => i !== index))}
                    disabled={lines.length === 1}
                    aria-label="Remove line"
                    className="rounded-md border border-slate-200 text-xs text-slate-500 disabled:opacity-30 dark:border-slate-700"
                  >
                    ✕
                  </button>
                </div>

                {/* Asked for only where the product actually needs it. The ledger refuses a receipt whose
                    serial count does not match its quantity, so the count is shown as it is typed rather
                    than discovered on submit. */}
                {serialTracked && (
                  <div>
                    <input
                      value={line.serials}
                      onChange={(e) => update(index, { serials: e.target.value })}
                      placeholder="Scan each serial, comma separated"
                      aria-label="Serial numbers"
                      className={INPUT}
                    />
                    <p
                      className={`mt-1 text-xs ${
                        given === expected ? "text-slate-500" : "text-amber-600 dark:text-amber-400"
                      }`}
                    >
                      {given} of {expected} serials — one per unit
                    </p>
                  </div>
                )}
              </div>
            );
          })}
        </div>

        <div className="flex items-center justify-between border-t border-slate-200 pt-4 dark:border-slate-800">
          <p className="text-sm">
            Goods value <Money amount={total} currency={currencyCode} />
          </p>

          <div className="flex gap-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-md border border-slate-200 px-3 py-1.5 text-sm dark:border-slate-700"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={busy}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
            >
              {busy ? "Receiving…" : "Receive"}
            </button>
          </div>
        </div>
      </form>
    </div>
  );
}

const INPUT =
  "w-full rounded-md border border-slate-200 bg-transparent px-3 py-1.5 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300";

const SELECT = INPUT;

function Field({
  label,
  help,
  required,
  children,
}: {
  label: string;
  help?: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className="block space-y-1">
      <span className="text-sm font-medium">
        {label}
        {required && <span className="text-red-500"> *</span>}
      </span>
      {children}
      {help && <span className="block text-xs text-slate-500">{help}</span>}
    </label>
  );
}
