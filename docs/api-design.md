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

Grouped by module, in build order. **P1–P6 are built** (identity, master data, inventory, purchasing,
sales, repairs); finance and the SaaS platform are still planned.

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
GET    /api/v1/purchase-orders          ?status=&supplierId=   POST, GET /{id}
POST   /api/v1/purchase-orders/{id}/approve      COMMITS THE MONEY, and is the gate on receiving:
                                        goods cannot post against a draft. Its own permission, so the
                                        person who picks the supplier need not be the one who signs.
POST   /api/v1/purchase-orders/{id}/cancel       refused once goods have arrived against it.
                                        The whole document is optional: §25 says so. A GRN needs no PO.

GET    /api/v1/goods-receipts           ?supplierId=&importShipmentId=   GET /{id}
POST   /api/v1/goods-receipts           receives goods AND posts them to stock, in one transaction.
                                        purchaseOrderId is nullable — the direct purchase
                                        (supplier → GRN → stock) is a first-class flow.
                                        Serial numbers are captured here, at the door.
                                        A line need not name its purchaseOrderLineId: an unnamed line
                                        is matched on product, so naming the order alone still closes
                                        it. A genuinely ambiguous order is reported, not guessed at.

GET    /api/v1/supplier-invoices        ?status=&supplierId=
POST   /api/v1/supplier-invoices        what they are asking to be paid. It moves NO stock — the
                                        receipt already did, and doing it here would double it.
                                        goodsReceiptId is optional: the bill may arrive before the
                                        goods, or cover several receipts.
POST   /api/v1/supplier-invoices/{id}/post       posting is what puts the debt on the balance.
                                        A draft owes nothing.
POST   /api/v1/supplier-invoices/{id}/cancel     refused once money has been paid against it.

GET    /api/v1/supplier-payments        ?supplierId=
POST   /api/v1/supplier-payments        header + allocations: one transfer settles three invoices, one
                                        invoice takes two instalments, and money with no invoice yet is
                                        an advance, not an error.
                                        THIS IS WHERE FX SETTLES. The invoice fixed the debt at its own
                                        rate; the money leaves at today's. A USD 1,000 invoice booked at
                                        3.67 (AED 3,670) and paid at 3.60 (AED 3,600) leaves the shop
                                        AED 70 better off — and the balance clears to ZERO, because the
                                        settled invoice has exactly what it added taken back off. The 70
                                        is P&L. It is NOT folded into the moving average: the laptops
                                        did not become cheaper to buy.

GET    /api/v1/import-shipments         ?status=   POST
POST   /api/v1/import-shipments/{id}/charges     freight, insurance, customs, clearing — any currency
POST   /api/v1/import-shipments/{id}/apportion   ?basis=ByValue   fold landed cost into inventory.
                                        Exactly once. Gated on Approve, not Edit, because the money
                                        feeds a MOVING average and so never washes back out.
                                        Returns what was absorbed and what was not.

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

# Repairs — built (P6). The customer's device is NOT stock and never becomes it.
GET    /api/v1/repairs                  ?status=&customerId=&technicianId=&openOnly=
                                        openOnly is the §35 pending-repairs report: everything not yet
                                        delivered or cancelled, promised-date first, so the job that is
                                        late is the one the shop sees.
GET    /api/v1/repairs/{id}             the job sheet, with its parts, labour, vendors and history

POST   /api/v1/repairs                  INTAKE. Moves no stock — the machine belongs to the customer.
                                        It answers the warranty question itself: the serial is looked up,
                                        the sale that put the machine in the customer's hands is found
                                        (Serial.SoldInvoiceLineId — the back-edge into Sales), and the
                                        job is stamped free or chargeable. A tickbox here is how a shop
                                        bills someone for a repair it promised to do for nothing.

POST   /api/v1/repairs/{id}/diagnose    → Diagnosing
POST   /api/v1/repairs/{id}/diagnosis   the findings and the estimate. A chargeable job now waits for
                                        the customer; a WARRANTY job goes straight to the bench, because
                                        there is no price for anyone to agree to.
POST   /api/v1/repairs/{id}/approve     THE CUSTOMER'S YES — not a manager's. It is the gate on the
                                        parts store, and it carries `Approve` for that reason.
POST   /api/v1/repairs/{id}/decline     the estimate was refused; the device goes back untouched
POST   /api/v1/repairs/{id}/test        → Testing
POST   /api/v1/repairs/{id}/ready       → Ready for collection
POST   /api/v1/repairs/{id}/deliver     the customer collects it. Does NOT require the bill to be paid —
                                        holding a customer's own laptop hostage over an invoice is not a
                                        credit-control policy. The debt is on their balance.
POST   /api/v1/repairs/{id}/cancel      refused once parts are fitted; return them to stock first

POST   /api/v1/repairs/{id}/parts       THE ONLY THING IN REPAIRS THAT MOVES STOCK. A
                                        `RepairConsumption` through IStockLedger, posted WHEN THE PART IS
                                        FITTED — not at invoicing (§45 D9). Refused before the customer
                                        has approved. Priced from the customer's tier, like any sale.
POST   /api/v1/repairs/parts/{id}/return  a `RepairReturn` movement — a real movement, not an undo
POST   /api/v1/repairs/{id}/labour      an hour is not a thing on a shelf: no stock, no ledger

POST   /api/v1/repairs/{id}/outsource   §29. No stock moves (the device was never the shop's to move);
                                        what lands on the ticket is a COST, in the vendor's currency at
                                        the rate on the day.
POST   /api/v1/repairs/outsourcing/{id}/receive   what the vendor actually charged
POST   /api/v1/repairs/outsourcing/{id}/cancel    refused once they have done the work

POST   /api/v1/repairs/{id}/invoice     raises an ORDINARY SALES INVOICE (§45 D11) — no `repair_invoices`
                                        table, so a repair bill is paid, credited, chased and aged by the
                                        machinery that already exists. It moves no stock: the parts left
                                        when they were fitted. A wholly-warranty job is refused rather
                                        than billed zero.

GET    /api/v1/warranties               POST — a MANUFACTURER'S or a SUPPLIER'S, only.
                                        The shop's own is refused: P5 computes it at the moment of sale
                                        from the product's warranty months and stamps it on the unit, and
                                        a second copy could disagree with the first.
GET    /api/v1/warranties/check?serialNumber=   "is this machine still under warranty?", asked from the
                                        counter before anything is booked in. Returns the answer IN WORDS
                                        the clerk can repeat to the customer — "Shop warranty, sold on
                                        invoice INV-2026-00001, expires 14 Jul 2027". A bare boolean
                                        starts an argument; a sentence ends one.
GET    /api/v1/warranties/claims        ?status=  — with what each claim COST THE SHOP, which is the only
                                        way to answer "which products keep coming back?"
POST   /api/v1/warranties/claims/{id}/accept
POST   /api/v1/warranties/claims/{id}/reject   THE DECISION THAT MAKES THE JOB CHARGEABLE. The parts and
                                        labour already booked as warranty work become billable, and the
                                        shop stops eating them. A reason is mandatory.
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

### Reports (P7)

```
GET    /api/v1/reports/receivables-ageing        ?asOf=&customerId=&branchId=
GET    /api/v1/reports/payables-ageing           ?asOf=&supplierId=
GET    /api/v1/reports/customer-statement/{id}   ?from=&to=
GET    /api/v1/reports/supplier-statement/{id}   ?from=&to=
```

Read-only, all four — `View` and `Export` and nothing else, on two new features (`reports.receivables`,
`reports.payables`). There is no `Create` on a report.

**Each one returns a `variance`, and it is the field to look at first.** `Customer.Balance` and
`Supplier.Balance` are stored figures, maintained by hand wherever a document moves money, with no rebuild
path — so a report that merely *added up the invoices* would be a second opinion, not a proof. These
rebuild the position from the documents and subtract the stored balance. Zero means the cache and the
documents agree. Anything else is drift, and the shop wants to hear about it on the day rather than at the
year end.

Getting that to come out at zero is most of the work, because the invoices and the balance disagree by
construction in two places:

- **An offset credit note moves the balance and not the invoice.** It writes no allocation, so the invoice
  it credits keeps its full outstanding and stays `Posted` for ever. Age the invoice alone and a
  fully-credited debt sits in the ninety-day column until the end of time. The report nets credit notes per
  invoice; a store-credit or refund credit note is *not* netted, because neither moves the balance either.
- **An unallocated payment moves the balance and not the invoice**, the other way. It surfaces as
  `credits` (receivables) or `advances` (payables) rather than being buried in the buckets — a customer who
  owes 10,000 at ninety days while sitting on a 9,000 advance is a different conversation from one who owes
  1,000.

Buckets are days **overdue**, not days since the invoice: `Current / 1–30 / 31–60 / 61–90 / 90+`, measured
from `DueAt`, which falls back to the document date where no terms were given (null means *due on receipt*,
not *never due* — read the other way, every counter sale drops out of the report).

A payable is valued at **the rate its invoice was booked at**, never today's. That is the only valuation
that reconciles to the balance, and revaluing an open payable would book an unrealised FX gain — a concept
this system does not have anywhere (§45 D8). So the detail rows carry both figures: what the supplier will
ask for (USD 1,000) and what it costs the shop (AED 3,670, at 3.67).

### Still planned

Sketches, not contracts.

```
# Finance and reporting (P7, remaining)
GET    /api/v1/reports/sales-summary    ?from=&to=&groupBy=product|salesperson|branch
GET    /api/v1/reports/stock-valuation
GET    /api/v1/reports/profit-and-loss  revenue − COGS − expenses (§45 D3 — computed, not a GL extract)
GET    /api/v1/reports/repair-profitability   P6 already computes the per-job margin; this aggregates it
GET    /api/v1/expenses                 POST — including the unabsorbed import cost P4 recorded and the
                                        warranty repairs P6 costed but never billed
```

## 7. Cross-cutting

- **CORS**: only the origins in `Cors:AllowedOrigins` (dev: `http://localhost:3000`).
- **Rate limiting**: to be added on `/auth/*` before public exposure — login is the one endpoint
  worth brute-forcing.
- **Logging**: Serilog, one structured line per request, enriched with the trace id, user id and
  company id. Never log tokens, password hashes or full card data.
- **Health**: `GET /health` (anonymous) — used by the frontend's landing page and by any
  container orchestrator.
