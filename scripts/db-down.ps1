<#
.SYNOPSIS
  Stops the local database containers. Data is preserved unless -RemoveData is passed.
#>
[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$composeArgs = @('compose', '--file', (Join-Path $root 'docker-compose.yml'), '--profile', 'tools', 'down')
if ($RemoveData) {
    Write-Host 'Removing the postgres-data volume. All local data will be lost.' -ForegroundColor Yellow
    $composeArgs += '--volumes'
}

docker @composeArgs
if ($LASTEXITCODE -ne 0) { throw 'docker compose down failed.' }
