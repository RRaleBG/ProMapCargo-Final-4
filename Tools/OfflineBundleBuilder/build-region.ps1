# Builds an offline region for the mobile app (see regions.json):
#   1. downloads the countries' OSM extracts from Geofabrik into osm/geofabrik/
#      (only when missing or older than -MaxAgeDays),
#   2. builds <region>.graphbundle + <region>.places into wwwroot/offline-bundles,
#   3. cuts the navigation map <region>-nav.pmtiles from wwwroot/maps/europe.pmtiles.
# The API serves both folders; the app lists the region in "Offline mape".
#
#   pwsh Tools/OfflineBundleBuilder/build-region.ps1 -Region zapadni-balkan
#   pwsh Tools/OfflineBundleBuilder/build-region.ps1 -Region all
#   pwsh Tools/OfflineBundleBuilder/build-region.ps1 -Region koridor-10 -SkipDownload -Force
#
# scripts/update-all.ps1 builds into a staging folder (-BundleDir, -MapDir) and cuts the
# maps from a fresh Protomaps build (-MapSource https://build.protomaps.com/<date>.pmtiles).
param(
    [Parameter(Mandatory)] [string]$Region,
    [int]$MaxAgeDays = 14,
    [switch]$SkipDownload,
    [switch]$SkipMap,
    [switch]$Force,
    [string]$BundleDir = "wwwroot/offline-bundles",
    [string]$MapDir = "wwwroot/maps",
    [string]$MapSource = "wwwroot/maps/europe.pmtiles",
    [int]$MaxZoom = 12
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "../..")
$regions = Get-Content (Join-Path $PSScriptRoot "regions.json") -Raw | ConvertFrom-Json -AsHashtable
$ids = if ($Region -eq "all") { $regions.Keys | Where-Object { $_ -notlike "_*" } } else { $Region -split "," }
# Same commands on the Windows dev machine and the Linux server (pmtiles and curl on PATH).
$pmtiles = if ($IsWindows) { Join-Path $repo "wwwroot/maps/pmtiles.exe" } else { "pmtiles" }
$curl = if ($IsWindows) { "curl.exe" } else { "curl" }
$python = if ($IsWindows) { "python" } else { "python3" }
$extracts = Join-Path $repo "osm/geofabrik"
New-Item -ItemType Directory -Force $extracts | Out-Null

Push-Location $repo
try {
    dotnet build Tools/OfflineBundleBuilder -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Builder build failed." }

    foreach ($id in $ids) {
        $r = $regions[$id]
        if (-not $r) { throw "Unknown region '$id' (see regions.json)." }
        Write-Host "=== $($r.title) ($id) ===" -ForegroundColor Cyan

        # 1. OSM extracts
        $pbfArgs = @()
        foreach ($country in $r.geofabrik) {
            $file = Join-Path $extracts (($country -replace "/", "-") + "-latest.osm.pbf")
            $stale = -not (Test-Path $file) -or ((Get-Date) - (Get-Item $file).LastWriteTime).TotalDays -gt $MaxAgeDays
            if ($stale -and -not $SkipDownload) {
                $url = "https://download.geofabrik.de/europe/$country-latest.osm.pbf"
                Write-Host "  downloading $url"
                & $curl -L --fail --retry 3 -o "$file.part" $url
                if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
                Move-Item -Force "$file.part" $file
            }
            if (-not (Test-Path $file)) { throw "Missing extract $file (run without -SkipDownload)." }
            $pbfArgs += @("--pbf", $file)
        }

        # 2. Routing + search package
        $builderArgs = @("Tools/OfflineBundleBuilder/bin/Release/net10.0/ProMapCargo.OfflineBundleBuilder.dll",
            $BundleDir, "--name", $id, "--title", $r.title) + $pbfArgs
        if ($r.slim) { $builderArgs += "--slim" }
        if ($Force) { $builderArgs += "--force" }
        dotnet @builderArgs
        if ($LASTEXITCODE -ne 0) { throw "Package build failed for $id." }

        # 3. Navigation map cut from the Europe archive (same style as online)
        if (-not $SkipMap) {
            New-Item -ItemType Directory -Force $MapDir | Out-Null
            $mapFile = Join-Path $MapDir "$id-nav.pmtiles"
            if ($Force -or -not (Test-Path $mapFile)) {
                $geojson = Join-Path ([System.IO.Path]::GetTempPath()) "$id-region.geojson"
                & $python Tools/OfflineBundleBuilder/region-geojson.py osm/countries.geojson $geojson @($r.map)
                if ($LASTEXITCODE -ne 0) { throw "Region polygon failed for $id." }
                & $pmtiles extract $MapSource $mapFile "--region=$geojson" "--maxzoom=$MaxZoom"
                if ($LASTEXITCODE -ne 0) { throw "Map extract failed for $id." }
                Write-Host ("  map: {0} ({1:N0} MB)" -f $mapFile, ((Get-Item $mapFile).Length / 1MB))
            }
        }
    }
}
finally {
    Pop-Location
}
