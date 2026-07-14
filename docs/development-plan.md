# Development plan

> Status: **P0–P5 complete.** Q6 and Q8 are answered (§45 D7, D8). The shop can sell: quote → order →
> delivery → invoice → payment, a POS till, returns, credit notes and store credit — proven end to end
> against a live API, not only by tests. **P6 (repairs) is next**: it needs P5's serial-to-invoice-line
> binding, which is built and set.
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
   [requirements.md §46](requirements.md)). Costing is decided (weighted average, D1) and so is
   landed cost (by value, D6 — its worked example became a test before a line of the code existed).
   **The tax model (Q6) is the expensive one still open**: inclusive-vs-exclusive changes every price
   field and every line calculation in sales.
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

### P4 — Purchasing and imports ✅ done

**Q2 is answered** (§45 **D6**): landed cost is apportioned **by value**, remainder to the largest line.
The business's worked example became a test *before* the code, exactly as this plan demanded.

**Delivered (11 new tables, 53 in total):**

- **Purchase orders — optional, and meant it.** Requirements §25 says "PO is optional", so
  `goods_receipts.purchase_order_id` is nullable and the direct purchase (supplier → GRN → stock) is a
  first-class path. Forcing an order would only produce fakes raised after the fact, which look real
  and are therefore worse than none.
- **Goods receipts** with serial capture at the door — which is what makes P6's warranty claim
  answerable two years later.
- **Import shipments**, their charges (freight, insurance, customs, clearing, in any currency), and
  **landed cost**.
- **Supplier invoices and payments**, header + allocations: one transfer settles three invoices, one
  invoice is settled by two instalments. A single `invoice_id` on a payment expresses neither.
- **FX gain/loss on settlement.** A USD 1,000 invoice booked at 3.67 and paid at 3.60 leaves the shop
  AED 70 better off. That gain came from the currency moving, not from selling anything — so it is
  **not** folded back into the cost of the stock. The laptops did not become cheaper to buy.

**The hard part, and the thing to remember about this phase.** Goods and their true cost do not arrive
together: the container is unpacked in March and the clearing agent invoices in April. The receipt has
to post when the goods physically arrive, so the charges are folded in afterwards by a new
**`MovementType.Revaluation`** — money into stock, without inventing a unit. That forced a new column,
`stock_movements.value_adjustment`, and a corresponding change to the balance audit, which now
recomputes value as `SUM(quantity × unit_cost + value_adjustment)`. Leave that second term out and
every import the shop ever landed would show as a permanent discrepancy nobody could clear.

**When the stock has already gone**, the cost has nowhere to live: the container sold out before its
clearing invoice arrived. That money is not silently dropped (it would overstate margin) and not
smeared over whatever else is on the shelf (that would charge one container's freight to another's
goods). It is recorded as `import_shipments.unabsorbed_cost` — visible, attributable, and P7's to
expense.

**Tests: 173 → 231.** The D6 worked example is pinned twice: once as pure arithmetic, and once end to
end against a real database, where 10 laptops at 1,000 and 100 cables at 50 in a container carrying
AED 3,000 land at exactly 1,200 and 60. Also proven: the ledger still audits clean after a revaluation,
a container cannot be costed twice (which would double the freight inside a moving average, where it
would never wash out), and an unabsorbed remainder is reported rather than hidden.

**A P3 bug fell out of building this.** Adjustment and count handlers added each line to the DbSet *and*
to the parent's navigation collection; EF's fixup had already done the latter, so every total computed
from the in-memory graph — including an adjustment's `NetValue`, the money written off — was **double**.
The database was correct; the documents were not. Fixed, with a regression test.

### P5 — Sales ✅ done

**Both blocking questions are answered** (§45 **D7**, **D8**): prices are **tax-exclusive** with
per-company effective-dated rates and **no jurisdiction hardcoded**; sales are raised in the company's
**base currency only**.

**Delivered (17 new tables, 70 in total):**

- **Quote → order → delivery → invoice.** A quotation reserves nothing (an offer is not a claim on the
  shelf, and holding stock for every speculative quote would empty the warehouse on paper while it sat
  full). **Confirming an order is what promises the stock** — one reservation per line, through the
  ledger, under its lock. That, and nothing else, is "prevent overselling".
- **The delivery is the only sales document that moves stock**, and it is where the serial binds. The
  invoice can be raised before the goods go, after them, or cover three deliveries — but exactly one
  document knows which machine went out of the door. **This is what makes P6's warranty claim answerable
  two years later.** The invoice snapshots the COGS the ledger valued the issue at: recomputing it later
  would restate the margin on every sale the shop has ever made, because the average keeps moving.
- **The till (POS)** — one call, one transaction, three documents. A declined card leaves the laptop on
  the shelf and no invoice chasing anybody. It composes the same handlers the documented flow uses, so
  there is still exactly one path by which stock moves.
- **Payments are header + tender + allocations.** One sale settled by cash *and* card; one transfer
  across three invoices; one invoice in two instalments. There is **no `Amount` column** on the payment —
  the total is the sum of the tender, because a header that could disagree with its method lines would be
  a till that does not balance.
- **Returns, credit notes, store credit.** A returned serial goes to `Returned`, **not** `InStock` — a
  machine that came back is inspected before it is sold to someone else. Store credit is a **ledger**, so
  "why do I have 240 credit?" has an answer.
- **Discount approval is a permission, not a queue** (§32). There is a customer at the counter; a sale
  that waited for a manager to log in would be a lost sale, and the shop would work around it by granting
  everyone the permission. Who approved it is stamped on the line.
- **`Idempotency-Key`** — the P4 debt, closed. The key is claimed by an INSERT *before* the work runs,
  and a unique index makes the claim atomic.

**The arithmetic that had to be got right, and the two places it was nearly got wrong:**

1. **Tax comes after the discount.** `SalesMath` is the only place that rule exists. Tax the gross and
   then discount, and every invoice the shop issues is wrong by the tax on the discount — a small number,
   on every line, forever, and one the tax authority eventually asks about.
2. **A refund is a negative payment, not just a negative invoice.** The credit note takes its total off
   the customer's balance; the refund then pays that credit back *out*, which puts it back on. Net zero.
   Only an offset against the balance actually moves it. Deduct on every method — the obvious
   implementation — and a customer refunded 100 in cash walks out with the money *and* a 100 credit.

**Tests: 231 → 312.** The end-to-end sale of a serial-tracked laptop is proven against a real Postgres:
stock falls by one, the serial is `Sold` and bound to its invoice line, COGS equals the moving average,
the customer owes 1,575 on a 1,500 sale, and **the P3 balance audit still reconciles** — which is what
proves nothing in sales wrote stock outside the ledger. Also proven: the same serial cannot be sold
twice, overselling is refused under the lock, a delivery cannot be billed twice, a declined card rolls
back all three documents, store credit cannot be overspent, and a replayed `Idempotency-Key` posts one
invoice rather than two.

**The screens.** `frontend/src/features/` existed as a README and nothing else; sales is the module that
finally creates it, so **the shape it lands on is the shape P6 and P7 will copy**:
`features/sales/{types, api, money-preview, components}`, with the routes in `app/` staying thin. Seven
screens — the till, quotations, orders, deliveries, invoices, payments, returns. The till is
barcode-first: the scan box holds focus and every scan adds a line, because a cashier's hands should
never have to leave the scanner.

Every state-changing call from the module sends an `Idempotency-Key`, generated **once per attempt**
rather than per render — a key that changed on re-render would be a new request every time, which is no
protection at all.

**Verified end to end against a live API**, not only by tests: a company was onboarded, two laptops
received at 1,200, and one sold through the till. The same idempotency key posted twice returned the
*identical receipt*, stock fell by exactly one, and exactly one invoice exists. The money came out
tax-exclusive (1,500 + 75 = 1,575, COGS 1,200, margin 300); the serial went to `Sold` and bound to its
invoice line; the return brought it back as `Returned` — **not** `InStock`, so it cannot be resold before
someone inspects it; the store credit landed in its ledger with the customer's balance at exactly zero;
and the P3 balance audit still agreed afterwards.

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
| **Frontend types still hand-written** | `types/*.ts` mirror the API by hand. A renamed API field compiles fine on the client and fails at runtime. **P3 added the `codegen` script** that generates them from `/openapi/v1.json`; the hand-written types are still what the app imports, so the drift is now *checkable* but not yet *impossible*. Rolled from P4, which built no screens. | **P5's screens** — the sales module is the first to be written against generated types, and the older files migrate behind it. |
| **P4 and P5 have no screens** | The purchasing and sales modules are reachable only through the API. The business logic is proven by tests, but a shop cannot yet receive a container or serve a customer without curl. | **P5 slice 4** (sales), then a purchasing pass. |
| ~~Product delete does not check stock~~ | **Closed in P3.** Retiring a product now fails if it has stock on hand or live serials, and names the warehouses. | ✅ |
| ~~Update/delete missing on some reference data~~ | **Closed in P3.** Brands, tax rates, price tiers, payment methods and discounts can now be edited and retired. A tax rate's **percent** is deliberately not editable — changing it in place would restate the tax on invoices already issued — so `POST /tax-rates/{id}/supersede` closes the old rate and opens its successor. | ✅ |
| Migrations applied by the API at start-up (Development) | Two production instances starting together would race. The maintenance job is safe here (it only reads, and its writes are per-company and transactional), but the schema is not. | Before the first real deploy: a separate deploy step. |
| ~~No idempotency keys yet~~ | **Closed in P5.** `IdempotencyFilter` honours the header on every state-changing request: the key is claimed by an INSERT *before* the work runs, and a unique index makes the claim atomic, so two clicks 50ms apart cannot both sell the laptop. A retry replays the stored response; a failed request releases its key, because a mistyped payment that could never be re-sent would be a worse trap than the one being closed. | ✅ |
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

1. **P6 — repairs.** Both its dependencies are now real: parts come out of stock through the ledger (P3),
   and a warranty claim can find the invoice line that sold a serial (P5's `Serial.SoldInvoiceLineId`,
   which is set at last). Nothing blocks it.
2. **Give purchasing its screens.** P4's module is still reachable only through the API — a shop cannot
   receive a container without curl. It should copy the `features/sales/` shape.
3. **Migrate the frontend onto generated types.** The `codegen` script has existed since P3 and nothing
   imports it. Sales is the natural first module to move, and the older hand-written files follow.
4. **Decide what a zero credit limit means.** `Customer.WouldExceedCreditLimit` treats zero as
   *unlimited*, while the entity's own comment says zero means "cash only". Nothing exercised it until
   P5. It is a one-line change either way, but it is a business decision, not a technical one.

## Open questions that still block a phase

Only **Q7** (SaaS payment processor → P8) and **Q9** (deployment target and file storage → P9) remain.
Neither blocks P6.
