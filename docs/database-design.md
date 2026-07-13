# Database design

> Status: **draft**. Tables below are the intended target, not the current schema — EF Core
> migrations create the tables as each module is built.
>
> **[architecture.md](architecture.md) is the authority.** It supersedes this document where the
> two disagree; §3 there carries the full ~95-table entity list. This file covers the tenancy
> model, the conventions and the core tables, and has been reconciled with the decisions taken
> in architecture.md §7 (per-user permissions, warehouses, payment allocations, weighted-average
> costing, no general ledger).

PostgreSQL 17. Schema `techstorepro`.

## 1. Tenancy model

**One database, one schema, a `company_id` column on every tenant-owned table.**

The tenant filter is applied centrally: any entity implementing `ITenantScoped` gets an EF
Core global query filter (`ApplicationDbContext.OnModelCreating`) pinned to the company on
the caller's token, and gets `company_id` stamped on insert. Feature code never writes
`WHERE company_id = …` by hand, so it cannot forget to.

Rejected alternatives, and why:

| Option              | Isolation | Migration cost              | Cross-company reporting | Verdict                                        |
| ------------------- | --------- | --------------------------- | ----------------------- | ---------------------------------------------- |
| Database per tenant | Strongest | N databases per release     | Painful                 | Too costly to operate at SaaS scale.            |
| Schema per tenant   | Strong    | N schemas per release       | Awkward                 | Same migration problem, no real gain.           |
| **Shared tables**   | In code   | One migration per release   | Trivial                 | **Chosen** — with the filter enforced centrally. |

The residual risk is a raw SQL query that bypasses the filter. Mitigations: raw SQL is
reviewed, and integration tests assert that company A's session cannot see company B's rows.

Defence in depth worth adding before production: Postgres row-level security keyed on a
session variable, so even a bypassed filter cannot return foreign rows.

## 2. Conventions

- Tables `snake_case`, plural: `sales_orders`, `stock_movements`.
- PK: `id uuid primary key default gen_random_uuid()`.
- FK: `<entity>_id`, always with an index.
- Tenant column: `company_id uuid not null references companies(id)`, and it is the **leading
  column of every composite index** — a query is always scoped before it is filtered.
- Timestamps: `timestamptz`, UTC. Audit columns `created_at, created_by, updated_at, updated_by`.
- Soft delete: `is_deleted boolean not null default false`, `deleted_at`, `deleted_by`,
  `deleted_reason` — requirements §10 asks for the reason and a restore path, not just a flag.
- Effective-dated configuration: every settable business rule (tax rates, prices, discounts,
  payment methods, warranty periods, numbering rules — requirements §11) carries `valid_from`,
  `valid_to` (null = open) and `is_active`, and is **versioned rather than overwritten**. Values are
  resolved as `GetValueAsOf(key, transactionDate)`, so a change tomorrow cannot rewrite a document
  raised today.
- Money: `numeric(18,4)` plus a `currency_code char(3)`. Quantities: `numeric(18,4)`.
- Enums are stored as `smallint`, mapped in code. (Postgres enum types are hard to alter.)
- Document numbers (`INV-2026-00042`) are generated from a sequence row keyed
  `(company_id, branch_id, document_type, year)` and locked with `SELECT … FOR UPDATE`; never from a
  global sequence, because the numbering must be gapless. **The key includes the branch** —
  requirements §5 lists document numbering under *Branch details*. A document type that is not
  branch-specific uses the company's default branch.

## 3. Core tables

### Platform / identity

```
companies            id, name, legal_name, tax_number, base_currency, country, is_active, ...
users                id, email (unique, citext), password_hash, full_name, is_active,
                     failed_login_count, locked_until, ...
company_users        id, company_id, user_id, is_default        -- membership; unique (company_id, user_id)
features             code (PK), module, name                    -- e.g. "sales.invoice"
permission_actions   code (PK)                                  -- View, Create, Edit, Delete,
                                                                --  Approve, Print, Export
user_permissions     id, company_id, company_user_id, feature_code, action_code, granted
                                                 -- unique (company_user_id, feature_code, action_code)
refresh_tokens       id, user_id, company_id, token_hash, expires_at, revoked_at, device_info, ip
login_history        id, user_id, company_id, at, ip, device_info, result, failure_reason
audit_log            id, company_id, user_id, entity, entity_id, action,
                     old_values (jsonb), new_values (jsonb), ip, at
branches             id, company_id, name, address, default_warehouse_id, is_default
warehouses           id, company_id, branch_id (NULL = company-shared), name, code, type
branch_warehouses    branch_id, warehouse_id, can_issue, can_receive
```

`users` is deliberately **not** tenant-scoped: one person, one login, many companies. The
join to a tenant is `company_users`, and that is where permissions hang.

**There is no `roles` table, and no role claim on the token.** Requirements §7 states *"No fixed
roles"*: permission is granted per user as a `(feature, action)` pair. Bulk assignment exists in the
UI (tick a module, tick an action column, copy another user's grid) but always writes individual
`user_permissions` rows — nothing is stored as a reusable bundle, so editing one user never changes
another. See [architecture.md §3.3](architecture.md).

A warehouse with `branch_id` set is owned by that branch. A warehouse with `branch_id` null is
shared at company level, and `branch_warehouses` declares which branches may issue from or receive
into it — "shared" must never silently mean "any branch can drain it".

### Master data

```
products             id, company_id, sku, name, category_id, brand, unit, tracking_mode,
                     tax_class_id, reorder_level, is_active     -- unique (company_id, sku)
product_categories   id, company_id, name, parent_id
serials              id, company_id, product_id, serial_no, status, current_branch_id,
                     purchase_cost, warranty_until               -- unique (company_id, serial_no)
customers            id, company_id, code, name, type, tax_number, credit_limit, payment_terms
suppliers            id, company_id, code, name, country, default_currency, lead_time_days
price_lists          id, company_id, name, currency_code, valid_from, valid_to
price_list_items     id, price_list_id, product_id, unit_price
tax_rates            id, company_id, name, percent, is_default
currencies           code, name                                  -- reference data, not tenant-scoped
fx_rates             id, company_id, currency_code, rate_to_base, rate_date
```

### Inventory

```
stock_movements      id, company_id, branch_id, warehouse_id, product_id, serial_id (null),
                     movement_type, quantity (signed), unit_cost, value_adjustment,
                     average_cost_after, balance_after, reference_type, reference_id,
                     reference_number, notes, occurred_at
stock_balances       id, company_id, warehouse_id, product_id, quantity, reserved_quantity,
                     average_cost
                     -- a cache of movements; unique (company_id, warehouse_id, product_id)
stock_reservations   id, company_id, warehouse_id, product_id, serial_id (null), quantity,
                     fulfilled_quantity, reference_type, reference_id, expires_at,
                     released_at, status
stock_adjustments    id, company_id, branch_id, warehouse_id, number, reason, explanation,
                     stock_count_id (null), adjusted_at
stock_adjustment_lines  id, company_id, stock_adjustment_id, product_id, serial_id (null),
                     quantity (signed), unit_cost, notes
stock_transfers      id, company_id, branch_id, number, from_warehouse_id, to_warehouse_id,
                     status, shipped_at, shipped_by, received_at, received_by, notes
stock_transfer_lines id, company_id, stock_transfer_id, product_id, serial_id (null),
                     quantity, received_quantity, unit_cost
stock_counts         id, company_id, branch_id, warehouse_id, number, status, started_at,
                     counted_at, approved_by, approved_at, stock_adjustment_id (null)
stock_count_lines    id, company_id, stock_count_id, product_id, serial_id (null),
                     system_quantity, counted_quantity, unit_cost, notes
serial_events        id, company_id, serial_id, type, status, warehouse_id, reference_type,
                     reference_id, reference_number, notes, at
barcode_print_jobs   id, company_id, source_type, source_id, symbology, template,
                     label_count, include_price, include_product_name, printed_at
```

**A transfer is two movements, not one.** Stock leaves the source when the van is loaded
(`TransferOut`) and arrives when someone signs for it (`TransferIn`), which may be days later and may
be for fewer units. A single instantaneous movement would make in-transit stock either sellable at
both ends or sellable at neither, and would leave a short delivery nowhere to be recorded.

**A count snapshots the system quantity onto the line when the line is counted**, not at approval. A
count that took two hours while the shop kept trading would otherwise compare this morning's shelf
against this afternoon's ledger and invent a variance out of the sales that happened in between.

`stock_movements`, `stock_balances` and `serial_events` are **not soft-deletable**: a ledger you can
retire a row from is not a ledger, and a balance of zero is a fact rather than a retired row. A
correction is a new, opposing movement.

**`value_adjustment` is money with no units behind it** — the landed cost of an import folded into
stock that was received weeks earlier (`movement_type = Revaluation`, the only type whose direction is
zero). It exists because goods and their true cost do not arrive together. The balance audit therefore
recomputes value as `SUM(quantity × unit_cost + value_adjustment)`; leave the second term out and every
import the shop ever landed would show as a permanent discrepancy nobody could clear.

### Purchasing and imports (P4)

```
purchase_orders      id, company_id, branch_id, warehouse_id, supplier_id, number, status,
                     currency_code, exchange_rate, ordered_at, expected_at, approved_by, approved_at
purchase_order_lines id, company_id, purchase_order_id, product_id, quantity, unit_price,
                     discount_percent, received_quantity
goods_receipts       id, company_id, branch_id, warehouse_id, supplier_id, number,
                     purchase_order_id (NULL), import_shipment_id (NULL),
                     currency_code, exchange_rate, supplier_reference, received_at
goods_receipt_lines  id, company_id, goods_receipt_id, purchase_order_line_id (null), product_id,
                     quantity, unit_price, discount_percent, apportioned_cost
goods_receipt_serials  id, company_id, goods_receipt_line_id, serial_number, serial_id
import_shipments     id, company_id, branch_id, supplier_id, number, status, transport_document,
                     vessel_or_flight, shipped_at, arrived_at, costed_at, unabsorbed_cost
import_shipment_charges  id, company_id, import_shipment_id, charge_type, vendor, amount,
                     currency_code, exchange_rate, incurred_at
supplier_invoices    id, company_id, branch_id, supplier_id, number, supplier_reference, status,
                     goods_receipt_id (null), currency_code, exchange_rate, invoiced_at, due_at
                     -- unique (company_id, supplier_id, supplier_reference): the same supplier
                     -- cannot bill the same reference twice, or it would be paid twice
supplier_invoice_lines   id, company_id, supplier_invoice_id, product_id (null), description,
                     quantity, unit_price, discount_percent, tax_percent
supplier_payments    id, company_id, branch_id, supplier_id, payment_method_id, number, reference,
                     amount, currency_code, exchange_rate, paid_at
supplier_payment_allocations  id, company_id, supplier_payment_id, supplier_invoice_id, amount,
                     invoice_exchange_rate, payment_exchange_rate
```

**`goods_receipts.purchase_order_id` is nullable, deliberately.** Requirements §25 makes the PO optional
and gives a direct-purchase flow, because a shop that drives to the wholesaler and comes back with a box
genuinely has no order. Forcing one would only produce fakes raised after the fact — which look real,
and are therefore worse than none.

**A payment is a header plus allocations, not a column on an invoice.** One transfer settles three
invoices; one invoice is settled by two instalments. A single `invoice_id` on a payment expresses
neither, and a shop paying its supplier monthly does both constantly.

**Both exchange rates are snapshotted on the allocation**, never looked up later. The invoice's rate is
a fact about the day it was raised; re-reading it years afterwards would give the same answer only until
somebody corrected a historical FX rate, at which point every past gain and loss in the system would
silently change. The realised FX result is `amount × (invoice_rate − payment_rate)`, and it belongs in
the P&L — **not** in the cost of the stock. The laptops did not become cheaper to buy; the currency
moved.

Rates are `numeric(18,6)`, not `(18,4)`: a rate multiplies a whole invoice, so its rounding error is
multiplied too.

Stock is keyed by **warehouse**, not branch. `branch_id` stays on the movement for reporting, but
the balance a sale decrements is a warehouse balance.

`stock_movements` is append-only and is the source of truth; `stock_balances` is a cache kept
in the same transaction as the movement that changes it, with the balance row locked
(`INSERT … ON CONFLICT DO NOTHING`, then `SELECT … FOR UPDATE` — the upsert exists because
`FOR UPDATE` locks nothing when the row does not yet exist). Reads hit the balance table.

**The cache must be able to prove itself.** `GET /api/v1/inventory/balance-audit` recomputes every
balance from the ledger, and the `InventoryMaintenanceService` runs the same audit nightly, per
company. It compares **quantity and cost**: a cache whose units are right and whose average cost is
wrong looks healthy while every sale from that warehouse books the wrong COGS into the P&L. It
reports; it never repairs — a disagreement means something wrote stock outside `IStockLedger`, and
overwriting the evidence would destroy the only trace of it.

`available = quantity - reserved_quantity`. That subtraction is what requirements §20's "prevent
overselling" actually means.

**Costing is weighted average** (decided; architecture.md §4.6). On each receipt:
`new_average = (qty_on_hand × old_average + qty_in × landed_unit_cost) ÷ (qty_on_hand + qty_in)`,
recomputed inside the same locked transaction as the movement. Note it is the **landed** cost that
raises the average — using the invoice cost would silently under-value every imported unit, and with
a moving average that error spreads to all stock of the product, not just the imported units.

### Purchasing and imports

```
purchase_orders      id, company_id, supplier_id, number, status, currency_code, fx_rate, ...
purchase_order_lines id, purchase_order_id, product_id, quantity, unit_price
goods_receipts       id, company_id, purchase_order_id (NULLABLE), supplier_id, branch_id,
                     warehouse_id, number, received_at
                     -- nullable PO: requirements §25 "PR is not required, PO is optional",
                     -- and Direct Purchase is Supplier → GRN → Stock, with no PO at all
goods_receipt_lines  id, goods_receipt_id, product_id, quantity, unit_cost, landed_unit_cost
goods_receipt_serials id, goods_receipt_line_id, serial_no    -- §27 capture at receipt
import_shipments     id, company_id, reference, supplier_id, status, apportion_basis,
                     currency_code, fx_rate, arrived_at
shipment_costs       id, import_shipment_id, cost_type, amount, currency_code  -- freight, duty, ...
shipment_purchase_orders  import_shipment_id, purchase_order_id
supplier_invoices    id, company_id, supplier_id, number, total, currency_code, due_date
supplier_payments    id, company_id, supplier_id, amount, currency_code, paid_at, method
```

Landed cost: total `shipment_costs` (converted to base currency) is apportioned across the
receipt lines of the shipment's POs by `apportion_basis`, and the result raises each line's
`unit_cost` before it reaches `stock_movements`. Inventory therefore holds landed cost, not
invoice cost — the whole point of the import module.

### Sales

```
quotes / quote_lines
sales_orders         id, company_id, branch_id, customer_id, number, status, currency_code,
                     fx_rate, subtotal, tax_total, total
sales_order_lines    id, sales_order_id, product_id, quantity, unit_price, discount,
                     tax_percent_snapshot
deliveries           id, company_id, sales_order_id, warehouse_id, delivered_at
delivery_lines       id, delivery_id, product_id, serial_id (null), quantity
invoices             id, company_id, sales_order_id, number, issued_at, due_date, total, status
invoice_lines        id, invoice_id, product_id, serial_id (null), quantity, unit_price,
                     discount, tax_percent_snapshot, tax_amount, line_total, unit_cost
payments             id, company_id, customer_id, number, amount, currency_code, paid_at, remarks
payment_lines        id, payment_id, payment_method_id, amount, reference
payment_allocations  id, payment_id, invoice_id, amount
credit_notes / credit_note_lines
customer_credit_ledger  id, company_id, customer_id, entry_type, amount, reference_type,
                        reference_id, at
```

`invoice_lines.serial_id` is what makes a warranty claim answerable two years later: it ties a
physical machine to the sale that put it in the customer's hands.

**A payment is a header, not a row on an invoice.** Requirements §23 requires several payment
*methods* on one sale (cash + card) and partial payment *across* invoices — a single
`payments.invoice_id` can express neither. So: `payments` (header) → `payment_lines` (one per
method) → `payment_allocations` (one per invoice settled).

**Lines snapshot the tax percentage they were computed with**, they do not merely point at a
`tax_rate_id`. General Rule 3 — *"historical transactions must not change when settings are
updated"* — is unenforceable through a foreign key alone: editing the tax rate next year would
silently restate every invoice ever raised. The rate row is effective-dated *and* the line keeps a
copy. Both are required.

### Repairs

```
repair_tickets       id, company_id, branch_id, number, customer_id, device_product_id (null),
                     device_serial (free text — the device is usually not our stock),
                     reported_fault, accessories, condition_notes, status,
                     estimated_cost, approved_at, warranty_invoice_line_id (null),
                     received_at, promised_at, delivered_at
repair_diagnoses     id, repair_ticket_id, technician_id, findings, recommended_action, at
repair_parts         id, repair_ticket_id, product_id, serial_id (null), quantity, unit_cost,
                     unit_price   -- consumption creates a stock_movement
repair_labour        id, repair_ticket_id, technician_id, hours, hourly_rate, description
repair_charges       id, repair_ticket_id, invoice_id (null)   -- null until invoiced
```

`warranty_invoice_line_id` links a warranty repair back to the sale. When set, parts and labour
are still costed (so the warranty's true cost is visible) but not charged.

## 4. Indexing

Every tenant-scoped table gets `(company_id)` as the leading column of its indexes:

```sql
create index ix_products_company_sku          on techstorepro.products (company_id, sku);
create index ix_stock_movements_company_prod  on techstorepro.stock_movements (company_id, product_id, occurred_at desc);
create index ix_invoices_company_customer     on techstorepro.invoices (company_id, customer_id, issued_at desc);
create index ix_repair_tickets_company_status on techstorepro.repair_tickets (company_id, status, received_at desc);
create unique index ux_serials_company_serial on techstorepro.serials (company_id, serial_no);
```

Text search on product and customer names uses `pg_trgm` (`gin (name gin_trgm_ops)`) — the
counter searches by fragments of a name, not by prefix.

## 5. Migrations

EF Core owns the schema. Migrations live in
`backend/src/TechStorePro.Infrastructure/Persistence/Migrations/` and are applied with
`./scripts/db-migrate.ps1`. They are never edited after being merged; a mistake is fixed by a
new migration.

Migrations must be backward-compatible with the running release (add a column, backfill, then
drop the old one in a later release) — the shop cannot take downtime for a deploy.
