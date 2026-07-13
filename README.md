# TechStorePro

Multi-company SaaS ERP for a computer **sales**, **import** and **repair** business.

**Phases P0, P1 and P2 are complete.** You can register a company, sign in, switch between companies,
manage branches and warehouses, invite users and grant them permissions feature by feature, change
business rules from Settings, and see every change in an audit trail — and you can now keep a full
catalogue: products (serial-tracked or not), customers, suppliers, tax rates, price tiers and lists,
discounts, payment methods and FX rates.

Pricing resolves properly: a wholesale customer and a walk-in are quoted different prices for the
same product, and the system reports which price list it used.

The remaining modules — inventory, purchasing, sales, repairs, finance — are sequenced in
[docs/development-plan.md](docs/development-plan.md). **P3 (inventory) is next.**

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
│       ├── components/          shared UI; ui/ primitives, layout/ shell
│       ├── features/            one folder per business module (none yet)
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
