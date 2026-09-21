param(
    [switch]$SkipAssetDownload,
    [switch]$SkipPmtilesBuild
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Write-Host "Applying ProMap Cargo local-map kit to $Root"

$layout = Join-Path $Root "Pages/Shared/_Layout.cshtml"
$compose = Join-Path $Root "docker-compose.yml"
$docker = Join-Path $Root "Dockerfile"
$navSrc = Join-Path $PSScriptRoot "../wwwroot/js/navigation.js"
$navDst = Join-Path $Root "wwwroot/js/navigation.js"
$styleSrc = Join-Path $PSScriptRoot "../wwwroot/styles/promap-dark.json"
$styleDst = Join-Path $Root "wwwroot/styles/promap-dark.json"

Copy-Item $navSrc $navDst -Force
Copy-Item $styleSrc $styleDst -Force
New-Item -ItemType Directory -Force -Path (Join-Path $Root "wwwroot/styles") | Out-Null

if (Test-Path $layout) {
    $text = Get-Content $layout -Raw
    $text = $text.Replace('https://unpkg.com/leaflet@1.9.4/dist/leaflet.css','~/lib/leaflet/leaflet.css')
    $text = $text.Replace('https://unpkg.com/maplibre-gl@6.10.0/dist/maplibre-gl.css','~/lib/maplibre-gl/dist/maplibre-gl.css')
    $text = $text.Replace('https://unpkg.com/leaflet@1.9.4/dist/leaflet.js','~/lib/leaflet/leaflet.js')
    $text = $text.Replace('https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js','~/lib/signalr/signalr.min.js')
    $text = $text.Replace('https://unpkg.com/maplibre-gl@6.10.0/dist/maplibre-gl.js','~/lib/maplibre-gl/dist/maplibre-gl.js')
    if ($text -notmatch 'lib/pmtiles/dist/pmtiles.js') {
        $marker = '<script src="~/lib/maplibre-gl/dist/maplibre-gl.js" defer></script>'
        $text = $text.Replace($marker, $marker + "`r`n    <script src=`"~/lib/pmtiles/dist/pmtiles.js`" defer></script>")
    }
    Set-Content $layout $text -Encoding UTF8
}

if (Test-Path $compose) {
    $text = Get-Content $compose -Raw
    $text = [regex]::Replace($text, '(?m)^\s*TomTom__ApiKey:.*\r?\n', '')
    $text = [regex]::Replace($text, '(?m)^\s*TomTom__TrafficStyle:.*\r?\n', '')
    Set-Content $compose $text -Encoding UTF8
}


$dockerTemplate = Join-Path $PSScriptRoot "../Dockerfile.local-map"
$dockerDst = Join-Path $Root "Dockerfile"
if (Test-Path $dockerTemplate) { Copy-Item $dockerTemplate $dockerDst -Force }

$gitignore = Join-Path $Root ".gitignore"
if (Test-Path $gitignore) {
    $gi = Get-Content $gitignore -Raw
    if ($gi -notmatch '(?m)^/wwwroot/maps/\*\.pmtiles\s*$') {
        Add-Content $gitignore "`r`n# Generated local vector map`r`n/wwwroot/maps/*.pmtiles`r`n"
    }
}

if (-not $SkipAssetDownload) {
    & (Join-Path $PSScriptRoot "prepare-local-map-assets.ps1")
}

if (-not $SkipPmtilesBuild) {
    $osm = Join-Path $Root "osm/serbia-latest.osm.pbf"
    if (Test-Path $osm) {
        & (Join-Path $PSScriptRoot "build-serbia-pmtiles.ps1")
    } else {
        Write-Warning "serbia-latest.osm.pbf is missing; skipping PMTiles generation."
    }
}

Write-Host "Local map kit applied."
