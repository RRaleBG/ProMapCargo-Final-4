#!/usr/bin/env bash
set -euo pipefail

# Pokreće ProMapCargo Development Environment.
# Ova skript:
# 1. Importa OSM podatke u PostgreSQL (ako nije već importano)
# 2. Pokreće ProMapCargo API na localhost:8090

OSM_FILE="${PROMAP_OSM_FILE:-osm/serbia-latest.osm.pbf}"
GRAPH_VERSION="${PROMAP_GRAPH_VERSION:-0}"
API_PORT="${PROMAP_API_PORT:-8090}"
OSRM_PORT="${PROMAP_OSRM_PORT:-5090}"
SKIP_IMPORT="${PROMAP_SKIP_IMPORT:-false}"
SKIP_OSRM="${PROMAP_SKIP_OSRM:-false}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT="$ROOT_DIR/ProMapCargo.Api.csproj"
IMPORTER_PROJECT="$ROOT_DIR/Importer/ProMapCargo.OsmImporter.csproj"
OSRM_SCRIPT="$ROOT_DIR/scripts/start-osrm.sh"
OSM_FILE_PATH="$ROOT_DIR/$OSM_FILE"
OSRM_PID=""

echo "🚀 ProMapCargo Development Environment"
echo ""
echo "Konfiguracija:"
echo "  OSM File: $OSM_FILE"
echo "  API Port: $API_PORT"
echo "  OSRM Port: $OSRM_PORT"
echo "  Skip Import: $SKIP_IMPORT"
echo "  Skip OSRM: $SKIP_OSRM"
echo ""

# Provjeri je li OSM datoteka dostupna
if [ ! -f "$OSM_FILE_PATH" ]; then
	echo "❌ OSM datoteka nije pronađena: $OSM_FILE_PATH"
	echo "   Dostupne datoteke:"
	ls -1 "$ROOT_DIR/osm"/*.pbf 2>/dev/null || echo "   (nema .pbf datoteka)"
	exit 1
fi

echo "✓ OSM datoteka pronađena"

# Ako GraphVersion nije zadana, koristi trenutni timestamp
if [ "$GRAPH_VERSION" = "0" ]; then
	GRAPH_VERSION=$(date +%s)
	echo "✓ Graph Version: $GRAPH_VERSION (auto-generated)"
fi

# ============================================================
# IMPORT OSM PODATAKA
# ============================================================

if [ "$SKIP_IMPORT" != "true" ]; then
	echo ""
	echo "📥 Importanje OSM podataka..."
	echo "   Projekat: $IMPORTER_PROJECT"
	echo "   OSM File: $OSM_FILE_PATH"
	echo "   Graph Version: $GRAPH_VERSION"
	echo ""

	cd "$ROOT_DIR"
	if dotnet run --project "$IMPORTER_PROJECT" -- "$OSM_FILE_PATH" "$GRAPH_VERSION"; then
		echo ""
		echo "✅ OSM import uspješan!"
	else
		echo ""
		echo "❌ OSM import neuspješan!"
		exit 1
	fi
else
	echo ""
	echo "⏭️  Preskakanje import-a (SKIP_IMPORT je postavljen)"
fi

# ============================================================
# POKRETANJE OSRM
# ============================================================

cleanup() {
	if [ -n "$OSRM_PID" ] && kill -0 "$OSRM_PID" 2>/dev/null; then
		kill "$OSRM_PID" 2>/dev/null || true
		wait "$OSRM_PID" 2>/dev/null || true
	fi
}

trap cleanup EXIT

if [ "$SKIP_OSRM" != "true" ]; then
	echo ""
	if ! command -v osrm-routed >/dev/null 2>&1; then
		echo "⚠️  osrm-routed nije pronađen u PATH-u. Port $OSRM_PORT neće biti pokrenut lokalno."
	elif [ ! -f "$OSRM_SCRIPT" ]; then
		echo "⚠️  OSRM skripta nije pronađena: $OSRM_SCRIPT"
	else
		echo "🗺️  Pokretanje lokalnog OSRM servisa na localhost:$OSRM_PORT..."
		OSRM_DATA_DIR="$ROOT_DIR/osm" OSRM_PORT="$OSRM_PORT" "$OSRM_SCRIPT" &
		OSRM_PID=$!
		sleep 2
		if ! kill -0 "$OSRM_PID" 2>/dev/null; then
			echo "⚠️  OSRM proces je odmah završen. Provjeri konzolni izlaz i dataset u osm/."
			OSRM_PID=""
		fi
	fi
else
	echo ""
	echo "⏭️  Preskakanje OSRM pokretanja (SKIP_OSRM je postavljen)"
fi

# ============================================================
# POKRETANJE API
# ============================================================

echo ""
echo "🏗️  Pokretanje ProMapCargo API na localhost:$API_PORT..."
echo ""

cd "$ROOT_DIR"
dotnet run --project "$API_PROJECT" --urls "http://localhost:$API_PORT" --configuration Debug

echo ""
echo "⏹️  Servis je zaustavljen."
