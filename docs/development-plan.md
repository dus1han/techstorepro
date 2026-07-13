# Development plan

> Status: **P0–P3 complete. CI is running. P4 (purchasing) is next but blocked on Q2 (landed-cost basis).**
>
> Phases are numbered P0–P9 and match [architecture.md §6](architecture.md). The earlier M0–M8
> milestone numbering is gone: it mapped almost one-to-one, and keeping two schemes alive only
> invited someone to cite the wrong one. Where this file and architecture.md disagree, architecture.md
> wins.

## Principles

1. **Tenancy is not a feature you add later.** It went into the foundation because retrofitting a
   `company_id` onto a live schema is a migration nightmare and a leak waiting to happen.
2. **Build the spine before the limbs.** Inventory is what sales, purchasing and repairs all touch.
   It comes before any of them, or the stock logic gets written three times and reconciled forever.
3. **Every phase is shippable.** A phase ends with something the shop could actually use, not a
   half-wired layer.
4. **Answer the open question before you build the phase it blocks** (see
   [requirements.md §46](requirements.md)). Costing is decided (weighted average);
   **landed-cost basis is the expensive one still open.**
5. **The cross-tenant denial test is a gate, not a nicety.** It runs at every phase boundary. One
   database holds every company's data; that test is the only thing standing between them.

---

## Phases

### P0 — Foundation ✅ done

Solution structure, Clean Architecture layering, tenancy plumbing, error contract, health check,
Docker Postgres, docs. No business modules.

**Exit criteria (met):** build clean; API boots; `/health` returns 200; frontend builds.

### P1 — Identity, tenancy, permissions, settings, audit ✅ done

Nothing else could be built first: every other module needs a `company_id` on the token and a user
to attribute changes to.

**Delivered:**

- **Entities (16 tables):** `Company`, `Branch`, `Warehouse`, `BranchWarehouse`, `User`,
  `CompanyUser`, `CompanyUserBranch`, `UserPermission`, `RefreshToken`, `LoginHistory`, `Feature`,
  `SettingDefinition`, `SettingValue`, `DocumentNumberSequence`, `AuditLog`.
  **There is no `Role` entity and no `role` claim** — requirements §45 D4.
- **Permissions (§7):** per-user `(feature, action)` grants across 8 features × 7 actions. Resolved
  per request from the database, never from the token, so a revoked permission takes effect on the
  caller's *next* request rather than whenever their token happens to expire. The owner holds
  everything implicitly, which is what stops a company revoking its own last administrator and
  bricking itself.
- **Auth (§8):** register, login, refresh with rotation and **replay detection** (a replayed token
  kills the whole chain), logout, switch-company, `/auth/me`. PBKDF2-SHA256 at 210k iterations;
  failed-login lockout; login history with IP and device.
- **Warehouses (D2):** branch-owned (`branch_id` set) or company-shared (`branch_id` null) with an
  explicit per-branch access list. "Shared" never silently means "any branch may drain it".
- **Settings (§11):** effective-dated and versioned, never overwritten, so a document raised in
  March still resolves March's value.
- **Document numbering (§5):** gapless per (company, branch, type, year), `SELECT … FOR UPDATE`
  inside the caller's transaction so a rollback returns the number instead of burning it.
- **Audit trail (§9):** written from the EF change tracker — old and new values, user, IP — so no
  handler can forget it. The password hash is never recorded.
- **Soft delete (§10):** mandatory delete reason, plus restore.
- **Frontend:** login, route guard, app shell, company switcher, permission-filtered navigation, and
  the per-user permission grid with bulk row/column toggles (a UI affordance that writes individual
  grants — nothing is stored as a reusable bundle).

**Tests: 22 green.** 16 domain unit tests, plus **6 cross-tenant isolation tests against a real
PostgreSQL** (Testcontainers): company A cannot read company B's row even holding its exact id,
cannot update it, and a row A creates never surfaces in B. That suite is the gate on P2 and it passes.

### P2 — Master data ✅ done

**Delivered (14 new tables, 30 in total):**

- **Products** — serial / batch / no tracking; product, service or spare part; brand-new or
  refurbished; barcode, SKU and item code; margin computed, never stored.
- **Customers and suppliers** — walk-in / individual / corporate; local / overseas / repair vendor.
- **Pricing that actually prices.** Price tiers, price lists, and an `IPriceResolver` that resolves
  customer tier → default tier → product price, and **reports which one it used**. Verified live: a
  wholesale customer pays 3,600 where a walk-in pays 3,950, and the system can say why.
- **Effective-dated** tax rates, discounts and payment methods; **price history** per product.
- **Currencies and FX** — a starter ISO set seeded, one rate per currency per day.

**Domain rules enforced on the entity, not in a validator** — so no code path can dodge them:

| Rule | Why |
| --- | --- |
| A service cannot be serial-tracked | A stock ledger entry for "one hour of labour" is meaningless |
| A walk-in cannot be given a credit limit | There is no account to bill |
| Two price lists for one tier cannot overlap | "What does this customer pay today?" would have two answers |
| A price floor cannot exceed the price | Every sale would need approval from day one |
| A fixed discount cannot exceed the line | The customer would be owed money for buying something |
| A discount's approval ceiling cannot sit below the discount | Every use of the rule would stall in approval |
| A customer or supplier with a balance cannot be retired | It would hide the debt from the receivables report |

**Frontend primitives landed** — `<DataTable>` (server paging, debounced search, sort) and
`<EntityForm>` (binds the API's RFC 7807 `errors` map onto the right inputs, so a validation rule is
written **once**, on the server). Forty later screens reuse both.

**TanStack Query adopted** (architecture.md §5.3 always called for it). Manual `useEffect` + `useState`
fetching was re-fetching on every dependency change, racing its own responses, caching nothing — and
Next 16's React Compiler lint rejects it outright. Query fixed the whole class and deleted code. The
**cache key includes the active company**, so switching company cannot serve tenant A's rows under
tenant B's name — a server-side isolation guarantee that a client cache could otherwise undo.

**Tests: 47 green** (39 domain + 8 cross-tenant). The isolation suite grew with the schema: company B
cannot see A's products, customers or suppliers, gets a 404 fetching A's product by its real id, and
**can reuse the same SKU** — uniqueness is scoped per company, not global. A retired product frees
its SKU, so a mistyped code is not held hostage forever by a soft-deleted row.

### P3 — Inventory (the spine) ✅ done

**Delivered (12 new tables, 42 in total):**

- **The stock ledger.** `IStockLedger` is the single door: nothing else in the system may write
  `stock_movements`, `stock_balances` or a serial's status. Every method **requires an ambient
  transaction and throws without one** — a stock movement is never the only thing a business
  operation does, and a ledger that quietly committed on its own would leave stock moved and the
  document that moved it rolled back.
- **The lock is the design.** The balance row is materialised and locked
  (`INSERT … ON CONFLICT DO NOTHING`, then `SELECT … FOR UPDATE`) before it is read, and the weighted
  average is recomputed *inside* that lock. The upsert is not redundant: `FOR UPDATE` locks nothing
  when the row does not exist yet, and two concurrent first-receipts would both insert.
- **Serial lifecycle** — a state machine, not a status column. `Scrapped` and `ReturnedToSupplier` are
  terminal; a sold unit never silently returns to the shelf. This is what stops the same laptop being
  sold twice, which quantities alone cannot.
- **Reservations** (§20), **transfers** (two movements, so in-transit stock belongs to neither end),
  **adjustments** (post immediately; mandatory reason), **stock counts** (§21: system quantity
  snapshotted at scan time, approval a separate permission because it authorises a write-off),
  **historical stock** (§19), **valuation**, and **barcode/QR label printing** to PDF (§17).
- **`available = quantity − reserved_quantity`.** That subtraction is what "prevent overselling" means,
  and it is enforced under the lock.

**The cache proves itself.** `stock_balances` is a cache of `stock_movements`, so `/inventory/balance-audit`
recomputes it from the ledger — and `InventoryMaintenanceService` runs **the same audit** nightly, per
company, alongside a 15-minute sweep of expired reservations (a forgotten quote would otherwise hold
the last unit off the shelf forever). The audit compares **cost as well as quantity**: a balance whose
units are right and whose average is wrong looks healthy while every sale from that warehouse books
the wrong COGS. It reports and never repairs — a disagreement means something wrote stock outside
`IStockLedger`, and overwriting the evidence would destroy the only trace of it.

**Tests: 142 green** (110 domain + 32 integration), up from 47. The ledger's guarantees are all claims
about what the *database* does — `FOR UPDATE`, `ON CONFLICT`, real transactions — so they are tested
against a real PostgreSQL and not an in-memory provider that would pass while the real thing oversold
the last laptop. The suite proves overselling is refused, that a reservation cannot be double-promised,
that the same serial cannot be sold twice, that a rolled-back transaction leaves no stock, **and that
the audit itself fails when a balance is corrupted behind the ledger's back** — in quantity *or* in
cost. The cross-tenant gate grew to cover all ten new tenant-scoped tables.

### P4 — Purchasing and imports

Purchase orders (optional), goods receipts with serial capture, supplier invoices and payments,
import shipments, **landed cost**, foreign-currency purchases and FX gain/loss.

- 🔴 **Blocked on Q2: the landed-cost apportionment basis** — by value, by weight, or by quantity?
  Needs worked examples from the business, turned into tests, *before* the code.
- This is now sharper than it looks. With weighted-average costing, a landed-cost error does not just
  misprice the imported units — it feeds the moving average and **spreads to all existing stock of
  that product**.
- GRN works **without** a PO: requirements §25 makes the PO optional and defines a direct-purchase
  flow, so `goods_receipts.purchase_order_id` is nullable.

### P5 — Sales

POS, quotes → orders → deliveries (serial picking) → invoices, multi-method payments, returns,
credit notes, store credit, discount approval.

- 🔴 **Blocked on Q6** (tax: inclusive or exclusive? which jurisdiction? e-invoicing required?) and
  **Q8** (can a customer be invoiced in a foreign currency?).
- Payments are header + method lines + allocations — one sale settled by cash *and* card, one payment
  across three invoices. A single `invoice_id` on a payment cannot express either.
- **Serial binding at delivery is what makes P6's warranty flow possible. Do not defer it.**

### P6 — Repairs

Intake, diagnosis, customer approval, parts consumption, labour, outsourced repair, warranty repairs
linked to the original sale, delivery, invoicing, profitability.

- Depends on P3 (parts come out of stock) and P5 (a warranty claim must find the invoice line that
  sold the serial).

### P7 — Finance, reporting, dashboard

Receivables/payables ageing, statements, cash and bank accounts, expenses, the §35 report set, the
§36 dashboard, §37 global search.

- ✅ Resolved: **no general ledger** (§45 D3). P&L is computed (revenue − COGS − expenses) and the
  books export to an external accounting package. It will not reconcile line-for-line to an
  accountant's ledger — an accepted trade, recorded so nobody rediscovers it as a surprise.

### P8 — Platform / SaaS

Subscription plans, per-company entitlement enforcement, billing, invoices, platform admin console,
onboarding.

- 🟡 **Blocked on Q7** (payment processor; self-service card signup or manual onboarding?).
- Deliberately late, even though requirements puts SaaS management at §2. It is only worth building
  once tenants have something to pay for. The *data model* for subscriptions is cheap and can land
  earlier; the billing, enforcement and admin console wait.

### P9 — Hardening and integrations

Backup/restore (§43), rate limiting on `/auth/*`, Postgres row-level security, refresh token moved to
an httpOnly cookie, Excel import/export (§42), the integration surface (§44).

---

## Definition of done (every phase)

- Domain rules covered by unit tests; each endpoint covered by an integration test against a real
  Postgres (Testcontainers), **including the cross-tenant denial case**.
- Migration written, reviewed, and backward-compatible with the running release.
- OpenAPI document regenerated; the frontend's types match it.
- No secret in the repository; new configuration documented.
- Docs updated in the same PR as the code — a stale `api-design.md` is worse than none.

## Carried debt

Recorded rather than hidden. Each has an owning phase; none is a reason to stop.

| Item | Risk today | Close by |
| --- | --- | --- |
| Refresh token in `localStorage` | An XSS payload could read it. The access token is memory-only, so the blast radius is one refresh token, not a live session — but this is a weakness, not a clean design. | **P9** — httpOnly, SameSite cookie set by the API. |
| No rate limiting on `/auth/*` | Lockout blunts online brute force but not a distributed one. | **P9** |
| Migrations applied by the API at start-up (Development) | Fine for one developer; two production instances starting together would race. | Before the first real deploy: a separate deploy step. |
| Postgres RLS not enabled | The EF query filter is enforced centrally and proven by tests. RLS would stop even a raw SQL query that bypassed it. | Before production (database-design.md §1). |
| **Frontend types still hand-written** | `types/*.ts` mirror the API by hand. A renamed API field compiles fine on the client and fails at runtime. **P3 added the `codegen` script** that generates them from `/openapi/v1.json`; the hand-written types are still what the app imports, so the drift is now *checkable* but not yet *impossible*. | **P4** — migrate the imports over, then delete the hand-written files. |
| ~~Product delete does not check stock~~ | **Closed in P3.** Retiring a product now fails if it has stock on hand or live serials, and names the warehouses. | ✅ |
| ~~Update/delete missing on some reference data~~ | **Closed in P3.** Brands, tax rates, price tiers, payment methods and discounts can now be edited and retired. A tax rate's **percent** is deliberately not editable — changing it in place would restate the tax on invoices already issued — so `POST /tax-rates/{id}/supersede` closes the old rate and opens its successor. | ✅ |
| Migrations applied by the API at start-up (Development) | Two production instances starting together would race. The maintenance job is safe here (it only reads, and its writes are per-company and transactional), but the schema is not. | Before the first real deploy: a separate deploy step. |
| No idempotency keys yet | `api-design.md §5` promises `Idempotency-Key` on state-changing endpoints. Nothing implements it. A double-clicked adjustment posts twice — and unlike a duplicate invoice, a duplicate write-off is invisible until the next count. | **P4**, with goods receipts. |
| ~~Repository not under version control~~ | **Closed.** The repository is on `origin` (GitHub) and P3 is pushed. The CI workflow's seven gates were verified locally before the push, so the pipeline is green the day it first runs rather than a wall of red everyone learns to ignore. | ✅ |

## Engineering practices

- **Branching:** short-lived branches off `main`, squash-merged. `main` is always deployable.
- **CI** (`.github/workflows/ci.yml`) — four gates on the backend, three on the frontend. All seven
  verified locally before being written down, so the pipeline is green the day it first runs rather
  than being a wall of red that everyone learns to ignore:

  | Gate | Why |
  | --- | --- |
  | `dotnet build -warnaserror` | A warning nobody fails on is a warning nobody reads — the EF-version conflict that cost an afternoon announced itself as one. |
  | `dotnet format --verify-no-changes` | Formatting arguments in review are wasted review. |
  | `dotnet test` | Includes the cross-tenant isolation suite against a real PostgreSQL (Testcontainers). |
  | `ef migrations has-pending-model-changes` | A model change with no migration builds and tests green, then fails on deploy against a schema that does not match. |
  | `npm ci` · `lint` · `build` | `ci` (not `install`) fails when package.json and the lockfile disagree — the bug you want to hear about. `next build` typechecks. |
- **Testing pyramid:** many domain unit tests, a solid layer of API integration tests against a real
  database, and a handful of end-to-end tests over the flows that lose money if they break (sell a
  serial-tracked item; receive an import and check the landed cost; complete a warranty repair).
- **Environments:** local (Docker) → staging → production. Migrations run as a separate deploy step,
  before the new app version starts.

## Local development

| Service  | URL |
| -------- | --- |
| Frontend | http://localhost:3000 |
| API      | http://localhost:5199 (`/health`, `/openapi/v1.json`) |
| Postgres | localhost:**5433** — db `techstorepro`, user `techstorepro` |

Port 5433, not 5432: a locally-installed PostgreSQL commonly owns 5432, and two servers fighting
over the port fails confusingly — the app connects successfully to the *wrong* database and reports
an authentication error.

### Configuration added in P3

```jsonc
"Inventory": {
  "Maintenance": {
    "Enabled": true,             // off ⇒ reservations are never swept and balances are never proven
    "SweepInterval": "00:15:00", // expired reservations; not nightly — a quote that expired at 09:00
                                 // must not hold the last unit off the shelf until 02:00 tomorrow
    "ReconcileAtUtcHour": 2      // the balance audit reads the whole ledger; not while the shop trades
  }
}
```

Turning `Enabled` off is a supported choice (a second instance should not run the job twice), and the
service logs a warning at start-up saying exactly what stops happening if you do.

## Immediate next steps

1. **Answer Q2 (landed-cost basis)** with worked examples from the business — by value, by weight, or
   by quantity? It blocks P4, and now that the ledger is real the stakes are concrete: the landed cost
   is what `ApplyInbound` feeds into the moving average, so an error there does not just misprice the
   imported units — it **spreads to all existing stock of that product** and never washes out.
2. **Watch the first CI run.** All seven gates pass locally; the pipeline has never actually executed.
3. **Start P4 — purchasing and imports**, once Q2 is answered. The GRN is what finally calls
   `IStockLedger.PostAsync` with a real supplier cost; today the only things that move stock are
   adjustments, transfers and counts.
