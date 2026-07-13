<#
.SYNOPSIS
  One-time setup for a new developer machine: checks tooling, restores packages,
  installs npm dependencies, starts the database and applies migrations.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Assert-Command {
    param([string]$Name, [string]$InstallHint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is required but was not found. $InstallHint"
    }
    Write-Host "  found $Name" -ForegroundColor DarkGray
}

Write-Host 'Checking prerequisites...' -ForegroundColor Cyan
Assert-Command dotnet 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
Assert-Command node   'Install Node.js 20 or later: https://nodejs.org'
Assert-Command docker 'Install Docker Desktop: https://docs.docker.com/desktop/'

Write-Host 'Restoring backend packages...' -ForegroundColor Cyan
dotnet restore (Join-Path $root 'backend/TechStorePro.slnx')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

Write-Host 'Installing frontend packages...' -ForegroundColor Cyan
Push-Location (Join-Path $root 'frontend')
try {
    npm install
    if ($LASTEXITCODE -ne 0) { throw 'npm install failed.' }

    if (-not (Test-Path '.env.local')) {
        Copy-Item '.env.example' '.env.local'
        Write-Host '  created frontend/.env.local from .env.example' -ForegroundColor DarkGray
    }
}
finally {
    Pop-Location
}

Write-Host 'Starting the database...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'db-up.ps1')

Write-Host 'Applying migrations...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'db-migrate.ps1')

Write-Host ''
Write-Host 'Setup complete. Start the stack with: ./scripts/start-dev.ps1' -ForegroundColor Green
