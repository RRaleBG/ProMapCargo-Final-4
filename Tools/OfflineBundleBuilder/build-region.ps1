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
param(
    [Parameter(Mandatory)] [string]$Region,
    [int]$MaxAgeDays = 14,
    [switch]$SkipDownload,
    [switch]$SkipMap,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "../..")
$regions = Get-Content (Join-Path $PSScriptRoot "regions.json") -Raw | ConvertFrom-Json -AsHashtable
$ids = if ($Region -eq "all") { $regions.Keys | Where-Object { $_ -notlike "_*" } } else { @($Region) }
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
                curl.exe -L --fail --retry 3 -o "$file.part" $url
                if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
                Move-Item -Force "$file.part" $file
            }
            if (-not (Test-Path $file)) { throw "Missing extract $file (run without -SkipDownload)." }
            $pbfArgs += @("--pbf", $file)
        }

        # 2. Routing + search package
        $builderArgs = @("Tools/OfflineBundleBuilder/bin/Release/net10.0/ProMapCargo.OfflineBundleBuilder.dll",
            "wwwroot/offline-bundles", "--name", $id, "--title", $r.title) + $pbfArgs
        if ($r.slim) { $builderArgs += "--slim" }
        if ($Force) { $builderArgs += "--force" }
        dotnet @builderArgs
        if ($LASTEXITCODE -ne 0) { throw "Package build failed for $id." }

        # 3. Navigation map cut from the Europe archive (same style as online)
        if (-not $SkipMap) {
            $mapFile = "wwwroot/maps/$id-nav.pmtiles"
            if ($Force -or -not (Test-Path $mapFile)) {
                $geojson = Join-Path ([System.IO.Path]::GetTempPath()) "$id-region.geojson"
                python Tools/OfflineBundleBuilder/region-geojson.py osm/countries.geojson $geojson @($r.map)
                if ($LASTEXITCODE -ne 0) { throw "Region polygon failed for $id." }
                & wwwroot/maps/pmtiles.exe extract wwwroot/maps/europe.pmtiles $mapFile "--region=$geojson"
                if ($LASTEXITCODE -ne 0) { throw "Map extract failed for $id." }
                Write-Host ("  map: {0} ({1:N0} MB)" -f $mapFile, ((Get-Item $mapFile).Length / 1MB))
            }
        }
    }
}
finally {
    Pop-Location
}
