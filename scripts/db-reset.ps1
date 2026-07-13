<#
.SYNOPSIS
  Destroys the local database and rebuilds it: fresh volume, init scripts, migrations.
.DESCRIPTION
  Local development only. This deletes all local data.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if (-not $Force) {
    Write-Host 'This deletes the local database volume and all data in it.' -ForegroundColor Yellow
    Write-Host 'Re-run with -Force to proceed.' -ForegroundColor Yellow
    exit 1
}

& (Join-Path $PSScriptRoot 'db-down.ps1') -RemoveData
& (Join-Path $PSScriptRoot 'db-up.ps1')
& (Join-Path $PSScriptRoot 'db-migrate.ps1')

Write-Host 'Database rebuilt.' -ForegroundColor Green
