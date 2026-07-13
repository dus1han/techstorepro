# scripts

PowerShell scripts for local development. Run them from the repository root.

| Script                            | Purpose                                                          |
| --------------------------------- | ---------------------------------------------------------------- |
| `setup.ps1`                       | One-time machine setup: tooling check, restore, install, migrate.  |
| `start-dev.ps1`                   | Runs the whole stack: database, API, frontend.                     |
| `db-up.ps1 [-WithPgAdmin]`        | Starts Postgres in Docker and waits for it to be healthy.          |
| `db-down.ps1 [-RemoveData]`       | Stops the containers; optionally deletes the data volume.          |
| `db-migrate.ps1`                  | Applies pending EF Core migrations.                                |
| `db-add-migration.ps1 <Name>`     | Generates a migration from the current model.                      |
| `db-reset.ps1 -Force`             | Destroys and rebuilds the local database.                          |
| `ensure-ef-tools.ps1`             | Installs the `dotnet-ef` CLI if missing. Called by the others.     |

If PowerShell blocks a script, allow local scripts for your user once:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```
