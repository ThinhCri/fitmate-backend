# =============================================================================
# scripts/ef-update.ps1
#
# Apply EF Core migrations using env from .env (or .env.example).
#
# Usage:
#   pwsh ./scripts/ef-update.ps1                       # dotnet ef database update
#   pwsh ./scripts/ef-update.ps1 -Action migrations    # list pending migrations
#   pwsh ./scripts/ef-update.ps1 -Action script        # generate idempotent SQL
#   pwsh ./scripts/ef-update.ps1 -EnvFile .env.prod    # use a specific env file
#   pwsh ./scripts/ef-update.ps1 -Project src/Fitmate.Infrastructure
#
# Required: .NET 8 SDK + dotnet-ef tool installed (`dotnet tool install -g dotnet-ef`).
# Required: .env file present (copy from .env.example if missing).
# =============================================================================

[CmdletBinding()]
param(
    [ValidateSet('update', 'migrations', 'script', 'info')]
    [string]$Action = 'update',

    [string]$EnvFile = '.env',

    [string]$StartupProject = 'src/Fitmate.Api',

    [string]$Project = 'src/Fitmate.Infrastructure',

    [string]$Migration,                  # for 'update' with target migration

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# -----------------------------------------------------------------------------
# 1. Resolve repo root (script lives in <root>/scripts/).
# -----------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..')
Push-Location $RepoRoot
try {

    # -----------------------------------------------------------------------------
    # 2. Load env from file.
    #    Order: -EnvFile path -> .env -> .env.example
    # -----------------------------------------------------------------------------
    function Load-EnvFile([string]$Path) {
        if (-not (Test-Path $Path)) {
            throw "Env file not found: $Path"
        }
        Get-Content $Path | ForEach-Object {
            $line = $_.Trim()
            if ($line.Length -eq 0) { return }
            if ($line.StartsWith('#')) { return }
            $eq = $line.IndexOf('=')
            if ($eq -lt 1) { return }
            $name = $line.Substring(0, $eq).Trim()
            $value = $line.Substring($eq + 1).Trim()

            # Strip surrounding quotes if any.
            if ($value.Length -ge 2) {
                if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                    ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
            }
            [System.Environment]::SetEnvironmentVariable($name, $value, 'Process')
            Write-Host "  $name = $value"
        }
    }

    $envPath = if (Test-Path $EnvFile) {
        $EnvFile
    } elseif (Test-Path '.env') {
        '.env'
    } elseif (Test-Path '.env.example') {
        Write-Warning '.env not found; falling back to .env.example (placeholders will NOT work for DB_PASSWORD).'
        '.env.example'
    } else {
        throw 'No env file found. Create .env from .env.example first.'
    }

    Write-Host "[ef-update] Loading env from: $envPath"
    Load-EnvFile $envPath

    # -----------------------------------------------------------------------------
    # 3. Defaults that mirror Program.cs.
    # -----------------------------------------------------------------------------
    if (-not $env:DB_HOST)     { $env:DB_HOST     = 'localhost' }
    if (-not $env:DB_PORT)     { $env:DB_PORT     = '5432' }
    if (-not $env:DB_NAME)     { $env:DB_NAME     = 'fitmate' }
    if (-not $env:DB_USER)     { $env:DB_USER     = 'fitmate' }
    if (-not $env:DB_PASSWORD) {
        throw 'DB_PASSWORD is required. Set it in .env (do NOT commit).'
    }

    Write-Host ''
    Write-Host "[ef-update] Target: $($env:DB_HOST):$($env:DB_PORT)/$($env:DB_NAME) as $($env:DB_USER)"
    Write-Host "[ef-update] Startup: $StartupProject | Project: $Project"
    Write-Host ''

    # -----------------------------------------------------------------------------
    # 4. Build args for dotnet ef.
    # -----------------------------------------------------------------------------
    $efArgs = @(
        'ef'
    )

    switch ($Action) {
        'update' {
            $efArgs += 'database', 'update'
            if ($Migration) { $efArgs += $Migration }
        }
        'migrations' {
            $efArgs += 'migrations', 'list'
        }
        'script' {
            $efArgs += 'migrations', 'script'
            if ($Migration) { $efArgs += $Migration }
        }
        'info' {
            $efArgs += 'dbcontext', 'info'
        }
    }

    $efArgs += '--project', $Project
    $efArgs += '--startup-project', $StartupProject
    if ($NoBuild) { $efArgs += '--no-build' }

    Write-Host "[ef-update] dotnet $($efArgs -join ' ')"
    Write-Host ''

    & dotnet @efArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host '[ef-update] Done.'
