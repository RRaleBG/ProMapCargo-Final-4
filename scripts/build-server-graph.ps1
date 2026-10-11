# Builds the server routing data (PostGIS graph + OSRM) for a set of countries from the
# Geofabrik extracts in osm/geofabrik (download them with Tools/OfflineBundleBuilder/build-region.ps1
# or from https://download.geofabrik.de/europe/).
#
#   pwsh scripts/build-server-graph.ps1                 # merge + filter + OSRM, then prints the import command
#   pwsh scripts/build-server-graph.ps1 -Import         # also imports the graph into PostGIS
#
# The extracts overlap at borders and may be from different days: they are merged as a
# history file and time-filtered so each object is kept once, in its newest version.
# OSRM uses the standard driving profile (car.lua); truck rules live in the PostGIS router.
# Countries and output name default to "_server" in Tools/OfflineBundleBuilder/regions.json.
param(
    [string[]]$Countries,
    [string]$Name,
    [switch]$Import
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$server = (Get-Content (Join-Path $repo "Tools/OfflineBundleBuilder/regions.json") -Raw | ConvertFrom-Json -AsHashtable)["_server"]
if (-not $Countries) { $Countries = $server.geofabrik }
if (-not $Name) { $Name = $server.name }
$osm = Join-Path $repo "osm"

function Invoke-Docker {
    docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') failed" }
}

# osmium-tool from the Ubuntu archive, as a local image (no third-party image).
docker image inspect promap-osmium *> $null
if ($LASTEXITCODE -ne 0) {
    "FROM ubuntu:24.04`nRUN apt-get update && apt-get install -y --no-install-recommends osmium-tool && rm -rf /var/lib/apt/lists/*`nWORKDIR /data`nENTRYPOINT [`"osmium`"]" |
        docker build -t promap-osmium -
    if ($LASTEXITCODE -ne 0) { throw "Building the osmium image failed." }
}

$inputs = foreach ($country in $Countries) {
    $file = Join-Path $osm "geofabrik/$country-latest.osm.pbf"
    if (-not (Test-Path $file)) { throw "Missing extract: $file" }
    "geofabrik/$country-latest.osm.pbf"
}

$volume = "${osm}:/data"
Write-Host "[1/4] Merging $($Countries.Count) extracts -> $Name.osm.pbf"
Invoke-Docker run --rm -v $volume promap-osmium merge @inputs -o "tmp-$Name.osh.pbf" --overwrite
Invoke-Docker run --rm -v $volume promap-osmium time-filter "tmp-$Name.osh.pbf" -o "$Name.osm.pbf" --overwrite
Remove-Item (Join-Path $osm "tmp-$Name.osh.pbf")

# The PostGIS importer only uses highway ways and turn restrictions (and keeps every node
# it reads), so it gets just those.
Write-Host "[2/4] Roads and turn restrictions -> $Name-roads.osm.pbf"
Invoke-Docker run --rm -v $volume promap-osmium tags-filter "$Name.osm.pbf" w/highway r/type=restriction -o "$Name-roads.osm.pbf" --overwrite

Write-Host "[3/4] OSRM (car.lua, MLD) -> $Name.osrm"
Invoke-Docker run --rm -v $volume osrm/osrm-backend:latest osrm-extract -p /opt/car.lua "/data/$Name.osm.pbf"
Invoke-Docker run --rm -v $volume osrm/osrm-backend:latest osrm-partition "/data/$Name.osrm"
Invoke-Docker run --rm -v $volume osrm/osrm-backend:latest osrm-customize "/data/$Name.osrm"
Write-Host "      Point the osrm command in docker-compose.yml at /data/$Name.osrm, then: docker compose up -d osrm"

$importCommand = "dotnet run --project Importer -c Release -- osm/$Name-roads.osm.pbf"
if ($Import) {
    Write-Host "[4/4] PostGIS import (the new version becomes active when it is ready)"
    Push-Location $repo
    try {
        dotnet run --project Importer -c Release -- "osm/$Name-roads.osm.pbf"
        if ($LASTEXITCODE -ne 0) { throw "Import failed." }
    }
    finally { Pop-Location }
} else {
    Write-Host "[4/4] PostGIS import: $importCommand"
}
Write-Host "Afterwards delete the old graph version (Sql/06-graph-maintenance.sql, section 2)."
