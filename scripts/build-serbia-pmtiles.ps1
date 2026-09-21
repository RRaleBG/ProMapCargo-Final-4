param(
    [string]$OsmFile = "serbia-latest.osm.pbf",
    [string]$MapFile = "serbia.pmtiles",
    [string]$Memory = "4g"
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$InputFile = Join-Path $Root "osm/$OsmFile"
$Maps = Join-Path $Root "wwwroot/maps"
if (-not (Test-Path $InputFile)) { throw "OSM file not found: $InputFile" }
New-Item -ItemType Directory -Force -Path $Maps | Out-Null
docker run --rm -e "JAVA_TOOL_OPTIONS=-Xmx$Memory" -v "$Root/osm:/data:ro" -v "$Maps:/output" ghcr.io/onthegomap/planetiler:latest "--osm-path=/data/$OsmFile" "--output=/output/$MapFile" --download --force
Write-Host "DONE: $(Join-Path $Maps $MapFile)"
