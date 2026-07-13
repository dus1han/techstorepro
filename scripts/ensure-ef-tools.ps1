<#
.SYNOPSIS
  Installs the dotnet-ef CLI if it is not already available. Called by the db-* scripts.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (Get-Command dotnet-ef -ErrorAction SilentlyContinue) { return }

Write-Host 'dotnet-ef not found. Installing it globally...' -ForegroundColor Cyan
dotnet tool install --global dotnet-ef
if ($LASTEXITCODE -ne 0) { throw 'Failed to install dotnet-ef.' }

Write-Host 'Installed dotnet-ef. If the next command fails, open a new terminal so PATH is refreshed.' -ForegroundColor Yellow
