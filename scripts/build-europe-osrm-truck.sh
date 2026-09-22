#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OSM_FILE="${PROMAP_OSM_FILE:-europe-latest.osm.pbf}"
PROFILE_FILE="${PROMAP_OSRM_PROFILE:-truck.lua}"
OUTPUT_PREFIX="${PROMAP_OSRM_OUTPUT_PREFIX:-europe-truck}"

[[ -f "$ROOT_DIR/osm/$OSM_FILE" ]] || { echo "ERROR: OSM file not found: $ROOT_DIR/osm/$OSM_FILE"; exit 1; }
[[ -f "$ROOT_DIR/profiles/osrm/$PROFILE_FILE" ]] || { echo "ERROR: OSRM profile not found: $ROOT_DIR/profiles/osrm/$PROFILE_FILE"; exit 1; }

echo "[1/3] osrm-extract for Europe truck profile"
docker run --rm -v "$ROOT_DIR/osm:/data" -v "$ROOT_DIR/profiles/osrm:/profiles:ro" osrm/osrm-backend:latest osrm-extract -p "/profiles/$PROFILE_FILE" "/data/$OSM_FILE"

SOURCE_PREFIX="${OSM_FILE%.osm.pbf}"
SOURCE_PREFIX="${SOURCE_PREFIX%.pbf}"
if [[ "$OUTPUT_PREFIX" != "$SOURCE_PREFIX" ]]; then
  shopt -s nullglob
  for source in "$ROOT_DIR"/osm/"$SOURCE_PREFIX".osrm*; do
    target="${source/$SOURCE_PREFIX/$OUTPUT_PREFIX}"
    cp -f "$source" "$target"
  done
fi

echo "[2/3] osrm-partition"
docker run --rm -v "$ROOT_DIR/osm:/data" osrm/osrm-backend:latest osrm-partition "/data/$OUTPUT_PREFIX.osrm"

echo "[3/3] osrm-customize"
docker run --rm -v "$ROOT_DIR/osm:/data" osrm/osrm-backend:latest osrm-customize "/data/$OUTPUT_PREFIX.osrm"

echo "DONE: $ROOT_DIR/osm/$OUTPUT_PREFIX.osrm*"
