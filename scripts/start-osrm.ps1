#!/usr/bin/env pwsh
param(
	[string]$DataDir = "",
	[string]$Dataset = "europe-truck",
	[int]$Port = 5090,
	[string]$Algorithm = "mld",
	[string]$OsrmExecutable = "",
	[switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DataDir)) {
	$DataDir = Join-Path (Split-Path -Parent $PSScriptRoot) "osm"
}

function Test-OsrmBundle {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Prefix
	)

	$suffixes = @(
		".osrm",
		".osrm.cells",
		".osrm.cell_metrics",
		".osrm.cnbg",
		".osrm.cnbg_to_ebg",
		".osrm.datasource_names",
		".osrm.ebg",
		".osrm.ebg_nodes",
		".osrm.edges",
		".osrm.enw",
		".osrm.fileIndex",
		".osrm.geometry",
		".osrm.icd",
		".osrm.maneuver_overrides",
		".osrm.mldgr",
		".osrm.names",
		".osrm.nbg_nodes",
		".osrm.partition",
		".osrm.properties",
		".osrm.ramIndex",
		".osrm.restrictions",
		".osrm.timestamp",
		".osrm.tld",
		".osrm.tls",
		".osrm.turn_duration_penalties",
		".osrm.turn_penalties_index",
		".osrm.turn_weight_penalties"
	)

	foreach ($suffix in $suffixes) {
		if (-not (Test-Path ($Prefix + $suffix))) {
			return $false
		}
	}

	return $true
}

function Resolve-OsrmExecutable {
	param(
		[string]$ConfiguredPath
	)

	$repoRoot = Split-Path -Parent $PSScriptRoot
	$candidates = @(
		$ConfiguredPath,
		$env:OSRM_ROUTED_PATH,
		$(if ($env:OSRM_HOME) { Join-Path $env:OSRM_HOME "osrm-routed.exe" }),
		$(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles "OSRM\osrm-routed.exe" }),
		$(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} "OSRM\osrm-routed.exe" }),
		(Join-Path $repoRoot "tools\osrm\osrm-routed.exe"),
		(Join-Path $repoRoot "osrm\osrm-routed.exe")
	) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

	foreach ($candidate in $candidates) {
		if (Test-Path $candidate) {
			return (Resolve-Path $candidate).Path
		}
	}

	$command = Get-Command osrm-routed -ErrorAction SilentlyContinue
	if ($null -ne $command) {
		return $command.Source
	}

	throw "osrm-routed was not found. Set -OsrmExecutable, OSRM_ROUTED_PATH, or OSRM_HOME, or install osrm-routed on PATH."
}

function Test-PortAvailable {
	param(
		[int]$PortNumber
	)

	$listener = Get-NetTCPConnection -State Listen -LocalPort $PortNumber -ErrorAction SilentlyContinue
	return $null -eq $listener
}

$preferredPrefix = Join-Path $DataDir $Dataset
$fallbackPrefix = Join-Path $DataDir "serbia-latest"

if (Test-OsrmBundle -Prefix $preferredPrefix) {
	$datasetPrefix = $preferredPrefix
	Write-Host "[info] Selected OSRM dataset: $datasetPrefix"
}
elseif (Test-OsrmBundle -Prefix $fallbackPrefix) {
	$datasetPrefix = $fallbackPrefix
	Write-Host "[warn] Preferred dataset '$preferredPrefix' is incomplete. Falling back to '$datasetPrefix'."
}
else {
	throw "No complete OSRM dataset bundle found under '$DataDir'."
}

$resolvedExecutable = $null
$useDockerRuntime = $false

try {
	$resolvedExecutable = Resolve-OsrmExecutable -ConfiguredPath $OsrmExecutable
}
catch {
	$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
	if ($null -ne $dockerCommand) {
		$useDockerRuntime = $true
		Write-Host "[warn] osrm-routed executable was not found. Falling back to Docker runtime (osrm/osrm-backend)."
	}
	else {
		throw
	}
}

if (-not $ValidateOnly -and -not (Test-PortAvailable -PortNumber $Port)) {
	throw "Port $Port is already in use."
}

if ($ValidateOnly) {
	Write-Host "[info] OSRM validation succeeded."
	if ($useDockerRuntime) {
		Write-Host "[info] Runtime: Docker (osrm/osrm-backend)"
	}
	else {
		Write-Host "[info] Executable: $resolvedExecutable"
	}
	Write-Host "[info] Dataset: $datasetPrefix"
	Write-Host "[info] Port: $Port"
	return
}

Write-Host "[info] Starting OSRM on port $Port with dataset: $datasetPrefix"

if ($useDockerRuntime) {
	$resolvedDataDir = (Resolve-Path $DataDir).Path
	$datasetFileName = [System.IO.Path]::GetFileName("$datasetPrefix.osrm")
	$containerName = "promap-osrm-local"

	# Best effort cleanup in case previous run left a container behind.
	& docker rm -f $containerName *> $null

	& docker run --rm --name $containerName -p "${Port}:5000" -v "${resolvedDataDir}:/data:ro" osrm/osrm-backend osrm-routed --algorithm $Algorithm --port 5000 "/data/$datasetFileName"
}
else {
	& $resolvedExecutable --algorithm $Algorithm --port $Port "$datasetPrefix.osrm"
}
