#!/bin/sh
set -e

OSRM_ALGORITHM="${OSRM_ALGORITHM:-mld}"
OSRM_DATA_DIR="${OSRM_DATA_DIR:-/data}"
OSRM_DATASET="${OSRM_DATASET:-europe-truck}"
OSRM_PORT="${OSRM_PORT:-5090}"

has_bundle() {
	prefix="$1"

	for suffix in \
		.osrm \
		.osrm.cells \
		.osrm.cell_metrics \
		.osrm.cnbg \
		.osrm.cnbg_to_ebg \
		.osrm.datasource_names \
		.osrm.ebg \
		.osrm.ebg_nodes \
		.osrm.edges \
		.osrm.enw \
		.osrm.fileIndex \
		.osrm.geometry \
		.osrm.icd \
		.osrm.maneuver_overrides \
		.osrm.mldgr \
		.osrm.names \
		.osrm.nbg_nodes \
		.osrm.partition \
		.osrm.properties \
		.osrm.ramIndex \
		.osrm.restrictions \
		.osrm.timestamp \
		.osrm.tld \
		.osrm.tls \
		.osrm.turn_duration_penalties \
		.osrm.turn_penalties_index \
		.osrm.turn_weight_penalties
	do
		if [ ! -f "$prefix$suffix" ]; then
			return 1
		fi
	done

	return 0
}

preferred_prefix="$OSRM_DATA_DIR/$OSRM_DATASET"
fallback_prefix="$OSRM_DATA_DIR/serbia-latest"

if has_bundle "$preferred_prefix"; then
	dataset_prefix="$preferred_prefix"
	echo "[info] Starting OSRM on port $OSRM_PORT with dataset: $dataset_prefix"
elif has_bundle "$fallback_prefix"; then
	dataset_prefix="$fallback_prefix"
	echo "[warn] Preferred dataset '$preferred_prefix' is incomplete. Falling back to '$dataset_prefix' on port $OSRM_PORT."
else
	echo "[error] No complete OSRM dataset bundle found under $OSRM_DATA_DIR." >&2
	echo "[error] Build europe-truck with scripts/build-europe-osrm-truck.ps1 or provide a valid fallback bundle." >&2
	exit 1
fi

exec osrm-routed --algorithm "$OSRM_ALGORITHM" --port "$OSRM_PORT" "$dataset_prefix.osrm"
