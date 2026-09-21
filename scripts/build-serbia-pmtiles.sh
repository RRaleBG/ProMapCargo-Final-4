#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OSM_FILE="${PROMAP_OSM_FILE:-serbia-latest.osm.pbf}"
OUTPUT_FILE="${PROMAP_MAP_FILE:-serbia.pmtiles}"
MEMORY="${PROMAP_PLANETILER_MEMORY:-4g}"
INPUT="/data/${OSM_FILE}"
OUTPUT="/output/${OUTPUT_FILE}"
[[ -f "$ROOT_DIR/osm/$OSM_FILE" ]] || { echo "ERROR: OSM file not found: $ROOT_DIR/osm/$OSM_FILE"; exit 1; }
mkdir -p "$ROOT_DIR/wwwroot/maps"
docker run --rm -e JAVA_TOOL_OPTIONS="-Xmx${MEMORY}" -v "$ROOT_DIR/osm:/data:ro" -v "$ROOT_DIR/wwwroot/maps:/output" ghcr.io/onthegomap/planetiler:latest --osm-path="$INPUT" --output="$OUTPUT" --download --force
echo "DONE: $ROOT_DIR/wwwroot/maps/$OUTPUT_FILE"
