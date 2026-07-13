<#
.SYNOPSIS
  Generates a new EF Core migration from the current model.
.EXAMPLE
  ./scripts/db-add-migration.ps1 AddCustomers
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Name
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$startup = Join-Path $root 'backend/src/TechStorePro.API'
$project = Join-Path $root 'backend/src/TechStorePro.Infrastructure'

& (Join-Path $PSScriptRoot 'ensure-ef-tools.ps1')

dotnet ef migrations add $Name `
    --project $project `
    --startup-project $startup `
    --output-dir Persistence/Migrations
if ($LASTEXITCODE -ne 0) { throw 'Failed to create the migration.' }

Write-Host "Created migration '$Name'. Review the generated Up/Down before committing." -ForegroundColor Green
