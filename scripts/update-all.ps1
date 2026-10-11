# Updates all routing and map data from one fresh OSM snapshot, so the server and the
# phones route on the same data:
#   1. downloads the Geofabrik extracts of every region and of the server graph,
#   2. builds every offline package (graph + places with points of interest) and cuts
#      every map from the newest Protomaps daily build, into a staging folder,
#   3. rebuilds the server graph: OSRM, then a new PostGIS graph version,
#   4. checks everything (sizes, map schema against the style, routes, place search),
#   5. publishes only what passed: files are swapped in place, the old graph version is
#      deleted (or the previous one is reactivated when the new one fails).
# The phones then show "Ažuriraj" for every changed package and map.
#
#   pwsh scripts/update-all.ps1                    # everything
#   pwsh scripts/update-all.ps1 -Vacuum            # also VACUUM FULL (locks routing for a while; run at night)
#   pwsh scripts/update-all.ps1 -SkipServer        # phones only (packages + maps)
#   pwsh scripts/update-all.ps1 -SkipMaps -SkipEurope
param(
    [switch]$SkipServer,
    [switch]$SkipMaps,
    [switch]$SkipEurope,
    [switch]$Vacuum,
    [int]$MaxAgeDays = 1,
    [string]$ProtomapsBuild,
    [string]$ApiUrl = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repo
$stamp = Get-Date -Format "yyyyMMdd-HHmm"
New-Item -ItemType Directory -Force "logs" | Out-Null
Start-Transcript -Path "logs/update-all-$stamp.log" | Out-Null

$lock = Join-Path $repo "osm/update-all.lock"
if ((Test-Path $lock) -and (Get-Process -Id (Get-Content $lock) -ErrorAction SilentlyContinue)) {
    throw "update-all is already running (PID $(Get-Content $lock))."
}
Set-Content $lock $PID

# Same commands on the Windows dev machine and the Linux server (pmtiles and curl on PATH).
$pmtiles = if ($IsWindows) { Join-Path $repo "wwwroot/maps/pmtiles.exe" } else { "pmtiles" }
$curl = if ($IsWindows) { "curl.exe" } else { "curl" }
$regions = Get-Content "Tools/OfflineBundleBuilder/regions.json" -Raw | ConvertFrom-Json -AsHashtable
$server = $regions["_server"]
$staging = Join-Path $repo "osm/staging-$stamp"
$failures = [System.Collections.Generic.List[string]]::new()
$published = [System.Collections.Generic.List[string]]::new()

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

function Invoke-Sql([string]$sql) {
    $out = $sql | docker exec -i promap-postgres psql -U promap -d promapcargo -v ON_ERROR_STOP=1 -At
    if ($LASTEXITCODE -ne 0) { throw "SQL failed: $sql" }
    return $out
}

# -PostGis: the truck router itself must find the route within 5 s (not the OSRM fallback);
# a larger graph must not make it slower.
function Test-Route([string]$name, [double[]]$from, [double[]]$to, [double]$minKm, [double]$maxKm, [switch]$PostGis) {
    $body = @{ startLat = $from[0]; startLon = $from[1]; endLat = $to[0]; endLon = $to[1] } | ConvertTo-Json
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-RestMethod "$ApiUrl/api/routing/route" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 360
        $seconds = $watch.Elapsed.TotalSeconds
        $km = $r.summary.distanceMeters / 1000
        $ok = $r.code -eq "Ok" -and $km -ge $minKm -and $km -le $maxKm -and
            (-not $PostGis -or ("$($r.diagnostics.engine)".StartsWith("PostGIS") -and $seconds -le 5))
        Write-Host ("  {0}: {1:N1} km via {2} in {3:N1} s {4}" -f $name, $km, $r.diagnostics.engine, $seconds, $(if ($ok) { "OK" } else { "FAIL" }))
        return $ok
    } catch {
        Write-Host "  ${name}: FAIL ($($_.Exception.Message))"
        return $false
    }
}

function Test-Search([string]$query) {
    $r = @(Invoke-RestMethod "$ApiUrl/api/geocode?q=$([uri]::EscapeDataString($query))&limit=1" -TimeoutSec 120)
    $ok = $r.Count -gt 0
    Write-Host "  search '$query': $(if ($ok) { $r[0].displayName } else { 'FAIL (no result)' })"
    return $ok
}

# A rebuilt file must not be much smaller than the one it replaces (a broken download or
# a failed step shows up as a small file).
function Test-Size([string]$new, [string]$current) {
    if (-not (Test-Path $current)) { return $true }
    $ratio = (Get-Item $new).Length / (Get-Item $current).Length
    if ($ratio -lt 0.9) { Write-Host ("  {0}: {1:P0} of the current size" -f (Split-Path $new -Leaf), $ratio); return $false }
    return $true
}

try {
    # ---- 1. OSM snapshot ----
    Step "1. OSM extracts (Geofabrik)"
    $countries = $regions.Keys | Where-Object { $_ -ne "_comment" } | ForEach-Object { $regions[$_].geofabrik } | Sort-Object -Unique
    foreach ($country in $countries) {
        $file = "osm/geofabrik/$($country -replace '/', '-')-latest.osm.pbf"
        if ((Test-Path $file) -and ((Get-Date) - (Get-Item $file).LastWriteTime).TotalDays -lt $MaxAgeDays) { continue }
        Write-Host "  $country"
        & $curl -L --fail --retry 3 -s -o "$file.part" "https://download.geofabrik.de/europe/$country-latest.osm.pbf"
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $country" }
        Move-Item -Force "$file.part" $file
    }

    # ---- 2. Map source: newest Protomaps build that still fits the style ----
    $mapSource = $null
    if (-not $SkipMaps) {
        Step "2. Map source (Protomaps daily build)"
        $dates = if ($ProtomapsBuild) { @($ProtomapsBuild) } else { 0..3 | ForEach-Object { (Get-Date).AddDays(-$_).ToString("yyyyMMdd") } }
        foreach ($date in $dates) {
            $url = "https://build.protomaps.com/$date.pmtiles"
            $metadata = & $pmtiles show --metadata $url 2>$null
            if ($LASTEXITCODE -eq 0 -and $metadata) { $mapSource = $url; break }
        }

        if (-not $mapSource) {
            $failures.Add("maps: no Protomaps build found")
        } else {
            # The style (promap-dark3.json) is written for Protomaps Basemap v4: every
            # source layer it uses must exist, else the maps are not updated.
            $meta = $metadata | ConvertFrom-Json
            $style = Get-Content "wwwroot/styles/promap-dark3.json" -Raw | ConvertFrom-Json
            $needed = $style.layers | Where-Object { $_.'source-layer' } | ForEach-Object { $_.'source-layer' } | Sort-Object -Unique
            $present = $meta.vector_layers | ForEach-Object { $_.id }
            $missing = $needed | Where-Object { $_ -notin $present }
            if ($meta.name -ne "Protomaps Basemap" -or -not "$($meta.version)".StartsWith("4.") -or $missing) {
                $failures.Add("maps: $mapSource is '$($meta.name)' $($meta.version), missing layers: $($missing -join ', ') - maps not updated")
                $mapSource = $null
            } else {
                Write-Host "  $mapSource ($($meta.name) $($meta.version), OSM $($meta.'planetiler:osm:osmosisreplicationtime'))"
            }
        }
    }

    # ---- 3. Offline packages and maps into staging ----
    Step "3. Offline packages$(if ($mapSource) { ' and maps' })"
    $buildArgs = @{ Region = "all"; SkipDownload = $true; Force = $true; BundleDir = "$staging/bundles"; MapDir = "$staging/maps" }
    if ($mapSource) { $buildArgs.MapSource = $mapSource } else { $buildArgs.SkipMap = $true }
    & "$repo/Tools/OfflineBundleBuilder/build-region.ps1" @buildArgs
    if ($mapSource -and -not $SkipEurope) {
        Write-Host "  europe.pmtiles"
        & $pmtiles extract $mapSource "$staging/maps/europe.pmtiles" "--bbox=-25,34,45,72" "--maxzoom=12"
        if ($LASTEXITCODE -ne 0) { $failures.Add("maps: europe.pmtiles extract failed") }
    }

    # ---- 4. Server graph (a failure here does not stop the phone packages) ----
    if (-not $SkipServer) {
      try {
        Step "4. Server graph ($($server.name))"
        $previous = Invoke-Sql "SELECT graph_version FROM routing_graph_versions WHERE status='ready' AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1;"
        & "$repo/scripts/build-server-graph.ps1" -Name "$($server.name)-next" -Import

        # OSRM: swap the files in and restart (the old data stays until the swap).
        Get-ChildItem "osm/$($server.name)-next*" | ForEach-Object {
            Move-Item -Force $_.FullName (Join-Path $_.DirectoryName ($_.Name -replace "-next", ""))
        }
        docker compose restart osrm | Out-Null
        Start-Sleep 20

        $current = Invoke-Sql "SELECT graph_version FROM routing_graph_versions WHERE status='ready' AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1;"
        # Run twice: the first request loads the graph tiles into the cache.
        $null = Test-Route "warm-up" @(44.8125, 20.4573) @(43.3209, 21.8958) 200 280
        $routesOk = (Test-Route "Beograd - Niš" @(44.8125, 20.4573) @(43.3209, 21.8958) 200 280 -PostGis) -and
            (Test-Route "Beograd - Zagreb" @(44.8125, 20.4573) @(45.8150, 15.9819) 370 430 -PostGis) -and
            (Test-Route "Beograd - Novi Sad" @(44.8125, 20.4573) @(45.2551, 19.8451) 80 110) -and
            (Test-Route "Beograd - Skoplje" @(44.8125, 20.4573) @(41.9981, 21.4254) 400 480)
        if ($routesOk -and $current -ne $previous) {
            Write-Host "  graph $current active, deleting $previous"
            Invoke-Sql @"
BEGIN;
DELETE FROM routing_overlay_edges WHERE graph_version = $previous;
DELETE FROM routing_edge_cells WHERE graph_version = $previous;
DELETE FROM routing_node_cells WHERE graph_version = $previous;
DELETE FROM routing_boundaries WHERE graph_version = $previous;
DELETE FROM routing_cell_adjacency WHERE graph_version = $previous;
DELETE FROM routing_cells WHERE graph_version = $previous;
DELETE FROM compiled_turn_restrictions WHERE graph_version = $previous;
DELETE FROM turn_restrictions WHERE graph_version = $previous;
DELETE FROM road_edges WHERE graph_version = $previous;
DELETE FROM osm_way_nodes WHERE graph_version = $previous;
DELETE FROM osm_ways WHERE graph_version = $previous;
DELETE FROM osm_nodes WHERE graph_version = $previous;
DELETE FROM routing_graph_versions WHERE graph_version = $previous;
COMMIT;
"@ | Out-Null
            $published.Add("server graph $current")
        } elseif (-not $routesOk) {
            if ($current -ne $previous) {
                Invoke-Sql "UPDATE routing_graph_versions SET status='failed', activated_at=NULL WHERE graph_version=$current; UPDATE routing_graph_versions SET activated_at=now() WHERE graph_version=$previous;" | Out-Null
            }
            $failures.Add("server: test routes failed, graph $previous stays active")
        }

        if ($Vacuum) {
            Write-Host "  VACUUM FULL (routing pauses meanwhile)"
            Invoke-Sql "VACUUM FULL ANALYZE osm_nodes; VACUUM FULL ANALYZE osm_way_nodes; VACUUM FULL ANALYZE osm_ways; VACUUM FULL ANALYZE road_edges;" | Out-Null
        }
      } catch {
        $failures.Add("server: $($_.Exception.Message)")
      }
    }

    # ---- 5. Publish packages and maps that passed ----
    Step "5. Publish"
    foreach ($id in $regions.Keys | Where-Object { $_ -notlike "_*" }) {
        # places first, graph second: the catalog version follows the newest file, so the
        # app sees the update only when both are in place.
        $files = @("$staging/bundles/$id.places", "$staging/bundles/$id.graphbundle")
        $missing = @($files | Where-Object { -not (Test-Path $_) })
        $small = @($files | Where-Object { (Test-Path $_) -and -not (Test-Size $_ "wwwroot/offline-bundles/$(Split-Path $_ -Leaf)") })
        if ($missing.Count -gt 0 -or $small.Count -gt 0) {
            $failures.Add("package ${id}: missing or too small, not published")
            continue
        }
        try {
            $files | ForEach-Object { Move-Item -Force $_ "wwwroot/offline-bundles/" }
            $published.Add("package $id")
        } catch {
            $failures.Add("package ${id}: $($_.Exception.Message)")
        }
    }

    foreach ($map in Get-ChildItem "$staging/maps/*.pmtiles" -ErrorAction SilentlyContinue) {
        if (-not (Test-Size $map.FullName "wwwroot/maps/$($map.Name)")) {
            $failures.Add("map $($map.Name): too small, not published")
            continue
        }
        try {
            Move-Item -Force $map.FullName "wwwroot/maps/"
            $published.Add("map $($map.Name)")
        } catch {
            # e.g. a phone is downloading it right now: the next run publishes it.
            $failures.Add("map $($map.Name): $($_.Exception.Message)")
        }
    }

    # Checksums of the new files: the app verifies every download against them.
    & "$repo/scripts/write-checksums.ps1"

    # ---- 6. Checks against the running API ----
    Step "6. Checks"
    foreach ($query in "Zagreb", "Niš", "pumpa, Novi Sad", "granični prelaz Batrovci") {
        if (-not (Test-Search $query)) { $failures.Add("search '$query' found nothing") }
    }
    $catalog = @(Invoke-RestMethod "$ApiUrl/api/offline-routing-bundles")
    Write-Host "  catalog: $($catalog.Count) routing packages, $(@(Invoke-RestMethod "$ApiUrl/api/map-packages").Count) maps"
}
catch {
    $failures.Add("aborted: $($_.Exception.Message)")
}
finally {
    Step "Summary"
    $published | ForEach-Object { Write-Host "  published: $_" -ForegroundColor Green }
    $failures | ForEach-Object { Write-Host "  FAILED: $_" -ForegroundColor Red }
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    Remove-Item $lock -ErrorAction SilentlyContinue
    Stop-Transcript | Out-Null
}

exit $(if ($failures.Count -gt 0) { 1 } else { 0 })
