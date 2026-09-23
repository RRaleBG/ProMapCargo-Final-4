param(
    [string]$MapLibreVersion = "6.10.0",
    [string]$PmtilesVersion = "4.5.0",
    [string]$LeafletVersion = "1.9.4",
    [string]$SignalRVersion = "8.0.7"
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Www = Join-Path $Root "wwwroot"
$Dirs = @(
    (Join-Path $Www "lib/maplibre-gl/dist"),
    (Join-Path $Www "lib/pmtiles/dist"),
    (Join-Path $Www "lib/leaflet"),
    (Join-Path $Www "lib/leaflet/images"),
    (Join-Path $Www "lib/signalr"),
    (Join-Path $Www "fonts/Noto Sans Regular")
)
$Dirs | ForEach-Object { New-Item -ItemType Directory -Force -Path $_ | Out-Null }
function Download-Asset([string]$Url,[string]$Destination){ Write-Host "Downloading $Url"; Invoke-WebRequest -Uri $Url -OutFile $Destination }
Download-Asset "https://unpkg.com/maplibre-gl@$MapLibreVersion/dist/maplibre-gl.js" (Join-Path $Www "lib/maplibre-gl/dist/maplibre-gl.js")
Download-Asset "https://unpkg.com/maplibre-gl@$MapLibreVersion/dist/maplibre-gl.css" (Join-Path $Www "lib/maplibre-gl/dist/maplibre-gl.css")
Download-Asset "https://unpkg.com/pmtiles@$PmtilesVersion/dist/pmtiles.js" (Join-Path $Www "lib/pmtiles/dist/pmtiles.js")
Download-Asset "https://unpkg.com/leaflet@$LeafletVersion/dist/leaflet.js" (Join-Path $Www "lib/leaflet/leaflet.js")
Download-Asset "https://unpkg.com/leaflet@$LeafletVersion/dist/leaflet.css" (Join-Path $Www "lib/leaflet/leaflet.css")
Download-Asset "https://unpkg.com/leaflet@$LeafletVersion/dist/images/marker-icon.png" (Join-Path $Www "lib/leaflet/images/marker-icon.png")
Download-Asset "https://unpkg.com/leaflet@$LeafletVersion/dist/images/marker-icon-2x.png" (Join-Path $Www "lib/leaflet/images/marker-icon-2x.png")
Download-Asset "https://unpkg.com/leaflet@$LeafletVersion/dist/images/marker-shadow.png" (Join-Path $Www "lib/leaflet/images/marker-shadow.png")
Download-Asset "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/$SignalRVersion/signalr.min.js" (Join-Path $Www "lib/signalr/signalr.min.js")
$FontBase = "https://protomaps.github.io/basemaps-assets/fonts/Noto%20Sans%20Regular"
foreach ($Range in @("0-255","256-511","512-767","768-1023","1024-1279")) { Download-Asset "$FontBase/$Range.pbf" (Join-Path $Www "fonts/Noto Sans Regular/$Range.pbf") }
Write-Host "Local frontend map assets prepared."
