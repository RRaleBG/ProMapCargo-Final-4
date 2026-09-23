#!/usr/bin/env pwsh
<#
.SYNOPSIS
	Pokreće ProMapCargo Development Environment.

.DESCRIPTION
	Ova skript:
	1. Importa OSM podatke u PostgreSQL (ako nije već importano)
	2. Pokreće ProMapCargo API na localhost:8090

	Zahtjev:
	- dotnet SDK mora biti instaliran
	- PostgreSQL konekcija mora biti dostupna
#>

param(
	[string]$OsmFile = "osm/serbia-latest.osm.pbf",
	[long]$GraphVersion = 0,
	[int]$ApiPort = 8090,
	[int]$OsrmPort = 5090,
	[string]$OsrmExecutable = "",
	[switch]$SkipImport = $false,
	[switch]$SkipOsrm = $false,
	[switch]$UseOsrmService = $false
)

$ErrorActionPreference = "Stop"

# Pronađi root direktorijum
$RootDir = Split-Path -Parent $PSScriptRoot
$ApiProject = Join-Path $RootDir "ProMapCargo.Api.csproj"
$ImporterProject = Join-Path $RootDir "Importer/ProMapCargo.OsmImporter.csproj"
$OsrmScript = Join-Path $RootDir "scripts/start-osrm.ps1"
$OsmFilePath = Join-Path $RootDir $OsmFile

Write-Host "🚀 ProMapCargo Development Environment" -ForegroundColor Cyan
Write-Host "`nKonfiguracija:" -ForegroundColor Yellow
Write-Host "  OSM File: $OsmFile"
Write-Host "  API Port: $ApiPort"
Write-Host "  OSRM Port: $OsrmPort"
Write-Host "  OSRM Service Mode: $UseOsrmService"
Write-Host "  Skip Import: $SkipImport"
Write-Host "  Skip OSRM: $SkipOsrm"

# Provjeri je li OSM datoteka dostupna
if (-not (Test-Path $OsmFilePath)) {
	Write-Host "`n❌ OSM datoteka nije pronađena: $OsmFilePath" -ForegroundColor Red
	Write-Host "   Dostupne datoteke:" -ForegroundColor Yellow
	Get-ChildItem (Join-Path $RootDir "osm") -Filter "*.pbf" -File 2>/dev/null | ForEach-Object { Write-Host "   - osm/$($_.Name)" }
	exit 1
}

Write-Host "✓ OSM datoteka pronađena" -ForegroundColor Green

# Ako GraphVersion nije zadana, koristi trenutni timestamp
if ($GraphVersion -eq 0) {
	$GraphVersion = [long]([System.DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
	Write-Host "✓ Graph Version: $GraphVersion (auto-generated)" -ForegroundColor Green
}

# ============================================================
# IMPORT OSM PODATAKA
# ============================================================

if (-not $SkipImport) {
	Write-Host "`n📥 Importanje OSM podataka..." -ForegroundColor Cyan
	Write-Host "   Projekat: $ImporterProject" -ForegroundColor Cyan
	Write-Host "   OSM File: $OsmFilePath" -ForegroundColor Cyan
	Write-Host "  Graph Version: $GraphVersion" -ForegroundColor Cyan

	Push-Location $RootDir
	try {
		dotnet run --project $ImporterProject -- "$OsmFilePath" $GraphVersion

		if ($LASTEXITCODE -eq 0) {
			Write-Host "`n✅ OSM import uspješan!" -ForegroundColor Green
		} else {
			Write-Host "`n❌ OSM import neuspješan!" -ForegroundColor Red
			exit 1
		}
	} finally {
		Pop-Location
	}
} else {
	Write-Host "`n⏭️  Preskakanje import-a (--SkipImport je postavljen)" -ForegroundColor Yellow
}

# ============================================================
# POKRETANJE OSRM
# ============================================================

$osrmProcess = $null
$osrmServiceName = "ProMapCargo.Osrm5090"

if (-not $SkipOsrm) {
	if (-not (Test-Path $OsrmScript)) {
		Write-Host "`n⚠️  OSRM skripta nije pronađena: $OsrmScript" -ForegroundColor Yellow
	} else {
		Write-Host "`n🗺️  Validacija lokalnog OSRM servisa za localhost:$OsrmPort..." -ForegroundColor Cyan
		$osrmValidationPassed = $false
		try {
			& $OsrmScript -DataDir (Join-Path $RootDir "osm") -Port $OsrmPort -OsrmExecutable $OsrmExecutable -ValidateOnly
			Write-Host "✓ OSRM launcher validacija uspješna" -ForegroundColor Green
			$osrmValidationPassed = $true
		} catch {
			Write-Host "⚠️  OSRM validacija nije uspjela: $($_.Exception.Message)" -ForegroundColor Yellow
		}

		if ($UseOsrmService) {
			$service = Get-Service -Name $osrmServiceName -ErrorAction SilentlyContinue
			if ($null -eq $service) {
				Write-Host "⚠️  Windows servis '$osrmServiceName' nije instaliran. Pokreni scripts/install-osrm-service.ps1." -ForegroundColor Yellow
			} else {
				Write-Host "`n🗺️  Pokretanje Windows OSRM servisa '$osrmServiceName'..." -ForegroundColor Cyan
				Start-Service -Name $osrmServiceName -ErrorAction Stop
			}
		} elseif ($osrmValidationPassed) {
			Write-Host "`n🗺️  Pokretanje lokalnog OSRM servisa na localhost:$OsrmPort..." -ForegroundColor Cyan
			$osrmProcess = Start-Process -FilePath "pwsh" -ArgumentList @(
				"-NoLogo",
				"-NoProfile",
				"-File",
				$OsrmScript,
				"-DataDir",
				(Join-Path $RootDir "osm"),
				"-Port",
				$OsrmPort,
				"-OsrmExecutable",
				$OsrmExecutable
			) -PassThru
			Start-Sleep -Seconds 2
			if ($osrmProcess.HasExited) {
				Write-Host "⚠️  OSRM proces je odmah završen. Provjeri konzolni izlaz, osrm-routed lokaciju i dataset u osm/." -ForegroundColor Yellow
				$osrmProcess = $null
			}
		}
	}
} else {
	Write-Host "`n⏭️  Preskakanje OSRM pokretanja (--SkipOsrm je postavljen)" -ForegroundColor Yellow
}

# ============================================================
# POKRETANJE API
# ============================================================

Write-Host "`n🏗️  Pokretanje ProMapCargo API na localhost:$ApiPort..." -ForegroundColor Cyan

Push-Location $RootDir
try {
	dotnet run --project $ApiProject --urls "http://localhost:$ApiPort" --configuration Debug
} finally {
	if ($null -ne $osrmProcess -and -not $osrmProcess.HasExited) {
		Stop-Process -Id $osrmProcess.Id -Force
	}
	if ($UseOsrmService) {
		$service = Get-Service -Name $osrmServiceName -ErrorAction SilentlyContinue
		if ($null -ne $service -and $service.Status -eq 'Running') {
			Stop-Service -Name $osrmServiceName -ErrorAction SilentlyContinue
		}
	}
	Pop-Location
}

Write-Host "`n⏹️  Servis je zaustavljen." -ForegroundColor Yellow
