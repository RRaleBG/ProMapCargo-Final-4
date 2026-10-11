# Installs the developer tools ProMap Cargo and its Claude Code MCP servers need
# (see CLAUDE.md), skipping what is already installed, and sets PROMAP_DATABASE_URI
# for the read-only Postgres MCP server.
#
#   pwsh scripts/install-dev-tools.ps1
#   pwsh scripts/install-dev-tools.ps1 -SkipDocker      # Docker Desktop already set up another way
#
# Open a new terminal afterwards so the updated PATH and variables are picked up.
param(
    [switch]$SkipDocker
)

$ErrorActionPreference = "Stop"
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw "winget nije dostupan. Instaliraj 'App Installer' iz Microsoft Store-a."
}

$packages = [ordered]@{
    "Git.Git"                  = "git"
    "Microsoft.PowerShell"     = "pwsh (skripte u Tools/ i scripts/)"
    "Microsoft.DotNet.SDK.10"  = ".NET 10 SDK"
    "GitHub.cli"               = "gh (GitHub)"
    "astral-sh.uv"             = "uv / uvx (Postgres MCP)"
    "OpenJS.NodeJS.LTS"        = "Node.js / npx (mobile MCP, Playwright MCP)"
    "Python.Python.3.12"       = "python (region-geojson.py)"
    "Google.PlatformTools"     = "adb (telefon, mobile MCP)"
}
if (-not $SkipDocker) {
    $packages["Docker.DockerDesktop"] = "Docker Desktop (PostGIS, OSRM)"
}

foreach ($id in $packages.Keys) {
    winget list --id $id -e --accept-source-agreements *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[ok]      $($packages[$id])" -ForegroundColor Green
        continue
    }
    Write-Host "[install] $($packages[$id])" -ForegroundColor Cyan
    winget install --id $id -e --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "$id nije instaliran (kod $LASTEXITCODE)."
    }
}

# MAUI Android workload for the mobile app.
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    if ((dotnet workload list) -match "maui-android") {
        Write-Host "[ok]      MAUI Android workload" -ForegroundColor Green
    } else {
        Write-Host "[install] MAUI Android workload" -ForegroundColor Cyan
        dotnet workload install maui-android
    }
} else {
    Write-Warning "dotnet još nije u PATH-u: otvori novi terminal i pokreni skriptu ponovo za MAUI workload."
}

# Connection for the Postgres MCP server (.mcp.json). Same local dev database as docker-compose.yml.
if (-not [Environment]::GetEnvironmentVariable("PROMAP_DATABASE_URI", "User")) {
    [Environment]::SetEnvironmentVariable("PROMAP_DATABASE_URI",
        "postgresql://promap:promap_dev_change_me@localhost:5432/promapcargo", "User")
    Write-Host "[set]     PROMAP_DATABASE_URI" -ForegroundColor Cyan
} else {
    Write-Host "[ok]      PROMAP_DATABASE_URI" -ForegroundColor Green
}

Write-Host ""
Write-Host "Gotovo. Otvori NOVI terminal, pa:" -ForegroundColor Yellow
Write-Host "  gh auth login"
Write-Host "  docker compose up -d postgres osrm"
Write-Host "  claude mcp list"
