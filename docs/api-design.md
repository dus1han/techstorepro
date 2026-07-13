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

The active company is a **claim inside the access token** (`company_id`), never a header or a
query parameter. A header would let any authenticated caller read another company's data by
editing the request; a claim cannot be changed without re-authenticating.

Switching company is therefore an auth operation, not a request parameter:

```
POST /api/v1/auth/switch-company   { "companyId": "…" }  ->  a new access token
```

The server resolves the company from the token (`TenantContext`), and the DbContext filters
every query by it. No endpoint accepts a company id in its route.

## 3. Authentication

JWT bearer, short-lived access token plus a rotating refresh token.

```
POST /api/v1/auth/register          create a company and its first owner
POST /api/v1/auth/login             -> { accessToken, refreshToken, companies[] }
POST /api/v1/auth/refresh           rotate; the old refresh token is revoked on use
POST /api/v1/auth/logout            revoke the refresh token
POST /api/v1/auth/switch-company    re-issue the access token for another membership
GET  /api/v1/auth/me                current user, active company, permissions
```

Access token claims: `sub` (user id), `email`, `company_id`. **There is no `role` claim** —
requirements §7 forbids fixed roles, so nothing in the system resolves a role name.

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

Updates to documents carry a `rowVersion`; a mismatch is a `409`, not a silent overwrite.

## 6. Endpoints

Grouped by module, in build order. **P1–P3 are built** (identity, master data, inventory); everything
from purchasing down is still planned.

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

# Purchasing and imports
GET    /api/v1/purchase-orders          POST, /{id}/approve, /{id}/cancel
POST   /api/v1/goods-receipts
GET    /api/v1/import-shipments         POST
POST   /api/v1/import-shipments/{id}/costs        add freight, duty, clearing
POST   /api/v1/import-shipments/{id}/apportion    fold landed cost into inventory

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
