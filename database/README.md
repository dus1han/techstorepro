# database

PostgreSQL 17. The schema is owned by **EF Core migrations**, not by hand-written SQL.

## Layout

| Path         | Purpose                                                                                          |
| ------------ | ------------------------------------------------------------------------------------------------ |
| `init/`      | Runs once on an empty data volume: extensions, schemas, roles. No tables.                          |
| `seed/`      | Reference and demo data, applied after migrations. Safe to re-run (idempotent upserts).            |
| `backups/`   | Local `pg_dump` output. Git-ignored.                                                               |

Migrations themselves live in the backend, at
`backend/src/TechStorePro.Infrastructure/Persistence/Migrations/`, because EF generates and
applies them from the model.

## Why one database with a CompanyId column

Every tenant-owned table carries a `company_id`, and every query is filtered by it through
an EF Core global query filter (see `ApplicationDbContext`). The alternatives were rejected:

- **Database per company** — cleanest isolation, but migrating hundreds of databases on every
  release and running cross-company reports becomes the dominant cost.
- **Schema per company** — same migration problem, plus connection-pool churn on schema switching.

The shared-table approach keeps migrations and reporting simple. The safety it gives up is
bought back in code: the query filter is applied automatically to any entity implementing
`ITenantScoped`, so a developer must go out of their way to bypass it.

## Common tasks

```powershell
./scripts/db-up.ps1              # start Postgres in Docker
./scripts/db-migrate.ps1         # apply migrations to the running database
./scripts/db-add-migration.ps1 AddCustomers   # generate a new migration
./scripts/db-reset.ps1           # drop the volume and rebuild from scratch
```

## Conventions

- `snake_case` for tables and columns, plural table names (`sales_orders`).
- Primary keys are `uuid`, defaulting to `gen_random_uuid()`.
- Money is `numeric(18,4)`; never `float`. Each monetary column is paired with a currency code.
- Timestamps are `timestamptz`, always stored in UTC.
- Every tenant-owned table: `company_id uuid NOT NULL`, indexed as the leading column of the
  composite indexes it participates in.
- Rows are soft-deleted (`is_deleted`), because sales and repair history must stay auditable.
