<#
.SYNOPSIS
  Runs the full local stack: database (Docker), API (:5199) and frontend (:3000).
.DESCRIPTION
  The API and frontend each open in their own PowerShell window so their logs stay separate.
  Close those windows to stop them; the database keeps running (./scripts/db-down.ps1).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'db-up.ps1')

Write-Host 'Starting the API on http://localhost:5199 ...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$(Join-Path $root 'backend/src/TechStorePro.API')'; dotnet watch run"
)

Write-Host 'Starting the frontend on http://localhost:3000 ...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location '$(Join-Path $root 'frontend')'; npm run dev"
)

Write-Host ''
Write-Host 'Frontend  http://localhost:3000' -ForegroundColor Green
Write-Host 'API       http://localhost:5199 (OpenAPI: /openapi/v1.json, health: /health)' -ForegroundColor Green
