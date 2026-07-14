# API design

> Status: **draft**. The conventions below are implemented in the foundation (error contract,
> tenancy, paging envelope). The endpoints are the intended surface.
>
> **[architecture.md](architecture.md) is the authority** where the two disagree. This file has been
> reconciled with it: no role claim, permissions by (feature, action), and a second API surface for
> platform administration.

Base URL in development: `http://localhost:5199`. OpenAPI document: `/openapi/v1.json`.

## 0. Two API surfaces

```
/api/v1/platform/*    Platform owners (requirements §2). NOT tenant-scoped.
                      Companies, plans, subscriptions, billing, usage.
/api/v1/*             Company users. Tenant-scoped by the company_id claim. The ERP itself.
```

Separate JWT audiences, separate authorisation policies. A tenant token can never reach a platform
endpoint; platform staff touch tenant data only through explicit, audited impersonation.

## 1. Conventions

- REST over JSON. Resources are plural nouns; verbs live in the HTTP method.
- Routes are versioned from the start: `/api/v1/...`. A breaking change ships as `v2`; adding
  an optional field is not breaking and does not.
- `camelCase` JSON, matching the TypeScript client.
- All times are UTC ISO-8601 (`2026-07-13T09:15:00Z`). All money is a decimal string plus a
  currency code — never a float, never a bare number that a JS client could round.

```json
{ "amount": "1499.0000", "currencyCode": "AED" }
```

## 2. Tenancy on the wire

The company is a **claim inside the access token** (`company_id`), never a header or a query
parameter. A header would let any authenticated caller read another company's data by editing the
request; a claim cannot be changed without re-authenticating.

The server resolves the company from the token (`TenantContext`), and the DbContext filters every
query by it. No endpoint accepts a company id in its route.

**There is no switch-company endpoint.** A user belongs to exactly one company (§45 D5), so there is
nothing to switch to; somebody who works for two companies has two accounts.

### The two kinds of caller

A **tenant user** carries `company_id`. A **platform operator** does not — they belong to no company.
That absence is dangerous, because a null tenant switches the DbContext query filters **off**: a
platform token reaching `/api/v1/products` would read every company on the platform at once.

So both are asserted **positively**, by two policies, and neither is inferred from the other's absence:

| Policy | Requires | Applies to |
| --- | --- | --- |
| `Tenant` | the `company_id` claim | every `/api/v1/*` feature endpoint |
| `Platform` | the `platform_admin` claim | `/api/v1/platform/*` only |

An authenticated token that has neither is refused rather than falling through.

## 3. Authentication

JWT bearer, short-lived access token plus a rotating refresh token.

```
POST /api/v1/auth/login             { "login": "ahmed@GULF01", "password": "…" }
POST /api/v1/auth/refresh           rotate; the old refresh token is revoked on use
POST /api/v1/auth/logout            revoke the refresh token
GET  /api/v1/auth/me                current user, their company, their permissions
```

**The login is one field, `username@COMPANYCODE`.** A username is unique only *within* a company —
two shops may each have an "admin" — so the login has to name its company. It is not two boxes: a
separate "company code" field asks the user to know something they cannot discover, and a company
dropdown would show every tenant on the platform to anyone who opened the page.

Every credential failure — unknown company, unknown user, wrong password, malformed login — returns
**the same message on the same timing path**. Told apart, they are a map of the platform.

There is **no registration endpoint**. A company cannot bring itself into existence; TechStorePro
onboards it (§2 below).

Access token claims: `sub` (user id), `username`, `company_id`. **No `role` claim** — requirements §7
forbids fixed roles, so nothing in the system resolves a role name. **No `email` claim** either: email
is optional and non-unique now, so it cannot identify anybody.

### The platform console (requirements §2)

Separate table, separate login, separate refresh tokens — a shop's owner is not a diminished platform
operator, they are not one at all. A platform admin signs in with a **bare username, no `@company`**.

```
POST   /api/v1/platform/auth/login       { "username": "…", "password": "…" }
GET    /api/v1/platform/companies        every company on the platform
POST   /api/v1/platform/companies        onboards a company AND its first user, in one transaction
                                         -> { companyCode, ownerLogin: "admin@GULF01" }
POST   /api/v1/platform/companies/{id}/suspend   nobody in it can sign in, including its owner
POST   /api/v1/platform/companies/{id}/restore
```

`POST /platform/companies` is what replaced self-service registration. It returns the owner's full
login, because that string is what the operator reads out to the customer and the one thing they
cannot reconstruct from anywhere else.

**Bootstrap:** the first platform admin is seeded from configuration (`Platform:FirstAdmin`) when the
table is empty — otherwise nobody could create anybody and an empty database would be unusable. It
never overwrites an existing admin.

Authorisation is by **(feature, action)** — e.g. `sales.invoice` × `Approve` — granted per user.
Permissions are deliberately **not** carried in the token:

- a user with hundreds of grants would bloat every request, and
- a revoked permission must take effect **immediately**, not whenever the token next refreshes.

They are loaded per request from `user_permissions`, cached briefly per (user, company), and
invalidated whenever a grant changes. `GET /auth/me` returns the same list so the frontend can hide
what the user cannot do — but **UI hiding is cosmetic; the server check is the real one.**

## 4. Standard shapes

**Paged list** — every collection endpoint returns this envelope
(`PagedResult<T>` server-side, `PagedResult<T>` in `frontend/src/types/api.ts`):

```json
{
  "items": [],
  "page": 1,
  "pageSize": 25,
  "totalCount": 137,
  "totalPages": 6,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

Query parameters: `?page=1&pageSize=25&search=&sortBy=createdAt&sortDir=desc`.
`pageSize` is capped server-side (100) so a client cannot ask for the whole table.

**Errors** — RFC 7807 problem details, produced by `ExceptionHandlingMiddleware`, identical
for every failure:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/api/v1/products",
  "traceId": "0HN7…",
  "errors": { "sku": ["SKU is required."] }
}
```

| Status | When                                                                  |
| ------ | --------------------------------------------------------------------- |
| 400    | Validation failure (`errors` populated) or a violated business rule.    |
| 401    | Missing, expired or invalid token.                                      |
| 403    | Authenticated, but lacking the permission — or reaching across tenants. |
| 404    | Not found *or* not visible to this company. The two are indistinguishable on purpose: a 404-vs-403 difference would leak the existence of another company's records. |
| 409    | Concurrency conflict (stale `rowVersion`) or a duplicate document number. |
| 422    | Well-formed but unprocessable (e.g. not enough stock to deliver).        |
| 500    | Unexpected. Logged with the `traceId`; no internal detail is returned.   |

`traceId` is on every error response and in the server log — it is what a support ticket quotes.

## 5. Commands, queries and idempotency

Every request enters the Application layer as a MediatR command or query, validated by
FluentValidation before its handler runs (`ValidationBehaviour`). The controller only
dispatches; it holds no logic.

State-changing endpoints that a user could double-submit (posting an invoice, receiving
stock, taking a payment) accept an `Idempotency-Key` header. A repeated key returns the
original result instead of creating a second document — a counter clerk double-clicking must
not invoice twice.

**Built in P5** (`IdempotencyFilter`), and the order of operations is the whole of it:

- the key is **claimed by an INSERT before the work runs**, and a unique index on `(company, key,
  endpoint)` makes that claim atomic. Claiming it afterwards would leave the exact window this closes
  wide open — two clicks 50ms apart would both find no record, and both would sell the laptop;
- a repeat of a **finished** request replays its stored response, byte for byte, so the caller cannot
  tell a retry from the original;
- a repeat of a request **still in flight** is a `409`, not a second execution;
- the same key with a **different body** is a `422` — that is not a retry, it is a caller bug, and
  replaying the first answer would hide it;
- a request that **fails releases its key**, so the caller can correct the input and try again with it.
  A mistyped payment that could never be re-sent would be a worse trap than the one being closed.

Updates to documents carry a `rowVersion`; a mismatch is a `409`, not a silent overwrite.

## 6. Endpoints

Grouped by module, in build order. **P1–P5 are built** (identity, master data, inventory, purchasing,
sales); repairs, finance and the SaaS platform are still planned.

```
# Master data — built (P2), reference-data edit/retire added in P3
GET    /api/v1/products                 ?search=&categoryId=&page=&pageSize=
POST   /api/v1/products
GET    /api/v1/products/{id}
PUT    /api/v1/products/{id}
DELETE /api/v1/products/{id}            soft delete; refused if the product still has stock
GET    /api/v1/customers                POST/PUT/DELETE alike
GET    /api/v1/suppliers
GET    /api/v1/branches

# Reference data — all six now support edit and retire (P3 closed the create-only gap)
GET    /api/v1/categories               POST, PUT /{id}, DELETE /{id}?reason=
GET    /api/v1/brands                   POST, PUT /{id}, DELETE /{id}?reason=
GET    /api/v1/price-tiers              POST, PUT /{id}, DELETE /{id}?reason=
GET    /api/v1/discounts                POST, PUT /{id}, DELETE /{id}?reason=
GET    /api/v1/payment-methods          POST, PUT /{id}, DELETE /{id}?reason=
GET    /api/v1/tax-rates                POST, PUT /{id}, DELETE /{id}?reason=
POST   /api/v1/tax-rates/{id}/supersede the rate changed: closes this one, opens its successor.
                                        PUT cannot change the percent — that would restate the tax
                                        on invoices already issued.

# Inventory — built (P3)
GET    /api/v1/inventory/stock          ?warehouseId=&productId=&lowStock=true   stock on hand
GET    /api/v1/inventory/movements      ?productId=&warehouseId=&type=&from=&to= the stock card
GET    /api/v1/inventory/historical     ?asOf=   opening/purchases/sales/transfers/adjustments/closing
GET    /api/v1/inventory/valuation      ?warehouseId=   stock at weighted average
GET    /api/v1/inventory/balance-audit  recomputes balances from the ledger; proves the cache

GET    /api/v1/inventory/adjustments    POST, GET /{id}
POST   /api/v1/inventory/adjustments    posts immediately — no approval step, mandatory reason

GET    /api/v1/inventory/transfers      POST, GET /{id}
POST   /api/v1/inventory/transfers/{id}/ship      posts TransferOut
POST   /api/v1/inventory/transfers/{id}/receive   posts TransferIn for what actually arrived
DELETE /api/v1/inventory/transfers/{id}           draft only: shipped stock cannot be un-shipped

GET    /api/v1/inventory/counts         POST, GET /{id}
POST   /api/v1/inventory/counts/{id}/lines        one scan; snapshots system qty at scan time
POST   /api/v1/inventory/counts/{id}/submit
POST   /api/v1/inventory/counts/{id}/approve      separate permission: it authorises the write-off
DELETE /api/v1/inventory/counts/{id}

GET    /api/v1/inventory/reservations   POST, DELETE /{id}
POST   /api/v1/inventory/reservations/expire      sweeps past-deadline holds (also run nightly)

GET    /api/v1/inventory/serials        ?productId=&status=&warehouseId=
GET    /api/v1/inventory/serials/{serialNumber}   full history — the warranty-claim query
POST   /api/v1/inventory/labels/print   returns a PDF (Code128 / EAN-13 / QR; thermal or A4 sheet)

# Purchasing and imports — built (P4)
GET    /api/v1/purchase-orders          POST, /{id}/approve, /{id}/cancel
                                        Optional: §25 says so. A GRN needs no PO.
POST   /api/v1/goods-receipts           receives goods AND posts them to stock, in one transaction.
                                        purchaseOrderId is nullable — the direct purchase
                                        (supplier → GRN → stock) is a first-class flow.
                                        Serial numbers are captured here, at the door.

GET    /api/v1/import-shipments         POST
POST   /api/v1/import-shipments/{id}/charges     freight, insurance, customs, clearing — any currency
POST   /api/v1/import-shipments/{id}/apportion   ?basis=ByValue   fold landed cost into inventory

# Sales — built (P5). Prices are tax-exclusive (D7); everything is in the base currency (D8).
GET    /api/v1/quotations               POST
POST   /api/v1/quotations/{id}/send     /accept, /reject
POST   /api/v1/quotations/{id}/convert  → a sales order, at the price that was quoted. It does not
                                        re-resolve against today's list: that promise is the document.

GET    /api/v1/sales-orders             POST
POST   /api/v1/sales-orders/{id}/confirm   RESERVES the stock and checks the credit limit. This is the
                                           moment the shop commits goods to someone who has not paid.
POST   /api/v1/sales-orders/{id}/cancel    gives the reserved stock back to the shelf

GET    /api/v1/deliveries               POST
POST   /api/v1/deliveries               THE ONLY THING IN SALES THAT MOVES STOCK. Binds the serial to
                                        the machine that left. salesOrderId is nullable — the counter
                                        sale is a first-class flow, exactly as the PO-less GRN is.

GET    /api/v1/sales-invoices           GET /{id}
POST   /api/v1/sales-invoices           prices the delivered lines, snapshots tax and COGS, raises the
                                        customer's balance. It moves NO stock — the delivery did.
POST   /api/v1/sales-invoices/{id}/cancel  unpaid only; does NOT return stock (that is a credit note)

GET    /api/v1/customer-payments        POST   header + tender lines + allocations.
                                        Cash AND card on one sale; one payment across three invoices.
                                        Unallocated money is a credit, not an error.

POST   /api/v1/pos/sales                the till: goods out, bill raised, money taken — ONE transaction.
                                        A declined card leaves the laptop on the shelf.

GET    /api/v1/credit-notes             POST   the only thing that puts stock back.
                                        Returned serials go to `Returned`, never straight to `InStock`.
GET    /api/v1/credit-notes/store-credit/{customerId}   a ledger, so the balance can explain itself
```

**Send an `Idempotency-Key` on `/pos/sales` and `/customer-payments`.** A double-clicked till would
otherwise take the money twice, and unlike a duplicate invoice nobody notices until they count the
drawer. See §5 — the key is claimed *before* the work runs.

### The import flow, and why it is shaped like this

**Goods and their true cost do not arrive together.** The container is unpacked and on the shelf in
March; the clearing agent invoices in April. The shop cannot refuse to book stock it can physically
see, so:

1. `POST /import-shipments` — the container is on the water.
2. `POST /goods-receipts` — it lands. **Stock posts immediately, at the goods price.**
3. `POST /import-shipments/{id}/charges` — freight, duty, insurance, clearing, as each is billed.
4. `POST /import-shipments/{id}/apportion` — folds that cost into the stock.

Step 4 posts a **`Revaluation`** movement: money into inventory, without inventing a unit. It is gated
on `Approve`, not `Edit`, and that is not ceremony — costing is weighted average (§45 D1), so the money
it moves feeds the moving average of every product in the container and spreads to units that arrived
years ago, where it **never washes out**.

Apportionment is **by value** (§45 D6), with the rounding remainder going to the largest line, so the
apportioned total equals the charge total to the fils.

It returns what was **absorbed** and what was not. If the container sold out before its clearing
invoice arrived, there is no stock left to carry that cost: the remainder is reported and recorded on
the shipment, rather than silently dropped (which would overstate margin) or smeared over whatever else
is on the shelf (which would charge one container's freight to another's goods).

```

# Sales
GET    /api/v1/quotes                   POST, /{id}/convert
GET    /api/v1/sales-orders             POST, /{id}/confirm, /{id}/cancel
POST   /api/v1/deliveries                        picks serials
GET    /api/v1/invoices                 POST, /{id}/void
POST   /api/v1/payments
POST   /api/v1/credit-notes

# Repairs
GET    /api/v1/repairs                  ?status=&technicianId=&branchId=
POST   /api/v1/repairs                           intake
POST   /api/v1/repairs/{id}/diagnose
POST   /api/v1/repairs/{id}/quote                -> customer approval
POST   /api/v1/repairs/{id}/approve | /reject
POST   /api/v1/repairs/{id}/parts                consumes stock
POST   /api/v1/repairs/{id}/labour
POST   /api/v1/repairs/{id}/complete | /deliver
POST   /api/v1/repairs/{id}/invoice

# Reporting
GET    /api/v1/reports/sales-summary    ?from=&to=&groupBy=product|salesperson|branch
GET    /api/v1/reports/stock-valuation
GET    /api/v1/reports/receivables-ageing
GET    /api/v1/reports/repair-turnaround
```

## 7. Cross-cutting

- **CORS**: only the origins in `Cors:AllowedOrigins` (dev: `http://localhost:3000`).
- **Rate limiting**: to be added on `/auth/*` before public exposure — login is the one endpoint
  worth brute-forcing.
- **Logging**: Serilog, one structured line per request, enriched with the trace id, user id and
  company id. Never log tokens, password hashes or full card data.
- **Health**: `GET /health` (anonymous) — used by the frontend's landing page and by any
  container orchestrator.
