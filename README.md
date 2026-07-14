# TechStorePro

Multi-company SaaS ERP for a computer **sales**, **import** and **repair** business.

**Phases P0–P6 are complete.** A company is onboarded by TechStorePro; its
staff sign in as `username@COMPANY`, hold permissions granted feature by feature, and leave an audit
trail behind every change. The shop keeps a full catalogue, prices it properly (a wholesale customer and
a walk-in are quoted different prices, and the system reports which price list it used), holds stock in a
locked ledger — and **buys, sells and repairs through screens, not curl.**

**Buying:** purchase order → goods receipt → landed cost → supplier invoice → payment. The goods receipt
is the only purchasing document that moves stock, and it is where the serial is captured. A container's
freight arrives *after* its goods do, so the landed cost is folded in afterwards — once, and never twice.
Paying a USD invoice at a rate the dirham has moved to realises a genuine FX gain, which goes to the P&L
and **not** into the cost of the stock: the laptops did not become cheaper to buy.

**Selling:** quote → order → delivery → invoice → payment, plus a POS till, returns, credit notes and
store credit. The delivery is the only sales document that moves stock, and it is where the serial binds.

**Repairing:** intake → diagnosis → customer approval → parts → labour → outsourcing → collection → bill.
**The customer's device is not stock** — the shop does not own it, cannot sell it and must not value it —
so only the parts fitted into it ever move on the ledger, and nothing may be fitted until the customer has
agreed to the price.

**And this is where the serial binding pays off.** A laptop sold last year comes back to the counter, and
the system works out for itself that the repair is free: it walks the serial back to the invoice line that
sold it and answers *"Shop warranty, sold on invoice INV-2026-00001, expires 14 Jul 2027"* — in words the
clerk can read out to the customer standing in front of them. Nobody ticks a box, because a tickbox is how
a shop ends up billing someone for a repair it had already promised to do for nothing. A warranty repair
still **costs**: the parts left the shelf, so the job shows the loss, and the shop can finally see which
product line its warranty is quietly paying for.

The remaining work — finance and reporting, the SaaS platform, hardening — is sequenced in
[docs/development-plan.md](docs/development-plan.md). **P7 (finance, reporting, dashboard) is next.**

## Stack

| Layer    | Technology                                              |
| -------- | ------------------------------------------------------- |
| Frontend | Next.js 16 (App Router), TypeScript, Tailwind CSS v4     |
| Backend  | ASP.NET Core 10 Web API, Clean Architecture, MediatR     |
| Database | PostgreSQL 17, EF Core migrations                        |
| Local    | Docker Compose (Postgres, optional pgAdmin)              |

## Getting started

Requires the .NET 10 SDK, Node.js 20+, and Docker Desktop.

```powershell
./scripts/setup.ps1      # check tooling, restore, install, start the DB, migrate
./scripts/start-dev.ps1  # run database + API + frontend together
```

| Service  | URL                                                      |
| -------- | -------------------------------------------------------- |
| Frontend | http://localhost:3000                                    |
| API      | http://localhost:5199 (`/health`, `/openapi/v1.json`)    |
| Postgres | localhost:**5433** — db `techstorepro`, user `techstorepro` |

Postgres is on **5433**, not the usual 5432: a locally-installed PostgreSQL commonly owns 5432, and
two servers fighting over the port fails confusingly — the app connects successfully to the *wrong*
database and reports an authentication error.

Register the first company at http://localhost:3000 — the first user becomes its owner and holds
every permission implicitly.

## Layout

```
TechStorePro/
├── frontend/                    Next.js app
│   └── src/
│       ├── app/                 routes (App Router)
│       ├── components/          shared UI; ui/ primitives, layout/ shell, data/ table + form
│       ├── features/            one folder per business module — sales/, purchasing/
│       ├── lib/                 api-client, env, utils
│       └── types/               API contract types
│
├── backend/                     TechStorePro.slnx
│   ├── src/
│   │   ├── TechStorePro.Domain/          entities, value objects, domain rules — no dependencies
│   │   ├── TechStorePro.Application/     use cases, interfaces, validation — depends on Domain
│   │   ├── TechStorePro.Infrastructure/  EF Core, Postgres, external services
│   │   └── TechStorePro.API/             controllers, middleware, auth, DI composition
│   └── tests/
│       ├── TechStorePro.Domain.Tests/
│       └── TechStorePro.Application.Tests/
│
├── database/                    init scripts, seed data, conventions
├── docs/                        requirements, database, API, plan
├── scripts/                     setup, run, migrate (PowerShell)
└── docker-compose.yml
```

## Architecture

Dependencies point inwards, and only inwards:

```
API  ──►  Infrastructure  ──►  Application  ──►  Domain
 └────────────────────────────────┘
        (also references Application directly, for DI and MediatR)
```

`Domain` knows nothing about EF Core, HTTP, or any package. `Application` defines interfaces
(`IApplicationDbContext`, `ITenantContext`, `ICurrentUser`) that `Infrastructure` and `API`
implement, so use cases can be tested without a database or a web server.

### Multi-tenancy

A **company** is the tenant. The active company is a claim (`company_id`) on the caller's JWT —
never a header or a route parameter, because those can be edited by the caller.

Any entity implementing `ITenantScoped` automatically gets:

- a **global query filter** pinned to the current company (`ApplicationDbContext`), so a query
  physically cannot return another company's rows, and
- its `CompanyId` **stamped on insert**.

That means isolation is enforced in one place instead of in every query, where one forgotten
`WHERE` clause would leak another company's data. Rationale and the rejected alternatives are in
[docs/database-design.md](docs/database-design.md).

Entities also get audit stamps (`CreatedAt/By`, `UpdatedAt/By`) and soft delete — sales, stock and
repair history must stay auditable, so records are retired, not removed.

## Documentation

| Document                                             | Contents                                                     |
| ---------------------------------------------------- | ------------------------------------------------------------ |
| [architecture.md](docs/architecture.md)              | **Start here.** System architecture, module dependencies, entity list, decisions, conflicts |
| [requirements.md](docs/requirements.md)              | Feature specification, §45 decisions taken, §46 open questions |
| [development-plan.md](docs/development-plan.md)      | Phases P0–P9, definition of done, carried debt, practices     |
| [database-design.md](docs/database-design.md)        | Tenancy model, conventions, target schema, indexing           |
| [api-design.md](docs/api-design.md)                  | REST conventions, auth, error contract, planned endpoints     |

`architecture.md` is the authority where the documents disagree. Five questions in
[requirements.md §46](docs/requirements.md) are still open, and each blocks the phase named against
it — the landed-cost basis is the expensive one.

## Development

```powershell
dotnet build backend/TechStorePro.slnx      # build the backend
dotnet test  backend/TechStorePro.slnx      # run the tests
./scripts/db-add-migration.ps1 AddThing    # generate a migration from the model
./scripts/db-migrate.ps1                   # apply migrations
cd frontend; npm run dev                   # frontend only
```

## Configuration

Nothing secret is in the repository. Local development uses throwaway values
(`appsettings.Development.json`, `docker-compose.yml`, `frontend/.env.local`).

Every other environment must supply, by environment variable or secret store:

| Setting                              | Purpose                                    |
| ------------------------------------ | ------------------------------------------ |
| `ConnectionStrings__DefaultConnection` | Postgres connection string                 |
| `Jwt__Key`                           | Token signing key — 32+ random bytes        |
| `Jwt__Issuer`, `Jwt__Audience`       | Token validation                            |
| `Cors__AllowedOrigins__0`            | Frontend origin                             |
| `NEXT_PUBLIC_API_BASE_URL`           | API base URL used by the frontend           |
