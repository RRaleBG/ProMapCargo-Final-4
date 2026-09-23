#!/usr/bin/env pwsh
param(
	[string]$ServiceName = "ProMapCargo.Osrm5090",
	[string]$DisplayName = "ProMapCargo OSRM 5090",
	[string]$Description = "Local OSRM routing service for ProMapCargo on port 5090.",
	[string]$DataDir = "",
	[string]$Dataset = "europe-truck",
	[int]$Port = 5090,
	[string]$Algorithm = "mld",
	[string]$OsrmExecutable = "",
	[switch]$StartAfterInstall
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "start-osrm.ps1"
if (-not (Test-Path $scriptPath)) {
	throw "start-osrm.ps1 was not found at '$scriptPath'."
}

if ([string]::IsNullOrWhiteSpace($DataDir)) {
	$DataDir = Join-Path (Split-Path -Parent $PSScriptRoot) "osm"
}

& $scriptPath -DataDir $DataDir -Dataset $Dataset -Port $Port -Algorithm $Algorithm -OsrmExecutable $OsrmExecutable -ValidateOnly

$pwshCommand = (Get-Command pwsh -ErrorAction SilentlyContinue)?.Source
if ([string]::IsNullOrWhiteSpace($pwshCommand)) {
	throw "pwsh was not found on PATH."
}

$quotedScript = '"' + $scriptPath + '"'
$quotedDataDir = '"' + $DataDir + '"'
$quotedExecutable = '"' + $OsrmExecutable + '"'
$binaryPath = '"' + $pwshCommand + '" -NoLogo -NoProfile -File ' + $quotedScript + ' -DataDir ' + $quotedDataDir + ' -Dataset ' + $Dataset + ' -Port ' + $Port + ' -Algorithm ' + $Algorithm
if (-not [string]::IsNullOrWhiteSpace($OsrmExecutable)) {
	$binaryPath += ' -OsrmExecutable ' + $quotedExecutable
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
	if ($existing.Status -eq 'Running') {
		Stop-Service -Name $ServiceName -Force
	}

	sc.exe delete $ServiceName | Out-Null
	Start-Sleep -Seconds 2
}

sc.exe create $ServiceName binPath= $binaryPath start= demand DisplayName= $DisplayName | Out-Null
sc.exe description $ServiceName $Description | Out-Null

Write-Host "Installed Windows service '$ServiceName' for OSRM on port $Port." -ForegroundColor Green

if ($StartAfterInstall) {
	Start-Service -Name $ServiceName
	Write-Host "Started Windows service '$ServiceName'." -ForegroundColor Green
}
