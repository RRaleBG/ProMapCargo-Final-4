#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WWW_ROOT="$ROOT_DIR/wwwroot"
MAPLIBRE_VERSION="${MAPLIBRE_VERSION:-6.10.0}"
PMTILES_VERSION="${PMTILES_VERSION:-4.5.0}"
LEAFLET_VERSION="${LEAFLET_VERSION:-1.9.4}"
SIGNALR_VERSION="${SIGNALR_VERSION:-8.0.7}"
mkdir -p "$WWW_ROOT/lib/maplibre-gl/dist" "$WWW_ROOT/lib/pmtiles/dist" "$WWW_ROOT/lib/leaflet" "$WWW_ROOT/lib/leaflet/images" "$WWW_ROOT/lib/signalr" "$WWW_ROOT/fonts/Noto Sans Regular"
download(){ curl --fail --location --silent --show-error --retry 3 --retry-delay 2 "$1" -o "$2"; }
download "https://unpkg.com/maplibre-gl@${MAPLIBRE_VERSION}/dist/maplibre-gl.js" "$WWW_ROOT/lib/maplibre-gl/dist/maplibre-gl.js"
download "https://unpkg.com/maplibre-gl@${MAPLIBRE_VERSION}/dist/maplibre-gl.css" "$WWW_ROOT/lib/maplibre-gl/dist/maplibre-gl.css"
download "https://unpkg.com/pmtiles@${PMTILES_VERSION}/dist/pmtiles.js" "$WWW_ROOT/lib/pmtiles/dist/pmtiles.js"
download "https://unpkg.com/leaflet@${LEAFLET_VERSION}/dist/leaflet.js" "$WWW_ROOT/lib/leaflet/leaflet.js"
download "https://unpkg.com/leaflet@${LEAFLET_VERSION}/dist/leaflet.css" "$WWW_ROOT/lib/leaflet/leaflet.css"
download "https://unpkg.com/leaflet@${LEAFLET_VERSION}/dist/images/marker-icon.png" "$WWW_ROOT/lib/leaflet/images/marker-icon.png"
download "https://unpkg.com/leaflet@${LEAFLET_VERSION}/dist/images/marker-icon-2x.png" "$WWW_ROOT/lib/leaflet/images/marker-icon-2x.png"
download "https://unpkg.com/leaflet@${LEAFLET_VERSION}/dist/images/marker-shadow.png" "$WWW_ROOT/lib/leaflet/images/marker-shadow.png"
download "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/${SIGNALR_VERSION}/signalr.min.js" "$WWW_ROOT/lib/signalr/signalr.min.js"
FONT_BASE="https://protomaps.github.io/basemaps-assets/fonts/Noto%20Sans%20Regular"
for RANGE in 0-255 256-511 512-767 768-1023 1024-1279; do download "${FONT_BASE}/${RANGE}.pbf" "$WWW_ROOT/fonts/Noto Sans Regular/${RANGE}.pbf"; done
echo "Local frontend map assets prepared."
