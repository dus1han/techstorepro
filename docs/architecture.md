# TechStorePro — System Architecture

> Status: **proposal for approval**. No code has been written against this document.
> It derives from [requirements.md](requirements.md) (44-section feature spec) and reconciles it
> with the foundation built in P0 and with [database-design.md](database-design.md) /
> [api-design.md](api-design.md), which it **supersedes where they conflict** (see §7).

---

## 1. System architecture

### 1.1 Shape: a modular monolith

One deployable ASP.NET Core Web API, one Next.js frontend, one PostgreSQL database. Modules are
enforced by folder and namespace boundaries inside the existing Clean Architecture layers, not by
separate services.

Rationale: every requirement in the spec is a *transactional* one — a sale moves stock, a repair
consumes parts, a GRN raises landed cost. Those need one database transaction. Microservices would
buy independent scaling this product does not need (a computer shop is not a high-traffic system)
and would cost distributed transactions across exactly the boundaries the business crosses most.
The module boundaries below are drawn so that if a service ever *must* be split out (reporting is
the likely first), the seam already exists.

### 1.2 Runtime topology

```
┌──────────────────┐        ┌─────────────────────────────────────────┐
│  Next.js 16      │  HTTPS │  ASP.NET Core 10 Web API                │
│  (App Router)    │───────►│                                         │
│  TS + Tailwind   │  JWT   │  ┌───────────────────────────────────┐  │
└──────────────────┘        │  │ API      controllers, middleware  │  │
                            │  ├───────────────────────────────────┤  │
                            │  │ Application   CQRS, MediatR       │  │
                            │  │               pipeline behaviours │  │
                            │  ├───────────────────────────────────┤  │
                            │  │ Domain        entities, rules     │  │
                            │  ├───────────────────────────────────┤  │
                            │  │ Infrastructure  EF Core, adapters │  │
                            │  └───────────────────────────────────┘  │
                            └───┬──────────┬──────────┬───────────┬───┘
                                │          │          │           │
                         ┌──────▼───┐ ┌────▼─────┐ ┌──▼───────┐ ┌─▼────────┐
                         │PostgreSQL│ │Background│ │  Object  │ │   SMTP   │
                         │    17    │ │   jobs   │ │  storage │ │ provider │
                         │          │ │(Hangfire)│ │(S3/Blob) │ │          │
                         └──────────┘ └──────────┘ └──────────┘ └──────────┘
```

**Additions to the P0 stack that the spec forces** (none of these exist today):

| Concern | Requirement driving it | Proposed component |
| --- | --- | --- |
| Background jobs | §4 monthly SaaS billing, §13 payment reminders/expiry warnings, §42 bulk Excel import, §43 backups | **Hangfire** on Postgres (no extra infrastructure — reuses the DB) |
| File storage | §39 attachments, §28 repair photos, §5 company logo, §4 storage limits | `IFileStorage` → local disk (dev), S3/Azure Blob (prod) |
| PDF generation | §12 documents, §17 barcode labels | **QuestPDF** (server-side, deterministic, printable) |
| Barcode / QR | §17 | **ZXing.Net** for encoding, rendered into QuestPDF labels |
| Excel import/export | §42 | **ClosedXML** |
| Email | §13 | `IEmailSender` → per-company SMTP, platform SMTP fallback |

Deliberately **not** added: Redis (Postgres covers caching and Hangfire at this scale), a message
broker (in-process domain events suffice), Elasticsearch (§37 global search is served by `pg_trgm`).

### 1.3 Two API surfaces

The spec describes two different products (§2 platform admin vs §5+ the tenant ERP). They share a
process but not an authorisation model:

```
/api/v1/platform/*    Platform owners. NOT tenant-scoped. Manages companies, plans, billing.
/api/v1/*             Company users. Tenant-scoped by the company_id claim. The ERP itself.
```

Separate JWT audiences and separate authorisation policies. A tenant token can never reach a
platform endpoint, and platform staff act on tenant data only through explicit, audited
impersonation. This is the boundary that keeps §8's data-isolation promise honest.

### 1.4 Cross-cutting engines

These are the pieces that make "configurable without code changes" (§Core Principles) real. They
are built **before** the modules that depend on them, because retrofitting them is what turns a
codebase into a hardcoded one.

1. **Tenant context** — *exists (P0)*. `company_id` from the token; EF global query filter.
2. **Permission engine** (§7) — feature × action matrix, evaluated per request.
3. **Settings engine** (§11) — effective-dated, typed, company/branch-scoped configuration with
   `GetValueAsOf(key, date)`. Every business rule reads from here.
4. **Document numbering** (§5) — gapless per company + branch + document type.
5. **Stock ledger** (§19–21) — append-only movements, derived balances, reservations.
6. **Audit trail** (§9) — old/new values harvested from the EF change tracker.
7. **Approval engine** (§32) — configurable limits and approvers.
8. **Notification engine** (§13) — templates, variables, queue, delivery history.
9. **Entitlement engine** (§4) — plan features, user/branch/storage limits.
10. **Attachment service** (§39) and **Notes service** (§38) — polymorphic, any entity.

### 1.5 The two rules that protect historical data

The spec states these twice (General Rules 3 and §11), and they are the single most
architecturally consequential lines in it:

> Historical transactions must not change when settings are updated.

- **Settings are effective-dated, never overwritten.** An update writes a new version with a new
  `valid_from`; the old row is retained.
- **Transactions snapshot what they used.** A sales invoice line stores the tax *percentage*, the
  unit price and the discount it was computed with — not just a `tax_rate_id` pointing at a row
  that someone will edit next year. A foreign key alone would silently rewrite history.

---

## 2. Module dependency diagram

Arrows point **towards the dependency** ("Sales depends on Inventory"). No cycles.

```
                        ┌──────────────────────────────────────┐
                        │   PLATFORM (SaaS)                    │   §2, §3, §4
                        │   plans · subscriptions · billing    │   not tenant-scoped
                        └───────────────┬──────────────────────┘
                                        │ provisions company + entitlements
                                        ▼
┌───────────────────────────────────────────────────────────────────────────────┐
│  FOUNDATION  (every module below depends on all of these)                      │
│                                                                               │
│   Identity & Access      Settings & Config      Audit & Timeline              │
│   §6 §7 §8               §11                    §9 §10                        │
│   users · companies      effective-dated        audit log · soft delete       │
│   branches · warehouses  business rules         activity timeline             │
│   permissions · JWT                                                           │
└───────────────────────────────────┬───────────────────────────────────────────┘
                                    ▼
┌───────────────────────────────────────────────────────────────────────────────┐
│  MASTER DATA          §14 §15 §16 §31                                          │
│  products · categories · brands · customers · suppliers                        │
│  price lists · tax rates · currencies · payment methods                        │
└───────────────────────────────────┬───────────────────────────────────────────┘
                                    ▼
┌───────────────────────────────────────────────────────────────────────────────┐
│  INVENTORY  ★ the spine       §17 §18 §19 §20 §21                              │
│  stock ledger (append-only) · balances per warehouse · serials · barcodes       │
│  reservations · transfers · adjustments · stock counts · costing                │
└──────┬───────────────────────┬────────────────────────┬───────────────────────┘
       │                       │                        │
       ▼                       ▼                        ▼
┌──────────────┐      ┌─────────────────┐      ┌──────────────────┐
│ PURCHASING   │      │ SALES           │      │ REPAIRS          │
│ §25 §27      │      │ §22 §23 §24     │      │ §28 §29 §30      │
│ PO · GRN     │      │ POS · quotes    │      │ intake·diagnosis │
│ supplier inv │      │ invoices·returns│      │ parts · labour   │
│              │      │ credit notes    │      │ outsourced       │
│      ▼       │      │      ▲          │      │      ▲           │
│  IMPORTS     │      │      │          │      │      │           │
│  §26         │      │      └──────────┼──────┘ warranty needs   │
│  shipments   │      │   warranty claim│        the original sale│
│  landed cost │      └─────────────────┘                         │
└──────┬───────┘               │                        │
       └───────────────────────┴────────────────────────┘
                               ▼
                  ┌────────────────────────────┐
                  │  FINANCE     §33 §34       │
                  │  AR · AP · cash · bank     │
                  │  expenses                  │
                  └─────────────┬──────────────┘
                                ▼
                  ┌────────────────────────────┐
                  │  REPORTING & DASHBOARD     │  §35 §36
                  │  read-only over everything │
                  └────────────────────────────┘

CROSS-CUTTING SERVICES — used by every module, depend only on Foundation:
  Documents & Printing §12 §17   Notifications §13   Attachments §39   Notes §38
  Global Search §37              Import/Export §42   Backup §43        Integrations §44
```

**Why Inventory is the spine:** purchasing raises stock, sales consumes it, repairs consume it, and
finance values it. Building any of the three leaf modules before the ledger means writing stock
logic three times and reconciling it forever.

**The one back-edge:** Repairs → Sales. A warranty repair (§30) must find the invoice line that sold
the serial. This is a *read* dependency on Sales, not a write, so it stays acyclic — but it is why
Sales must ship before Repairs.

---

## 3. Database entity list

PostgreSQL 17, schema `techstorepro`. Conventions from [database-design.md §2](database-design.md)
hold: `snake_case` plural tables, `uuid` PKs, `company_id` leading every tenant index, `numeric(18,4)`
money, `timestamptz` UTC, soft delete, enums as `smallint`.

**★ = new, not in the current database-design.md. ⚠ = changes an existing design decision.**

### 3.1 Platform / SaaS  ★ (entire section is new)

```
★ subscription_plans        id, name, monthly_price, annual_price, trial_days,
                            max_users, max_branches, storage_limit_mb, is_active
★ plan_features             plan_id, feature_code            -- what the plan unlocks
★ subscriptions             id, company_id, plan_id, status, trial_ends_at,
                            current_period_start, current_period_end, cancelled_at
★ subscription_invoices     id, company_id, subscription_id, number, period_start,
                            period_end, amount, tax_amount, total, due_date, status
★ subscription_payments     id, subscription_invoice_id, amount, method, reference, paid_at
★ company_usage             company_id, users_count, branches_count, storage_used_mb, measured_at
★ platform_users            id, email, password_hash, full_name, is_active   -- platform staff
★ platform_audit_log        id, platform_user_id, action, target_company_id, at, details
```

Subscription lifecycle (§4): `Trial → Active → Expired → Suspended → Cancelled`, plus `Renewed`.
Stored as `smallint` status with transitions enforced in the domain.

### 3.2 Identity, tenancy, security

```
  companies                 id, name, legal_name, logo_file_id, tax_number, registration_number,
                            base_currency, timezone, country, address, email, website,
                            bank_details, is_active
  branches                  id, company_id, name, address, contact, ★ default_warehouse_id, is_default
★ warehouses            ⚠   id, company_id, ★ branch_id (NULLABLE), name, code, type, is_active
                            -- branch_id SET  = owned by that branch
                            -- branch_id NULL = shared at company level
★ branch_warehouses     ⚠   branch_id, warehouse_id, can_issue, can_receive
                            -- which branches may transact against a SHARED warehouse
  users                     id, email (citext unique), password_hash, full_name, phone,
                            is_active, ★ failed_login_count, ★ locked_until, ★ must_change_password
  company_users             id, company_id, user_id, is_default   -- membership; unique(company_id,user_id)
  refresh_tokens            id, user_id, company_id, token_hash, expires_at, revoked_at,
                            ★ device_info, ★ ip_address
★ login_history             id, user_id, company_id, at, ip_address, device_info, user_agent,
                            result, failure_reason
★ user_sessions             id, user_id, company_id, started_at, last_seen_at, ip, device, revoked_at
★ user_preferences          user_id, company_id, theme, default_landing_page,
                            dashboard_layout (jsonb), favourite_modules (jsonb)
```

**⚠ `warehouses` is new and it changes the inventory keys.** See §7 conflict C.

**DECIDED: a branch may own warehouses *and* use shared ones, configurably.** `warehouses.branch_id`
is nullable — set means branch-owned, null means company-shared. `branch_warehouses` then declares
which branches may issue from or receive into a shared warehouse, so "shared" never silently means
"every branch can drain it". Resolution order when a transaction needs a warehouse:

```
1. explicit warehouse on the document        (user picked one)
2. branch.default_warehouse_id               (§5 "Branch details: Default warehouse")
3. reject — never guess
```

A branch-owned warehouse is only visible to its owning branch. A shared warehouse is visible to the
branches listed in `branch_warehouses`. Both are enforced in `IStockLedger`, not in feature code.

### 3.3 Permissions  ⚠ (replaces the `roles` / `role_permissions` design)

§7 says *"No fixed roles. Company Admin can assign permissions by feature."* The permission unit is
therefore **(feature, action)**, not a role name.

**DECIDED: strictly per-user.** There are no stored templates and no role table of any kind. Nothing
in the schema or at runtime resolves a role name.

```
★ features                  code (PK), module, name, display_order   -- reference data, seeded
                            e.g. "sales.invoice", "inventory.transfer", "repairs.job"
★ permission_actions        code (PK)   -- View, Create, Edit, Delete, Approve, Print, Export
★ user_permissions          id, company_id, company_user_id, feature_code, action_code, granted
                            -- unique (company_user_id, feature_code, action_code)
```

The 300-checkbox problem is solved **in the UI, not in the schema**: the permission grid offers bulk
toggles (tick an entire module row, tick an entire action column, copy the grid from another user as
a one-off pre-fill). Every one of those writes individual `user_permissions` rows. Nothing is
persisted as a reusable bundle, so editing one user never changes another — which is precisely the
property §7 is asking for.

### 3.4 Settings & configuration  ★ (entire section is new)

Every effective-dated business rule (§11) lives in one versioned store rather than a column per rule.

```
★ setting_definitions       key (PK), data_type, scope (company|branch), module,
                            description, default_value      -- reference data, seeded
★ setting_values            id, company_id, branch_id (null = company-wide), key,
                            value (jsonb), valid_from, valid_to (null = open),
                            is_active, created_by, created_at
★ document_number_sequences id, company_id, branch_id, document_type, prefix, next_number,
                        ⚠   year, padding    -- unique (company_id, branch_id, document_type, year)
★ document_templates        id, company_id, document_type, logo_file_id, header, footer,
                            terms, show_vat, show_bank_details, signature_file_id
★ approval_rules            id, company_id, rule_type, threshold_value, approver_permission,
                            valid_from, valid_to, is_active   -- e.g. discount > 10% needs approval
★ approvals                 id, company_id, entity_type, entity_id, rule_id, requested_by,
                            requested_at, status, decided_by, decided_at, reason
```

Resolution is always `GetValueAsOf(key, transactionDate)`, so a tax change tomorrow cannot rewrite
an invoice raised today.

### 3.5 Master data

```
  product_categories        id, company_id, name, parent_id
★ brands                    id, company_id, name
  products                  id, company_id, item_code, sku, barcode, name, category_id, brand_id,
                            model, specifications (jsonb), unit, tracking_mode (none|serial|batch),
                          ★ product_kind (product|service|spare_part),
                          ★ condition (brand_new|refurbished),
                            tax_class_id, purchase_price, selling_price, reorder_level,
                          ★ warranty_months, is_active        -- unique (company_id, sku)
  serials                   id, company_id, product_id, serial_no, status, current_warehouse_id ⚠,
                            purchase_cost, supplier_id, goods_receipt_line_id,
                          ★ sold_invoice_line_id, warranty_until   -- unique (company_id, serial_no)
★ serial_events             id, serial_id, event_type, reference_type, reference_id, at, notes
                            -- the §18 history: purchased → received → sold → repaired → returned
  customers                 id, company_id, code, name, ★ customer_type (walkin|individual|corporate),
                            company_name, contact, email, phone, address, tax_number,
                            credit_limit, payment_terms, ★ price_tier_id, ★ balance
  suppliers                 id, company_id, code, name, ★ supplier_type (local|overseas|repair_vendor),
                            country, default_currency, payment_terms, tax_number, lead_time_days
  tax_rates                 id, company_id, name, percent, is_default,
                        ⚠ ★ valid_from, valid_to, is_active
★ price_tiers               id, company_id, name    -- retail, wholesale, corporate (§31)
  price_lists               id, company_id, price_tier_id, name, currency_code, valid_from, valid_to
  price_list_items          id, price_list_id, product_id, unit_price
★ price_history             id, company_id, product_id, price_type, old_price, new_price,
                            changed_by, changed_at, valid_from
★ discounts                 id, company_id, discount_type (product|customer), product_id (null),
                            customer_id (null), method (percent|fixed), value, max_value,
                            valid_from, valid_to, is_active
★ payment_methods           id, company_id, name, kind (cash|bank|card|cheque|online|custom),
                            requires_reference, is_active, valid_from, valid_to
  currencies                code (PK), name, symbol       -- reference data, not tenant-scoped
  fx_rates                  id, company_id, currency_code, rate_to_base, rate_date
```

### 3.6 Inventory  ⚠ (re-keyed from branch to warehouse)

```
  stock_movements           id, company_id, branch_id, ⚠ warehouse_id, product_id, serial_id (null),
                            movement_type, quantity (signed), unit_cost, reference_type,
                            reference_id, occurred_at, created_by
  stock_balances            company_id, ⚠ warehouse_id, product_id, quantity, ★ reserved_quantity,
                            average_cost      -- PK (company_id, warehouse_id, product_id)
★ stock_reservations        id, company_id, warehouse_id, product_id, quantity,
                            reference_type (quote|sales_order), reference_id,
                            expires_at, released_at, status              -- §20
  stock_counts              id, company_id, warehouse_id, status, counted_at,
                            approved_by, approved_at
  stock_count_lines         id, stock_count_id, product_id, serial_id (null),
                            counted_qty, system_qty, variance
★ stock_transfers           id, company_id, from_warehouse_id, to_warehouse_id, number,
                            status, shipped_at, received_at
★ stock_transfer_lines      id, stock_transfer_id, product_id, serial_id (null), quantity
★ stock_adjustments         id, company_id, warehouse_id, number, reason, approved_by, at
★ stock_adjustment_lines    id, stock_adjustment_id, product_id, serial_id (null),
                            quantity, unit_cost
★ barcode_print_jobs        id, company_id, source_type (product|grn), source_id, template,
                            copies, printed_by, printed_at             -- §17
```

`stock_movements` stays append-only and is the source of truth. `stock_balances` is a cache written
in the **same transaction** as the movement. `available = quantity - reserved_quantity` — that
subtraction is what §20's "prevent overselling" actually means.

**Historical stock (§19)** — "view stock on a previous date" is answered by replaying
`stock_movements` up to that date, not by a snapshot table. Opening / purchases / sales / transfers /
adjustments / repairs / closing all fall out of `movement_type` grouping.

### 3.7 Purchasing & imports

```
  purchase_orders           id, company_id, branch_id, supplier_id, number, status,
                            currency_code, fx_rate, subtotal, tax_total, total, expected_at
  purchase_order_lines      id, purchase_order_id, product_id, quantity, unit_price, tax_rate_id
  goods_receipts            id, company_id, branch_id, ⚠ warehouse_id,
                        ⚠   purchase_order_id (NULLABLE — §25 direct purchase has no PO),
                            supplier_id, number, received_at
  goods_receipt_lines       id, goods_receipt_id, product_id, quantity, unit_cost,
                            ★ landed_unit_cost
★ goods_receipt_serials     id, goods_receipt_line_id, serial_no       -- §27 capture at receipt
  supplier_invoices         id, company_id, supplier_id, number, goods_receipt_id (null),
                            total, currency_code, fx_rate, due_date, status
  supplier_payments         id, company_id, supplier_id, amount, currency_code, paid_at,
                            payment_method_id, reference, remarks
★ supplier_payment_alloc    id, supplier_payment_id, supplier_invoice_id, amount
  import_shipments          id, company_id, supplier_id, reference, status, apportion_basis,
                            currency_code, fx_rate, shipped_at, arrived_at, ★ tracking_number
  shipment_costs            id, import_shipment_id, cost_type (freight|insurance|customs|clearing),
                            amount, currency_code, fx_rate
  shipment_purchase_orders  import_shipment_id, purchase_order_id
```

**Landed cost (§26):** `shipment_costs` converted to base currency, apportioned across receipt lines
by `apportion_basis`, written into `goods_receipt_lines.landed_unit_cost`, and *that* is the
`unit_cost` on the resulting `stock_movement`. Inventory holds landed cost, not invoice cost.
**Basis is unanswered** — see §7 question Q2.

### 3.8 Sales

```
  quotes                    id, company_id, branch_id, customer_id, number, status,
                            valid_until, currency_code, subtotal, tax_total, total
  quote_lines               id, quote_id, product_id, quantity, unit_price, discount,
                            tax_percent_snapshot
  sales_orders              id, company_id, branch_id, customer_id, quote_id (null), number,
                            status, currency_code, fx_rate, subtotal, tax_total, total
  sales_order_lines         id, sales_order_id, product_id, quantity, unit_price,
                            discount, tax_percent_snapshot
  deliveries                id, company_id, sales_order_id, warehouse_id ⚠, number, delivered_at
  delivery_lines            id, delivery_id, product_id, serial_id (null), quantity
  invoices                  id, company_id, branch_id, customer_id, sales_order_id (null),
                            number, issued_at, due_date, currency_code, fx_rate,
                            subtotal, tax_total, total, paid_amount, status
  invoice_lines             id, invoice_id, product_id, serial_id (null), quantity, unit_price,
                            discount, tax_percent_snapshot, tax_amount, line_total, unit_cost
★ payments             ⚠    id, company_id, customer_id, number, amount, currency_code,
                            paid_at, remarks     -- header; NO single invoice_id (§23)
★ payment_lines        ⚠    id, payment_id, payment_method_id, amount, reference
                            -- §23 "multiple payment methods" on one sale
★ payment_allocations  ⚠    id, payment_id, invoice_id, amount
                            -- one payment across several invoices; partial payment
  credit_notes              id, company_id, customer_id, invoice_id (null), number, issued_at,
                            reason, subtotal, tax_total, total, ★ settlement_type
  credit_note_lines         id, credit_note_id, invoice_line_id (null), product_id,
                            serial_id (null), quantity, unit_price, tax_percent_snapshot
★ customer_credit_ledger    id, company_id, customer_id, entry_type (issued|consumed|refunded),
                            amount, reference_type, reference_id, at   -- §24 store credit
★ returns                   id, company_id, customer_id, invoice_id, number, received_at,
                            resolution (exchange|store_credit|credit_note|cash_refund|bank_refund)
★ return_lines              id, return_id, invoice_line_id, product_id, serial_id (null),
                            quantity, condition, restock (bool)
```

**⚠ The payments redesign is forced by §23.** The current `payments.invoice_id` cannot express
"one sale settled by cash + card", nor "one payment against three invoices". Header + method lines +
allocations is the standard shape and it is not optional.

### 3.9 Repairs

```
  repair_tickets            id, company_id, branch_id, number, customer_id,
                            device_product_id (null), device_serial (free text),
                            reported_fault, accessories, condition_notes, status,
                            estimated_cost, approved_at, approved_by,
                            warranty_invoice_line_id (null), ★ warranty_type,
                            received_at, promised_at, delivered_at, technician_id
  repair_diagnoses          id, repair_ticket_id, technician_id, findings,
                            recommended_action, estimated_cost, at
  repair_parts              id, repair_ticket_id, product_id, serial_id (null), warehouse_id ⚠,
                            quantity, unit_cost, unit_price, is_chargeable
  repair_labour             id, repair_ticket_id, technician_id, hours, hourly_rate,
                            description, is_chargeable
  repair_charges            id, repair_ticket_id, invoice_id (null)
★ repair_outsourcing        id, repair_ticket_id, vendor_supplier_id, sent_at, expected_at,
                            received_at, cost, currency_code, status, notes      -- §29
★ repair_status_history     id, repair_ticket_id, from_status, to_status, changed_by, at, notes
★ warranties                id, company_id, warranty_type (manufacturer|supplier|shop),
                            source_type (invoice_line|goods_receipt_line|serial),
                            source_id, serial_id (null), product_id, starts_on, ends_on   -- §30
★ warranty_claims           id, company_id, warranty_id, repair_ticket_id (null),
                            claimed_at, status, outcome, notes
```

Repair workflow (§28): `Received → Diagnosis → Customer Approval → Repair → Testing → Ready →
Delivered`, enforced as a domain state machine with every transition written to
`repair_status_history`.

### 3.10 Finance

```
★ expenses                  id, company_id, branch_id, category_id, number, amount,
                            currency_code, expense_date, payment_method_id, supplier_id (null),
                            reference, remarks, attachment_file_id           -- §34
★ expense_categories        id, company_id, name, parent_id  -- rent, transport, import, repair
★ cash_accounts             id, company_id, branch_id, name, currency_code, opening_balance
★ bank_accounts             id, company_id, name, bank_name, account_number, currency_code,
                            opening_balance
★ cash_movements            id, company_id, account_type (cash|bank), account_id, direction,
                            amount, currency_code, reference_type, reference_id, occurred_at
```

**DECIDED: no general ledger.** No chart of accounts, no journal entries, no double-entry. §33's
receivables, payables, cash, bank and expenses are all derivable from the transactional tables above.

§35's **Profit & Loss** is therefore a *computed* report, not a GL extract:

```
revenue   = Σ invoice_lines.line_total            (less credit notes)
COGS      = Σ invoice_lines.unit_cost * quantity  (weighted-average cost at time of sale)
expenses  = Σ expenses.amount
profit    = revenue - COGS - expenses
```

**The honest caveat, recorded deliberately:** this will not tie out line-for-line to an accountant's
books, because it has no journals to reconcile against. That is an accepted trade — §44 already lists
"Accounting systems" as a planned integration, so the exit path is an **export** to Xero / QuickBooks /
Zoho rather than a GL of our own. If the business later needs statutory accounts produced *from*
TechStorePro, that is a new project, not a patch.

### 3.11 Cross-cutting

```
  audit_log                 id, company_id, user_id, entity_type, entity_id, action,
                            old_values (jsonb), new_values (jsonb), ip_address, at   -- §9
★ notes                     id, company_id, entity_type, entity_id, body, is_private,
                            created_by, created_at, updated_at                        -- §38
★ attachments               id, company_id, entity_type, entity_id, file_name, content_type,
                            size_bytes, storage_key, uploaded_by, uploaded_at         -- §39
★ notification_templates    id, company_id, event_code, subject, body, is_active       -- §13
★ notification_log          id, company_id, event_code, recipient, subject, sent_at,
                            status, error_details                                      -- §13
★ company_email_settings    company_id, smtp_host, smtp_port, use_ssl, username,
                            password_encrypted, sender_email, sender_name,
                            reply_to, signature, notifications_enabled (jsonb)
★ import_jobs               id, company_id, entity_type, file_name, status, total_rows,
                            success_rows, error_rows, error_report_file_id, created_by  -- §42
★ idempotency_keys          id, company_id, key, endpoint, request_hash, response_body,
                            status_code, created_at    -- api-design §5, currently unimplemented
```

**Soft delete (§10)** applies to every important record via the existing `ISoftDeletable`, but the
interface must gain a **`DeletedReason`** field — see §7 conflict H.

**Entity count: ~95 tables.** Roughly 40 of them are new relative to the current
`database-design.md`.

---

## 4. Backend architecture

### 4.1 Layers (unchanged from P0 — the foundation is sound)

```
API  ──►  Infrastructure  ──►  Application  ──►  Domain
 └──────────────────────────────────┘
        (API also references Application, for DI and MediatR)
```

`Domain` has no package dependencies. `Application` defines the interfaces; `Infrastructure` and
`API` implement them.

### 4.2 Module structure inside the layers

Modules are folders, consistently, in all four projects. This is the seam that would let a module be
extracted later.

```
TechStorePro.Domain/
  Common/          BaseEntity, AuditableEntity, TenantEntity, ITenantScoped, ISoftDeletable
  Platform/        SubscriptionPlan, Subscription, ...
  Identity/        Company, User, CompanyUser, Branch, Warehouse, UserPermission
  Configuration/   SettingValue, DocumentNumberSequence, ApprovalRule
  Catalog/         Product, Category, Brand, Customer, Supplier, PriceList, TaxRate
  Inventory/       StockMovement, StockBalance, Serial, StockReservation, StockCount
  Purchasing/      PurchaseOrder, GoodsReceipt, SupplierInvoice, ImportShipment
  Sales/           Quote, SalesOrder, Invoice, Payment, CreditNote, Return
  Repairs/         RepairTicket, RepairPart, RepairLabour, Warranty
  Finance/         Expense, CashAccount, BankAccount, CashMovement

TechStorePro.Application/
  Common/          Behaviours, Interfaces, Exceptions, Models    (exists)
  <Module>/
    Commands/      CreateInvoice/{Command, Handler, Validator}
    Queries/       GetInvoiceById/{Query, Handler}
    Dtos/
    Services/      module services (IStockLedger, ILandedCostCalculator, ...)

TechStorePro.Infrastructure/
  Persistence/     ApplicationDbContext, Configurations/<Module>/, Migrations/
  Identity/        JwtTokenService, PasswordHasher, PermissionEvaluator
  Configuration/   SettingsProvider, DocumentNumberGenerator
  Files/           LocalFileStorage, S3FileStorage
  Email/           SmtpEmailSender, TemplateRenderer
  Documents/       QuestPdfRenderer, BarcodeRenderer, ExcelExporter
  Jobs/            HangfireJobScheduler, BillingJob, ReminderJob, BalanceAuditJob
```

### 4.3 The request pipeline

Every request enters Application as a MediatR command/query. Behaviours run in this order — the
order matters:

```
HTTP → [Exception] → [Auth] → [RateLimit] → Controller → MediatR
                                                            │
   1. LoggingBehaviour          trace id, user, company
   2. EntitlementBehaviour      is this feature in the company's plan? (§4)          ★
   3. PermissionBehaviour       does this user hold (feature, action)? (§7)          ★
   4. ValidationBehaviour       FluentValidation                          [exists]
   5. IdempotencyBehaviour      replay the stored response for a repeat key ★
   6. TransactionBehaviour      one DB transaction per command            ★
   7. AuditBehaviour            harvest old/new values from ChangeTracker ★
        │
        └─► Handler → Domain → DbContext (tenant filter + audit stamps) [exists]
```

Entitlement before permission is deliberate: a feature the company has not *bought* must be
invisible even to a user who has been *granted* it.

### 4.4 Authorisation: the permission engine (§7)

Permissions are `(feature, action)` pairs, e.g. `sales.invoice` × `Approve`. They are **not** in the
JWT — a user with 300 permissions would blow the token size, and a revoked permission must take
effect immediately, not at the next token refresh.

- The token carries `sub`, `email`, `company_id` only.
- Permissions are loaded per request from `user_permissions`, cached per (user, company) with a
  short TTL and invalidated on any permission change.
- `PermissionBehaviour` reads a `[RequiresPermission("sales.invoice", Action.Approve)]` attribute on
  the command and denies with `403`.
- The same feature list is returned by `GET /api/v1/auth/me` so the frontend can hide what the user
  cannot do. **UI hiding is cosmetic; the server check is the real one.**

### 4.5 The stock ledger (highest-risk component)

Every stock change goes through one service — `IStockLedger` — and nothing else may write
`stock_movements` or `stock_balances`:

```
IStockLedger.Post(movement)      // append movement + update balance, SAME transaction
IStockLedger.Reserve(...)        // §20 — increments reserved_quantity
IStockLedger.Release(...)
IStockLedger.Available(...)      // quantity - reserved_quantity
```

Concurrency: the balance row is locked (`SELECT … FOR UPDATE`) before it is adjusted, so two
concurrent sales of the last unit cannot both succeed. A nightly `BalanceAuditJob` recomputes
balances from movements and alerts on any disagreement — the ledger is the source of truth and the
cache must always be able to prove itself.

### 4.6 Costing

**DECIDED: weighted average.** Moving average cost per (product, warehouse), recalculated on every
receipt:

```
new_average = (qty_on_hand * old_average + qty_in * landed_unit_cost)
              / (qty_on_hand + qty_in)

COGS on sale = quantity * average_cost   (snapshotted onto invoice_lines.unit_cost)
```

It is held behind `ICostingStrategy` anyway, so FIFO remains a second implementation if the business
ever needs it — but no FIFO cost-layer table is built, and weighted average is the only strategy
shipped. The average is stored on `stock_balances.average_cost` (already in the P0 design) and is
recomputed **inside the same locked transaction** as the movement that changes it.

Note the interaction with landed cost: the average must be raised by the **landed** unit cost, not
the invoice cost, or every import silently under-values stock. §4.5 and §3.7 depend on each other here.

### 4.7 Document numbering (§5)

Gapless per `(company, branch, document_type, year)`. The sequence row is locked with
`SELECT … FOR UPDATE` inside the same transaction as the document insert, so a rolled-back
transaction does not burn a number. **Branch-level, not company-level — see §7 conflict D.**

### 4.8 Background jobs

| Job | Schedule | Section |
| --- | --- | --- |
| Generate monthly subscription invoices | monthly | §4 |
| Send invoice / reminder / expiry / suspension emails | daily | §4, §13 |
| Payment reminders and customer statements | daily | §13, §33 |
| Recompute stock balances and assert agreement | nightly | §19 |
| Expire stale stock reservations | hourly | §20 |
| Warranty expiry notifications | daily | §30 |
| Bulk Excel import processing | on demand | §42 |
| Database backup verification | daily | §43 |

---

## 5. Frontend architecture

Next.js 16 App Router, TypeScript, Tailwind v4 — as built in P0.

### 5.1 Route structure

```
src/app/
  (auth)/                    login, register, forgot-password        — no shell
  (platform)/                platform admin console (§2)             — platform shell
    companies/ plans/ billing/ usage/ logs/
  (app)/                     the ERP                                 — app shell
    layout.tsx               sidebar + topbar + company switcher + global search + shortcuts
    dashboard/                                                        §36
    catalog/                 products, categories, customers, suppliers
    inventory/               stock, movements, transfers, adjustments, counts, barcodes
    purchasing/              orders, receipts, supplier-invoices, imports
    sales/                   pos, quotes, orders, invoices, payments, returns, credit-notes
    repairs/                 jobs, diagnosis, outsourced, warranty
    finance/                 receivables, payables, cash, bank, expenses
    reports/                                                          §35
    settings/                company, branches, warehouses, users, permissions,
                             taxes, prices, discounts, payment-methods, numbering,
                             documents, email, templates               §11 §12 §13
```

### 5.2 Layers

```
app/          routes; server components fetch, client components interact
features/     one folder per module: components, hooks, api calls, schemas
components/   ui/ (primitives)  layout/ (shell)  data/ (table, filters, pagination)
lib/          api-client (exists), env (exists), permissions, formatting, shortcuts
types/        api.ts — GENERATED from the OpenAPI document, never hand-written
```

**The three primitives that everything else reuses** (build them once, in P2, properly):

1. **`<DataTable>`** — server-side paging, search, sort, column visibility, row actions, export.
   Every list screen in §14–§35 is this component with a different column set.
2. **`<EntityForm>`** — react-hook-form + zod, resolves the RFC 7807 `errors` object onto fields, so
   server validation lands on the right input automatically.
3. **`<Can feature="sales.invoice" action="Approve">`** — permission-aware rendering, driven by the
   permission list from `/auth/me`.

Time spent here pays back across ~40 screens.

### 5.3 Data layer

- **TanStack Query** for server state (caching, invalidation, optimistic updates). The POS screen
  (§22) needs sub-100ms product lookup — that is a cache, not a round trip per keystroke.
- Money is a **decimal string + currency code** end to end (api-design §1). It is never parsed into a
  JS `number` — `0.1 + 0.2` is exactly the bug an ERP cannot ship.
- Types are generated from `/openapi/v1.json`; a drifted client type becomes a build error.

### 5.4 Specific UI requirements

| Requirement | Approach |
| --- | --- |
| §36 animated dashboard | Server-rendered widget data + client chart components; layout stored in `user_preferences` |
| §37 global search | One `⌘K` palette → `GET /api/v1/search?q=` across product/serial/barcode/customer/invoice/repair |
| §40 keyboard shortcuts | A shortcut registry provider; POS and invoice screens are keyboard-first |
| §41 theme & preferences | Tailwind `dark:` + `prefers-color-scheme`, overridden by `user_preferences.theme` |
| §22 POS | Dedicated full-screen route, barcode-scanner input focus trap, offline-tolerant cart in local storage |
| §17 barcode printing | Server renders the label PDF; browser prints. Thermal support = correct page geometry |

---

## 6. Development phases

Sequenced by dependency, not by importance. Each phase ends with something the shop could actually
use. **Phase boundaries are where the cross-tenant denial test runs** — a user of company A, given
company B's ids, must get a 404 from every endpoint added in that phase.

| Phase | Scope | Blocked on |
| --- | --- | --- |
| **P0** ✅ | Foundation: layering, tenancy, error contract, health, Docker | — |
| **P1** ✅ | **Identity, tenancy, permissions, settings, audit.** Company registration, branches, **warehouses** (owned + shared), users, the per-user (feature × action) permission engine, JWT + refresh + login history + lockout, the effective-dated Settings engine, document numbering, audit trail, soft delete + restore. **Done** — 16 domain tests + 6 cross-tenant isolation tests (Testcontainers) green; auth, permissions, audit, soft delete and settings verified against a running API. | — |
| **P2** ✅ | **Master data.** Products, categories, brands, customers, suppliers, tax rates, price tiers/lists, discounts, payment methods, currencies, FX. **Done** — 14 tables, an `IPriceResolver` that reports *why* a customer pays what they pay, `<DataTable>` + `<EntityForm>` primitives, TanStack Query adopted. 47 tests green. | — |
| **P3** | **Inventory — the spine.** Stock ledger, warehouses, balances, serial lifecycle, barcodes/QR + label printing, reservations, transfers, adjustments, stock counts, historical stock, **weighted-average costing**. ← **next** | ✅ clear |
| **P4** | **Purchasing & imports.** PO (optional), GRN with serial capture, supplier invoices/payments, import shipments, **landed cost**, FX. | **Q2 (landed-cost basis)** |
| **P5** | **Sales.** POS, quotes → orders → delivery (serial picking) → invoice, multi-method payments, returns, credit notes, store credit, discount approval. | **Q6 (tax), Q8 (FX sales)** |
| **P6** | **Repairs.** Intake, diagnosis, approval, parts (consumes P3 stock), labour, outsourced repair, warranty linked to the P5 sale, invoicing, profitability. | P3 + P5 |
| **P7** | **Finance, reporting, dashboard.** AR/AP ageing, statements, cash/bank, expenses, computed P&L, the §35 report set, §36 dashboard, §37 global search. | ✅ clear |
| **P8** | **SaaS platform.** Plans, subscriptions, entitlement enforcement, billing, invoices, platform admin console, onboarding. | **Q7 (payment gateway)** |
| **P9** | **Hardening & integrations.** Backup/restore (§43), rate limiting, RLS defence-in-depth, Excel import/export (§42), integration API surface (§44). | — |

**Note on P8:** the spec presents SaaS management as §2 — near the top — but it is built last on
purpose. It is only worth building once tenants have something to pay for. The *data model* for
subscriptions is created in P1 (a company must have a subscription record from day one); only the
billing, enforcement and admin console wait for P8. Entitlement *checks* are in the pipeline from
P1 and simply return "allowed" until P8 fills the plans in.

**Definition of done, every phase** (from development-plan.md, still correct): domain unit tests;
integration tests against real Postgres via Testcontainers including the cross-tenant denial case;
migration written and backward-compatible; OpenAPI regenerated and frontend types matched; no secret
in the repo; docs updated in the same PR.

---

## 7. Conflicts and missing requirements

### 7.1 Conflicts — these must be resolved before the phase they touch

| # | Conflict | Where | Impact | Recommendation |
| --- | --- | --- | --- | --- |
| **A** | **Dead cross-references.** `development-plan.md` and `database-design.md` point at "requirements.md §6 open questions" and "§4.8" — the new requirements.md has no such sections. | Docs | Docs cite requirements that no longer exist; the open questions that blocked P3/P4/P7 are now **undocumented**, but still unanswered. | Fold the open questions into requirements.md as a new section (they are Q1–Q9 below). |
| **B** ✅ | **"No fixed roles" (§7) vs the `roles` / `role_permissions` / `company_user_roles` tables and the JWT `role` claim** in database-design.md and api-design.md §3. | Permissions | The built design resolves role *names*; the spec forbids them. | **RESOLVED: strictly per-user.** Roles tables dropped entirely; `user_permissions` on (feature, action) per §3.3. No stored templates. `role` claim removed from the JWT. Bulk-assign is a UI affordance only. |
| **C** ✅ | **Multi-warehouse (§Core, §19) is absent from the schema.** `stock_movements` / `stock_balances` are keyed by `branch_id`; there is no `warehouses` table. But §5 gives a branch a *"default warehouse"*, which only makes sense if warehouses exist and are many-per-branch. | Inventory | Fundamental. Stock keys change; every later module inherits them. Retrofitting after P3 means rewriting the ledger. | **RESOLVED: both, configurably.** `warehouses.branch_id` nullable (set = branch-owned, null = company-shared) + `branch_warehouses` access map. Stock re-keyed to `warehouse_id`; `branch_id` stays on movements for reporting. See §3.2. |
| **D** | **Document numbering scope.** §5 lists numbering under *Branch details*; database-design.md §2 says "per-company, per-document-type sequence". | Config | An invoice number would collide or be non-gapless per branch. | Sequence key = (company, branch, document_type, year). A branch-agnostic doc type simply uses the company's default branch. |
| **E** ✅ | **Profit & Loss report (§35) without a general ledger (§33).** §33 asks only for receivables, payables, cash, bank, expenses — no chart of accounts, no journals. | Finance | P&L conventionally needs a GL. Without one it is a *computed* report (revenue − COGS − expenses) that will not tie out to an accountant's books. | **RESOLVED: no GL.** Computed P&L (revenue − COGS − expenses), export to an external accounting package (§44). The non-reconcilable caveat is accepted and recorded in §3.10. |
| **F** | **`tax_rates` has no validity dates** in database-design.md, but §11 demands Valid From / Valid To / Active for tax rates, prices, discounts, payment methods, warranty periods and numbering rules — and General Rule 3 forbids historical transactions changing. | Config | A tax rate edit would silently restate every past invoice. | Add validity columns **and** snapshot the tax percent onto `invoice_lines` (§3.8). A foreign key alone is not enough. Both are required. |
| **G** | **`payments.invoice_id` (single) vs §23 "multiple payment methods" and partial payment across invoices.** | Sales | Cannot express cash + card on one sale, nor one payment settling three invoices. | Header (`payments`) + `payment_lines` (per method) + `payment_allocations` (per invoice). See §3.8. |
| **H** | **Soft delete is missing `delete reason`.** §10 requires Delete flag, Deleted user, Deleted date, **Delete reason**, Restore. The built `ISoftDeletable` has the first three and no reason, and there is no restore path. | Foundation | Small, but it is in the already-written P0 code (`Domain/Common/ISoftDeletable.cs`). | Add `DeletedReason` to the interface and a restore command. Cheap now, a migration later. |
| **I** | **GRN without a PO.** §25 says "PR is not required, PO is optional" and defines a Direct Purchase flow (Supplier → GRN → Stock). `goods_receipts.purchase_order_id` is not marked nullable in database-design.md. | Purchasing | Direct purchase — the common case in a shop — would be impossible. | Make `purchase_order_id` nullable; carry `supplier_id` on the GRN directly. |

### 7.2 Open questions

#### Resolved

| # | Question | Decision |
| --- | --- | --- |
| **Q1** ✅ | Costing method: weighted average or FIFO? | **Weighted average.** Moving average per (product, warehouse), raised by *landed* cost. Held behind `ICostingStrategy`; no FIFO cost-layer table is built. §4.6. |
| **Q3** ✅ | Is a general ledger in scope? | **No GL.** Computed P&L (revenue − COGS − expenses); export to an external accounting package. Will not reconcile line-for-line to an accountant's books — accepted. §3.10. |
| **Q4** ✅ | Warehouse model? | **Both, configurably.** `warehouses.branch_id` nullable: set = branch-owned, null = company-shared, with `branch_warehouses` declaring which branches may use a shared one. Stock keyed by `warehouse_id`. §3.2. |
| **Q5** ✅ | "No fixed roles" — how literally? | **Strictly per-user.** No stored templates, no role table, nothing resolves a role name. Bulk assignment is a UI affordance that writes individual rows. §3.3. |

#### Still open — needed before the phase they block

| # | Question | Blocks | Why it is expensive to defer |
| --- | --- | --- | --- |
| **Q2** | **Landed-cost apportionment basis: by value, by weight, or by quantity?** (§26 says "calculate landed cost", never how) | **P4** | Misapplied landed cost silently misprices every sale that follows — and with weighted-average costing now decided, it feeds straight into the moving average, so an error propagates to *all* stock of that product, not just the imported units. Needs worked examples from the business, turned into tests, *before* the code. |
| **Q6** | **Tax model.** One VAT rate or many? Prices tax-inclusive or tax-exclusive? Which jurisdiction (api-design's example uses **AED** — is this UAE)? Is a **tax e-invoicing integration** (FTA / ZATCA / equivalent) required? | **P5** | Tax-inclusive vs exclusive changes every price field and every line calculation. E-invoicing is a certification project, not a feature. |
| **Q7** | **SaaS billing: which payment processor** (Stripe? local gateway? manual bank transfer?), and is signup **self-service with a card**, or manual onboarding? §4 says "Billing / Payments" but names no processor. | **P8** | Determines whether P8 is a two-week module or a PCI-scoped integration. |
| **Q8** | **Multi-currency sales.** §26 covers foreign-currency *purchases*. §23 says payments capture a currency. **Can a customer be invoiced in a foreign currency**, or is selling always in the company's base currency? | **P5** | FX gain/loss on receivables is a whole sub-module. |
| **Q9** | **Deployment target and file storage.** Cloud (Azure/AWS) or self-hosted? Where do §39 attachments and §28 repair photos live? Is there a data-residency constraint? §43 wants automated backups — of what, to where, with what RPO/RTO? | **P1** (storage), **P9** (backup) | `IFileStorage` abstracts the *code*, but the §4 storage limits and §43 backup guarantees need a real target. |

### 7.3 Smaller gaps worth confirming (not blocking)

- **Barcode symbology** (§17): Code128? EAN-13? Both? And are thermal printers driven by
  browser print (PDF with correct geometry) or direct ESC/POS?
- **Offline POS** (§22): must the counter keep selling when the network drops? This is a large
  architectural commitment (local-first sync) and the spec does not say.
- **Localisation**: single language, or Arabic/RTL? Nothing in the spec mentions i18n, but the
  currency hint (AED) suggests a market where it may be expected.
- **Audit log retention** (§9): audit rows will outgrow every other table. How long are they kept?
- **Technician scheduling** (§28): repairs have a `technician_id` but the spec asks for no capacity
  planning or workload view. Confirm that is intentional.
- **Approval workflow depth** (§32): single approver, or multi-level chains?
- **`users` are global, not tenant-scoped** — one login, many companies (already true in P0). Confirm
  this is intended: it means an email address is unique across the whole *platform*, not per company.

---

## 8. Status

**P1 is complete and verified.** Delivered:

- `requirements.md` §45 records the four decisions; §46 records the five questions still open,
  which fixes the dead cross-references (conflict A).
- `database-design.md` and `api-design.md` reconciled with this document (conflicts B, C, D, F, G, I).
- `ISoftDeletable` gained `DeletedReason` (conflict H) — the only change to pre-existing code.
- Backend: 16 tables, 8 features × 7 actions, the auth set, and CRUD for branches, warehouses, users
  and settings. 0 warnings, 0 errors.
- Tests: **16 domain unit tests + 6 cross-tenant isolation tests** against a real PostgreSQL via
  Testcontainers. The cross-tenant denial test is the gate on P2, and it passes.
- Frontend: login, route guard, app shell, company switcher, permission-aware navigation, and the
  per-user permission grid.

### Known gaps, recorded rather than hidden

| Gap | Why it is acceptable for now | When it must be closed |
| --- | --- | --- |
| The refresh token is held in `localStorage`. | An XSS payload could read it. The access token is memory-only, so the blast radius is one refresh token, not a live session — but this is a real weakness, not a clean design. | **P9.** Move it to an httpOnly, SameSite cookie set by the API. |
| No rate limiting on `/auth/*`. | Lockout after N failed attempts blunts online brute force, but not a distributed one. | **P9** — api-design.md §7 already calls for it. |
| Migrations are applied by the API at start-up in Development. | Fine for one developer. Two production instances starting together would race. | Before the first real deploy: run migrations as a separate deploy step. |
| No CI. | The tests exist and pass locally; nothing yet stops a red build from being merged. | **Next** — build, test, `dotnet format`, and a check that no model change lacks a migration. |
| Postgres RLS is not enabled. | The EF global query filter is enforced centrally and proven by tests. RLS is the belt to that pair of braces — it would stop even a raw SQL query that bypassed the filter. | Before production, per database-design.md §1. |

### Next

**P2 — master data.** Products, categories, brands, customers, suppliers, tax rates, price tiers,
discounts, payment methods, currencies and FX. The three reusable frontend primitives
(`<DataTable>`, `<EntityForm>`, `<Can>`) land here and every later module reuses them.

**Q2 (landed-cost basis) must be answered before P4**, and it is now sharper than it was: with
weighted-average costing decided, a landed-cost error spreads into the moving average of *all* stock
of a product, not just the imported units.
