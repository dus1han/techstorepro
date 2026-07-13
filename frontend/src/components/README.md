# components

Shared, presentational components used across features.

- `ui/` — design-system primitives (button, input, table, dialog). No data fetching, no
  business rules.
- `layout/` — app shell pieces (sidebar, topbar, company switcher).

Anything specific to a single business module belongs in `src/features/<module>/`, not here.
