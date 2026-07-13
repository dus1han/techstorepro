<#
.SYNOPSIS
  Applies all pending EF Core migrations to the running database.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$startup = Join-Path $root 'backend/src/TechStorePro.API'
$project = Join-Path $root 'backend/src/TechStorePro.Infrastructure'

& (Join-Path $PSScriptRoot 'ensure-ef-tools.ps1')

dotnet ef database update --project $project --startup-project $startup
if ($LASTEXITCODE -ne 0) { throw 'Migration failed. Is the database running (./scripts/db-up.ps1)?' }

Write-Host 'Database is up to date.' -ForegroundColor Green
