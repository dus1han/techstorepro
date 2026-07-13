# seed

Data applied **after** migrations.

- `reference/` — data the system needs to function (currencies, units of measure, default
  roles). Applied in every environment, including production.
- `demo/` — fictional companies, products and repair tickets for local development and
  demos. Never applied to production.

Every script must be idempotent (`INSERT ... ON CONFLICT DO NOTHING` or an upsert), because
they are re-run on each `db-reset` and on every fresh developer machine.

No seed data exists yet — it arrives with the modules that own it.
