<#
.SYNOPSIS
  Starts PostgreSQL in Docker and waits until it accepts connections.
#>
[CmdletBinding()]
param(
    [switch]$WithPgAdmin
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$composeArgs = @('compose', '--file', (Join-Path $root 'docker-compose.yml'))
if ($WithPgAdmin) { $composeArgs += @('--profile', 'tools') }
$composeArgs += @('up', '--detach')

docker @composeArgs
if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed. Is Docker Desktop running?' }

Write-Host 'Waiting for PostgreSQL to become healthy...' -ForegroundColor Cyan
foreach ($attempt in 1..30) {
    $status = docker inspect --format '{{.State.Health.Status}}' techstorepro-postgres 2>$null
    if ($status -eq 'healthy') {
        Write-Host 'PostgreSQL is ready on localhost:5433 (db=techstorepro, user=techstorepro).' -ForegroundColor Green
        if ($WithPgAdmin) { Write-Host 'pgAdmin is on http://localhost:5050' -ForegroundColor Green }
        exit 0
    }
    Start-Sleep -Seconds 2
}

throw 'PostgreSQL did not become healthy in 60s. Check: docker logs techstorepro-postgres'
