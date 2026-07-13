# Development plan

> Status: **P0, P1 and P2 complete. CI is running. P3 (inventory) is next and unblocked.**
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

### P3 — Inventory (the spine) ← **next, unblocked**

Stock ledger (append-only movements), balances per **warehouse**, serial lifecycle, barcodes/QR and
label printing, reservations, transfers, adjustments, stock counts, historical stock,
**weighted-average costing**.

- ✅ Unblocked: costing is **weighted average** (§45 D1); the warehouse model is settled (D2).
- The stock ledger is the highest-risk code in the system. Every movement is written in the **same
  transaction** as the balance it changes, with the balance row locked; a nightly job recomputes
  balances from movements and must agree to the cent.
- `available = quantity − reserved_quantity`. That subtraction is what "prevent overselling" means.

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
| **Frontend types still hand-written** | `types/identity.ts` and `types/catalog.ts` mirror the API by hand. A renamed API field compiles fine on the client and fails at runtime. | **P3** — generate from `/openapi/v1.json` so drift becomes a build error. Slipped from P2. |
| Product delete does not check stock | Retiring a product that still has units on the shelf should be refused. The stock ledger does not exist yet, so the check cannot be written honestly — it is left undone rather than faked. | **P3**, with the ledger. |
| Update/delete missing on some reference data | Brands, tax rates, price tiers, payment methods and discounts can be created and listed, but not yet edited or retired. Enough to build P3 on; not enough to run a shop. | **P3**, alongside inventory. |
| Repository not under version control | CI exists as a workflow file, but nothing runs it: there is no git repository and no remote. | **Immediately.** |

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

## Immediate next steps

1. **Initialise git and push.** The CI workflow exists and its gates are verified, but nothing runs
   it until there is a repository and a remote. This is the cheapest item on the list and the one
   that protects everything else.
2. **Answer Q2 (landed-cost basis)** with worked examples from the business. It blocks P4, and with
   weighted-average costing an error there contaminates the moving average of *all* stock of a
   product, not only the imported units.
3. **Start P3 — inventory.** The stock ledger is the highest-risk code in the system and the spine
   everything else hangs off. Both of its blockers are resolved: weighted-average costing (§45 D1)
   and the branch-owned-or-shared warehouse model (D2).
