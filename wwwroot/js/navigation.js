"use strict";

window.ProMap = window.ProMap || {};

(() => {
    if (window.__promapNavigationInitialized) {
        return;
    }

    const root = document.querySelector('[data-module="navigation"]');

    if (!root) {
        return;
    }

    window.__promapNavigationInitialized = true;

    const $ = (id) => document.getElementById(id);

    // ============================================================
    // STATE
    // ============================================================

    const state = {
        map: null,

        start: null,
        destination: null,

        startMarker: null,
        destinationMarker: null,
        gpsMarker: null,

        routeLayers: [],
        routeResponse: null,
        lastRoutingErrorDiagnostics: null,
        selectedRouteIndex: 0,

        routeCoordinates: [],
        routeCumulativeDistances: [],

        maneuvers: [],
        maneuverIndex: 0,
        routeProgressMeters: 0,

        liveFollow: true,
        profile: "truck",
        picking: null,
        lastHeading: null,

        geocodeTimers: {
            start: null,
            end: null,
        },

        routing: false,
        live: false,
        lastRerouteAt: 0,

        embeddedMode:
            new URLSearchParams(window.location.search).get("embedded") === "1" ||
            new URLSearchParams(window.location.search).get("mobile") === "1", 

        pendingMobilePayload: null,

        localMap: {
            host: null,
            map: null,
            layer: null,
            loading: null,
            ready: false,
            failed: false,
            visible: false,
            lastError: null,
            archive: null,
            syncFrame: 0,
            lastSyncZoom: null,
            lastSyncCenter: null,
            enhancements: null,
        },
    };

    // ============================================================
    // CONSTANTS
    // ============================================================

    const DEFAULTS = {
        start: {
            latitude: 44.8178,
            longitude: 20.4573,
            label: "Beograd, Srbija",
        },

        destination: {
            latitude: 45.2551,
            longitude: 19.8335,
            label: "Novi Sad, Srbija",
        },
    };

    const PRESETS = {
        "40t": {
            weight: 40,
            height: 4.0,
            width: 2.55,
            length: 16.5,
            axleLoad: 10,
            axles: 5,
            maxSpeed: 90,
        },

        "12t": {
            weight: 12,
            height: 3.8,
            width: 2.55,
            length: 12.0,
            axleLoad: 8,
            axles: 3,
            maxSpeed: 90,
        },

        "7.5t": {
            weight: 7.5,
            height: 3.5,
            width: 2.5,
            length: 9.0,
            axleLoad: 7,
            axles: 2,
            maxSpeed: 90,
        },

        "3.5t": {
            weight: 3.5,
            height: 3.2,
            width: 2.2,
            length: 7.0,
            axleLoad: 4,
            axles: 2,
            maxSpeed: 90,
        },
    };

    const LOCAL_MAPLIBRE_JS = "/lib/maplibre-gl/dist/maplibre-gl.js";

    const LOCAL_MAPLIBRE_CSS = "/lib/maplibre-gl/dist/maplibre-gl.css";

    const LOCAL_PMTILES_JS = "/lib/pmtiles/dist/pmtiles.js";

    const LOCAL_MAP_STYLE = "/styles/promap-dark3.json?v=20260924-promap-dark-v12";

    const LOCAL_MAPLIBRE_CSS_ID = "promap-local-maplibre-css";

    const PROMAP_PMTILES_ENDPOINT = "/maps/europe.pmtiles";

    function toBoolean(value, fallback = false) {
        if (typeof value === "boolean") {
            return value;
        }

        if (typeof value === "string") {
            const normalized = value.trim().toLowerCase();

            if (normalized === "true" || normalized === "1") {
                return true;
            }

            if (normalized === "false" || normalized === "0") {
                return false;
            }
        }

        return fallback;
    }

    function applyLayerStates(layers) {
        if (!layers || typeof layers !== "object") {
            return;
        }

        const mapping = {
            route: "route",
            restrictions: "restrictions",
            fleet: "fleet",
            poi: "poi",
        };

        for (const [source, group] of Object.entries(mapping)) {
            if (!(source in layers)) {
                continue;
            }

            const visible = toBoolean(layers[source], group === "route" || group === "restrictions");

            if (state.localMap?.enhancements?.setGroupVisible) {
                state.localMap.enhancements.setGroupVisible(group, visible);
            }

            const checkbox = document.querySelector(`[data-promap-layer="${group}"]`);
            if (checkbox && "checked" in checkbox) {
                checkbox.checked = visible;
            }
        }
    }

    function buildMobileRouteResponse(payload) {
        const routePoints = Array.isArray(payload?.route)
            ? payload.route.map(normalizePoint).filter(Boolean)
            : [];

        if (routePoints.length < 2) {
            return null;
        }

        const coordinates = routePoints.map((point) => [point.longitude, point.latitude]);

        const maneuvers = Array.isArray(payload?.maneuvers)
            ? payload.maneuvers
                .map((item) => {
                    const point = normalizePoint(item);

                    if (!point) {
                        return null;
                    }

                    return {
                        type: item.type || "continue",
                        modifier: item.modifier || null,
                        instruction: item.instruction || "Nastavi pravo",
                        distanceMeters: Number(item.distanceMeters) || 0,
                        latitude: point.latitude,
                        longitude: point.longitude,
                    };
                })
                .filter(Boolean)
            : [];

        return {
            engine: "PostGIS",
            usedFallback: false,
            selectedRouteIndex: 0,
            maneuvers,
            violations: [],
            routes: [
                {
                    id: "mobile-live",
                    distance: Number(payload?.distanceMeters) || 0,
                    duration: Number(payload?.durationSeconds) || 0,
                    geometry: {
                        type: "LineString",
                        coordinates,
                    },
                    analysis: {
                        restricted: Number(payload?.restrictionCount) > 0,
                        violations: [],
                    },
                },
            ],
        };
    }

    function applyPendingMobilePayload() {
        const payload = state.pendingMobilePayload;

        if (!payload || typeof payload !== "object") {
            return false;
        }

        const start = normalizePoint(payload.start);
        const destination = normalizePoint(payload.destination);
        const current = normalizePoint(payload.current);

        if (start) {
            setStart(start);
        }

        if (destination) {
            setDestination(destination);
        }

        if (current && state.map) {
            updateGpsMarker({
                latitude: current.latitude,
                longitude: current.longitude,
                accuracy: Number(payload?.current?.accuracy) || undefined,
                speed: Number(payload?.current?.speed) || undefined,
                timestamp: Date.now(),
            });
        }

        applyLayerStates(payload.layers);

        if (state.localMap?.enhancements?.setRoute) {
            const response = buildMobileRouteResponse(payload);

            if (response) {
                renderRouteResponse(response);
                selectRoute(0, true);
            }
        }

        state.pendingMobilePayload = null;

        return true;
    }

    function ensureMobileBridge() {
        window.proMapMobile = window.proMapMobile || {};

        window.proMapMobile.applyState = (payload) => {
            state.pendingMobilePayload = payload || {};

            if (state.localMap?.enhancements?.setRoute) {
                applyPendingMobilePayload();
            }

            return true;
        };
    }

    // ============================================================
    // GENERIC HELPERS
    // ============================================================

    function setText(id, value) {
        const element = $(id);

        if (element) {
            element.textContent = value ?? "";
        }
    }

    function setHidden(id, hidden) {
        const element = $(id);

        if (element) {
            element.hidden = Boolean(hidden);
        }
    }

    function numberValue(id, fallback) {
        const element = $(id);

        if (!element) {
            return fallback;
        }

        const value = Number.parseFloat(element.value);

        return Number.isFinite(value) ? value : fallback;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function showError(message) {
        const element = $("navError");

        if (!element) {
            return;
        }

        element.textContent = message || "";
        element.hidden = !message;
    }

    function buildRoutingErrorMessage(error) {
        const payload = error?.payload || {};
        const postGisReason = payload?.postGisFailureReason || payload?.postGisDiagnostics?.failureReason;
        const details = payload?.details || error?.message;

        if (postGisReason && details) {
            return `${postGisReason} OSRM fallback nije dostupan: ${details}`;
        }

        if (postGisReason) {
            return postGisReason;
        }

        if (payload?.message && details && payload.message !== details) {
            return `${payload.message} ${details}`;
        }

        return payload?.message || details || "Došlo je do greške prilikom računanja rute.";
    }

    function setGpsStatus(text, kind = "") {
        const element = $("gpsStatus");

        if (!element) {
            return;
        }

        element.textContent = text;
        element.classList.remove("ready", "warning", "danger");

        if (kind) {
            element.classList.add(kind);
        }

        setText("navKpiGps", text || "Standby");
    }

    function setEngine(engine, usedFallback = false) {
        const normalized = String(engine || "PostGIS");

        const summary = usedFallback ? `${normalized} · FALLBACK` : normalized;

        setText("engineHeader", normalized.toUpperCase());
        setText("summaryEngine", summary);
        setText("diagnosticEngine", normalized);
        setText("diagnosticFallback", usedFallback ? "DA" : "NE");
        setText("navKpiEngine", usedFallback ? "Fallback" : normalized);

        const badge = $("engineBadge");

        if (badge) {
            badge.classList.remove("error");
            badge.classList.add("ready");

            badge.innerHTML = `<span></span > ${escapeHtml(
                summary.toUpperCase(),
            )} `;
        }
    }

    function setRoutingUi(routing) {
        state.routing = Boolean(routing);

        const button = $("calcRoute");

        if (!button) {
            return;
        }

        button.disabled = state.routing;

        const label = button.querySelector("strong");

        if (label) {
            label.textContent = state.routing
                ? "Računam rutu…"
                : state.profile === "truck"
                    ? "Izračunaj truck rutu"
                    : "Izračunaj auto rutu";
        }
    }

    // ============================================================
    // FORMATTING
    // ============================================================

    function formatDistance(meters) {
        const value = Number(meters);

        if (!Number.isFinite(value)) {
            return "—";
        }

        if (
            window.ProMap.Maneuvers &&
            typeof window.ProMap.Maneuvers.formatDistance === "function"
        ) {
            return window.ProMap.Maneuvers.formatDistance(value);
        }

        if (value >= 1000) {
            return `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)} km`;
        }

        return `${Math.round(value)} m`;
    }

    function formatDuration(seconds) {
        const value = Number(seconds);

        if (!Number.isFinite(value)) {
            return "—";
        }

        if (
            window.ProMap.Maneuvers &&
            typeof window.ProMap.Maneuvers.formatDuration === "function"
        ) {
            return window.ProMap.Maneuvers.formatDuration(value);
        }

        const minutes = Math.max(0, Math.round(value / 60));

        const hours = Math.floor(minutes / 60);

        const remainder = minutes % 60;

        return hours > 0
            ? `${hours} h ${String(remainder).padStart(2, "0")} min`
            : `${minutes} min`;
    }

    function formatEta(value, durationSeconds = null) {
        let date = null;

        if (value) {
            date = new Date(value);
        } else if (Number.isFinite(Number(durationSeconds))) {
            date = new Date(Date.now() + Number(durationSeconds) * 1000);
        }

        if (!date || Number.isNaN(date.getTime())) {
            return "—";
        }

        return new Intl.DateTimeFormat("sr-RS", {
            hour: "2-digit",
            minute: "2-digit",
        }).format(date);
    }

    // ============================================================
    // GEO
    // ============================================================

    function normalizePoint(point) {
        if (!point) {
            return null;
        }

        const latitude = Number(point.latitude ?? point.lat ?? point.Lat);

        const longitude = Number(point.longitude ?? point.lon ?? point.Lon);

        if (
            !Number.isFinite(latitude) ||
            !Number.isFinite(longitude) ||
            latitude < -90 ||
            latitude > 90 ||
            longitude < -180 ||
            longitude > 180
        ) {
            return null;
        }

        return {
            latitude,
            longitude,
            label:
                point.label ??
                point.displayName ??
                point.display_name ??
                point.name ??
                "",
        };
    }

    function parseCoordinateText(value) {
        const text = String(value || "").trim();

        const match = text.match(
            /^\s*(-?\d+(?:[.,]\d+)?)\s*[,;]\s*(-?\d+(?:[.,]\d+)?)\s*$/,
        );

        if (!match) {
            return null;
        }

        return normalizePoint({
            latitude: Number(match[1].replace(",", ".")),

            longitude: Number(match[2].replace(",", ".")),
        });
    }

    function geometryToLatLngs(geometry) {
        if (!geometry) {
            console.debug("[GEOMETRY] No geometry provided");
            return [];
        }

        let value = geometry;

        if (typeof value === "string") {
            try {
                value = JSON.parse(value);
                console.debug("[GEOMETRY] Parsed geometry from string");
            } catch {
                console.error("[GEOMETRY] Failed to parse geometry string");
                return [];
            }
        }

        if (Array.isArray(value)) {
            const result = value
                .filter((point) => Array.isArray(point) && point.length >= 2)
                .map((point) => [Number(point[1]), Number(point[0])])
                .filter(
                    (point) => Number.isFinite(point[0]) && Number.isFinite(point[1]),
                );
            console.debug(`[GEOMETRY] Array geometry: converted ${value.length} → ${result.length} points`);
            if (result.length > 0) {
                console.debug(`[GEOMETRY] First point (array): [${result[0][0].toFixed(6)}, ${result[0][1].toFixed(6)}]`);
                console.debug(`[GEOMETRY] Last point (array): [${result[result.length-1][0].toFixed(6)}, ${result[result.length-1][1].toFixed(6)}]`);
            }
            return result;
        }

        if (value?.type === "Feature") {
            value = value.geometry;
            console.debug("[GEOMETRY] Extracted geometry from Feature");
        }

        if (!value || !value.type || !Array.isArray(value.coordinates)) {
            console.warn("[GEOMETRY] Invalid geometry: missing type or coordinates");
            return [];
        }

        if (value.type === "LineString") {
            const result = value.coordinates
                .filter((point) => Array.isArray(point) && point.length >= 2)
                .map((point) => [Number(point[1]), Number(point[0])])
                .filter(
                    (point) => Number.isFinite(point[0]) && Number.isFinite(point[1]),
                );
            console.debug(`[GEOMETRY] LineString: converted ${value.coordinates.length} → ${result.length} points`);
            if (result.length > 0) {
                console.debug(`[GEOMETRY] First point (LineString): [${result[0][0].toFixed(6)}, ${result[0][1].toFixed(6)}]`);
                console.debug(`[GEOMETRY] Last point (LineString): [${result[result.length-1][0].toFixed(6)}, ${result[result.length-1][1].toFixed(6)}]`);
            }
            if (value.coordinates.length > 0) {
                console.debug(`[GEOMETRY] Original [lon,lat]: [${value.coordinates[0][0]}, ${value.coordinates[0][1]}]`);
                console.debug(`[GEOMETRY] Converted [lat,lon]: [${result[0][0]}, ${result[0][1]}]`);
            }
            return result;
        }

        if (value.type === "MultiLineString") {
            const result = value.coordinates
                .flatMap((line) =>
                    Array.isArray(line)
                        ? line
                            .filter((point) => Array.isArray(point) && point.length >= 2)
                            .map((point) => [Number(point[1]), Number(point[0])])
                        : [],
                )
                .filter(
                    (point) => Number.isFinite(point[0]) && Number.isFinite(point[1]),
                );
            console.debug(`[GEOMETRY] MultiLineString: converted to ${result.length} points`);
            return result;
        }

        console.warn(`[GEOMETRY] Unsupported geometry type: ${value.type}`);
        return [];
    }

    function haversineMeters(aLat, aLon, bLat, bLon) {
        const earth = 6371000;

        const dLat = ((bLat - aLat) * Math.PI) / 180;

        const dLon = ((bLon - aLon) * Math.PI) / 180;

        const lat1 = (aLat * Math.PI) / 180;

        const lat2 = (bLat * Math.PI) / 180;

        const a =
            Math.sin(dLat / 2) ** 2 +
            Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLon / 2) ** 2;

        return 2 * earth * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    function distanceToRouteMeters(latitude, longitude) {
        if (state.routeCoordinates.length === 0) {
            return null;
        }

        let best = Infinity;

        for (const point of state.routeCoordinates) {
            best = Math.min(
                best,
                haversineMeters(latitude, longitude, point[0], point[1]),
            );
        }

        return Number.isFinite(best) ? best : null;
    }

    function distanceToManeuverMeters(position, maneuver) {
        if (!position || !maneuver) {
            return null;
        }

        const latitude = Number(maneuver.latitude);

        const longitude = Number(maneuver.longitude);

        if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
            return null;
        }

        return haversineMeters(
            Number(position.latitude),
            Number(position.longitude),
            latitude,
            longitude,
        );
    }

    function getCurrentGpsPosition() {
        const position = window.ProMap.Gps?.getPosition?.();

        if (!position) {
            return null;
        }

        const latitude = Number(position.latitude ?? position.coords?.latitude);

        const longitude = Number(position.longitude ?? position.coords?.longitude);

        if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
            return null;
        }

        return {
            latitude,
            longitude,

            accuracy: Number(position.accuracy ?? position.coords?.accuracy),

            heading: Number(position.heading ?? position.coords?.heading),

            speed: Number(position.speed ?? position.coords?.speed),

            timestamp: position.timestamp ?? position.coords?.timestamp,
        };
    }

    // ============================================================
    // LOCAL MAP ASSETS
    // ============================================================

    function loadCssOnce(href, id) {
        return new Promise((resolve, reject) => {
            const existing = document.getElementById(id);

            if (existing) {
                resolve();
                return;
            }

            const link = document.createElement("link");

            link.id = id;
            link.rel = "stylesheet";
            link.href = href;

            link.onload = () => resolve();

            link.onerror = () =>
                reject(new Error(`Lokalni CSS nije moguće učitati: ${href} `));

            document.head.appendChild(link);
        });
    }

    function loadScriptOnce(src, globalName) {
        return new Promise((resolve, reject) => {
            if (globalName && window[globalName]) {
                resolve(window[globalName]);
                return;
            }

            const existing = document.querySelector(`script[src = "${src}"]`);

            if (existing) {
                existing.addEventListener(
                    "load",
                    () => resolve(globalName ? window[globalName] : undefined),
                    { once: true },
                );

                existing.addEventListener(
                    "error",
                    () =>
                        reject(
                            new Error(`Lokalni JavaScript nije moguće učitati: ${src} `),
                        ),
                    { once: true },
                );

                return;
            }

            const script = document.createElement("script");

            script.src = src;
            script.async = false;

            script.onload = () =>
                resolve(globalName ? window[globalName] : undefined);

            script.onerror = () =>
                reject(new Error(`Lokalni JavaScript nije moguće učitati: ${src} `));

            document.head.appendChild(script);
        });
    }

    async function ensureLocalMapLibraries() {
        await loadCssOnce(LOCAL_MAPLIBRE_CSS, LOCAL_MAPLIBRE_CSS_ID);

        const maplibregl = await loadScriptOnce(LOCAL_MAPLIBRE_JS, "maplibregl");

        const pmtiles = await loadScriptOnce(LOCAL_PMTILES_JS, "pmtiles");

        if (!maplibregl) {
            throw new Error("MapLibre GL JS nije dostupan.");
        }

        if (!pmtiles) {
            throw new Error("PMTiles JS nije dostupan.");
        }

        return {
            maplibregl,
            pmtiles,
        };
    }

    async function loadMapLibre() {
        if (window.__promapMapLibreInstance) {
            return window.__promapMapLibreInstance;
        }

        const { maplibregl } = await ensureLocalMapLibraries();

        if (
            !maplibregl ||
            typeof maplibregl.Map !== "function" ||
            typeof maplibregl.addProtocol !== "function"
        ) {
            throw new Error("Lokalni MapLibre paket nije validan.");
        }

        window.__promapMapLibreInstance = maplibregl;

        console.info("[ProMap Navigation] Local MapLibre loaded:", {
            version: maplibregl.version ?? "unknown",

            hasMap: typeof maplibregl.Map === "function",

            hasAddProtocol: typeof maplibregl.addProtocol === "function",
        });

        return maplibregl;
    }

    function loadPmtiles() {
        if (window.pmtiles) {
            return window.pmtiles;
        }

        throw new Error("PMTiles JS nije učitan iz lokalnog paketa.");
    }

    async function ensurePmtilesProtocol() {
        if (window.__promapPmtilesProtocolRegistered) {
            return;
        }

        const maplibregl = await loadMapLibre();

        if (!window.pmtiles || typeof window.pmtiles.Protocol !== "function") {
            throw new Error("Lokalni PMTiles nije učitan.");
        }

        const protocol = new window.pmtiles.Protocol();
        maplibregl.addProtocol("pmtiles", protocol.tile);
        window.__promapPmtilesProtocol = protocol;
        window.__promapPmtilesProtocolRegistered = true;
        console.info("[ProMap Navigation] PMTiles protocol registered.");
    }

    // ============================================================
    // LOCAL MAP HOST
    // ============================================================

    function prepareLocalMapHost(mapElement) {
        if (state.localMap.host) {
            return state.localMap.host;
        }

        const host = document.createElement("div");
        host.id = "promap-local-osm-map";
        host.setAttribute("aria-hidden", "true");
        Object.assign(host.style, {
            position: "absolute",

            inset: "0",
            width: "100%",
            height: "100%",
            zIndex: "0",
            pointerEvents: "none",
            visibility: "hidden",
            overflow: "hidden",
            borderRadius: "inherit",
            background: "#031712",
        });

        mapElement.appendChild(host);
        state.localMap.host = host;
        return host;
    }

    function syncLocalMapBackground() {
        const localMap = state.localMap.map;
        const leafletMap = state.map;

        if (!localMap || !leafletMap) {
            return;
        }

        const center = leafletMap.getCenter();
        const zoom = leafletMap.getZoom();
        const roundedZoom = Number(zoom.toFixed(2));
        const roundedLat = Number(center.lat.toFixed(6));
        const roundedLng = Number(center.lng.toFixed(6));

        if (
            state.localMap.lastSyncZoom === roundedZoom &&
            state.localMap.lastSyncCenter?.lat === roundedLat &&
            state.localMap.lastSyncCenter?.lng === roundedLng
        ) {
            return;
        }

        try {
            localMap.jumpTo({
                center: [center.lng, center.lat],
                zoom,
                bearing: 0,
                pitch: 0,
            });

            state.localMap.lastSyncZoom = roundedZoom;
            state.localMap.lastSyncCenter = {
                lat: roundedLat,
                lng: roundedLng,
            };
        } catch {
            // Ignore transient lifecycle errors.
        }
    }

    function scheduleLocalMapBackgroundSync() {
        if (state.localMap.syncFrame) {
            return;
        }

        state.localMap.syncFrame = window.requestAnimationFrame(() => {
            state.localMap.syncFrame = 0;
            syncLocalMapBackground();
        });
    }

    function setLocalMapHostVisible(visible) {
        const host = state.localMap.host;

        if (!host) {
            return;
        }

        host.style.visibility = visible ? "visible" : "hidden";

        state.localMap.visible = Boolean(visible);

        if (state.map) {
            state.map.getContainer().style.background = visible
                ? "transparent"
                : "#031712";
        }
    }

    // ============================================================
    // LOCAL MAP STYLE
    // ============================================================

    let localMapStylePromise = null;

    async function loadLocalMapStyle(mapElement) {
        try {
            const maplibregl = await loadMapLibre();
            await ensurePmtilesProtocol();

            localMapStylePromise ??= fetch(LOCAL_MAP_STYLE, {
                headers: {
                    Accept: "application/json",
                },

                credentials: "same-origin",
                cache: "no-store",
            }).then(async (response) => {
                if (!response.ok) {
                    throw new Error(`promap-dark.json HTTP ${response.status}`);
                }

                return response.json();
            });

            const rawStyle = await localMapStylePromise;

            const style = JSON.parse(
                JSON.stringify(rawStyle).replaceAll(
                    "__PROMAP_PM_TILES_URL__",
                    PROMAP_PMTILES_ENDPOINT,
                ),
            );

            if (Number(style.version) !== 8) {
                throw new Error("promap-dark.json mora biti MapLibre Style v8.");
            }

            if (!Array.isArray(style.layers)) {
                throw new Error("promap-dark.json nema layers array.");
            }

            const map = new maplibregl.Map({
                container:
                    mapElement,
                style,
                center: [50.064671, 0.628492],
                zoom: 8,
                attributionControl: true,
                interactive: false,
                dragPan: false,
                scrollZoom: false,
                boxZoom: false,
                doubleClickZoom: false,
                dragRotate: false,
                keyboard: false,
                touchZoomRotate: false,
            });

            await new Promise((resolve, reject) => {
                let settled = false;

                const timer = window.setTimeout(() => {
                    if (settled) {
                        return;
                    }

                    settled = true;

                    reject(
                        new Error(
                            "Local OSM / PMTiles mapa nije učitana u roku od 30 sekundi.",
                        ),
                    );
                }, 30000);

                map.once("load", () => {
                    if (settled) {
                        return;
                    }

                    if (window.ProMapMapIcons) {
                        window.ProMapMapIcons.init(map);
                    }


                    settled = true;
                    window.clearTimeout(timer);
                    resolve();
                });

                map.once("error", (event) => {
                    console.error("[ProMap Navigation] MapLibre load error:", event?.error || event);
                    if (settled) {
                        return;
                    }

                    settled = true;
                    window.clearTimeout(timer);
                    reject(event?.error || new Error("MapLibre resource error."));
                });
            });

            console.info("[ProMap Navigation] LOCAL PMTILES MAP LOADED");

            return map;
        } catch (error) {
            console.error("[ProMap Navigation] Local OSM map FAILED:", error);

            throw error;
        }
    }

    async function activateLocalBaseMap(mapElement) {
        if (!state.map || !window.ProMap.MapLayers?.attach) {
            return;
        }

        const record = await window.ProMap.MapLayers.attach(state.map, {
            archiveUrl: PROMAP_PMTILES_ENDPOINT,
        });

        if (!record?.maplibreMap) {
            return;
        }

        state.localMap.host = record.host ?? prepareLocalMapHost(mapElement);
        state.localMap.map = record.maplibreMap;
        state.localMap.ready = true;
        state.localMap.failed = false;
        state.localMap.archive = record.archiveUrl ?? PROMAP_PMTILES_ENDPOINT;

        if (!state.localMap.enhancements && window.ProMap.MapEnhancements?.install) {
            state.localMap.enhancements = window.ProMap.MapEnhancements.install(record.maplibreMap, {
                visibleGroups: {
                    route: true,
                    restrictions: true,                  
                    fleet: false,
                    poi: true,
                    elevation: false,
                },
            });
            console.info("[ProMap Navigation] Map enhancements ready.");

            window.dispatchEvent(
                new CustomEvent("promap:map-enhancements-ready", {
                    detail: {
                        map: state.map,
                        enhancements: state.localMap.enhancements,
                    },
                })
            );
        }

        setLocalMapHostVisible(true);
        scheduleLocalMapBackgroundSync();

        window.setTimeout(() => {
            state.localMap.enhancements?.setGroupVisible?.("fleet", true);
            state.localMap.enhancements?.startFleetPolling?.({
                intervalMs: 30000,
                initialDelayMs: 1200,
            });
        }, 600);

        try {
            state.localMap.map?.resize();
            scheduleLocalMapBackgroundSync();
        } catch {
            // Ignore resize errors.
        }
    }

    function deactivateLocalBaseMap() {
        setLocalMapHostVisible(false);
    }

    // ============================================================
    // MAP
    // ============================================================

    // function initializeMap() {
    //     const mapElement = $("navMap");

    //     if (!mapElement) {
    //         console.error("[ProMap Navigation] #navMap nije pronađen u DOM-u.");

    //         return;
    //     }

    //     if (!window.L) {
    //         console.error("[ProMap Navigation] Leaflet nije učitan lokalno.");

    //         showError("Leaflet nije učitan. Proveri _Layout.cshtml.");

    //         return;
    //     }

    //     if (state.map) {
    //         state.map.invalidateSize();
    //         return;
    //     }

    //     state.map = L.map(mapElement, {
    //         zoomControl: true,
    //         preferCanvas: true,
    //         minZoom: 3,
    //         maxZoom: 22,
    //         zoomSnap: 0.25,
    //         zoomDelta: 0.5,
    //         wheelPxPerZoomLevel: 90,
    //         attributionControl: true,
    //         scrollWheelZoom: true,
    //         dragging: true,
    //         doubleClickZoom: true,
    //         boxZoom: true,
    //         keyboard: true,
    //         touchZoom: true,
    //         tapHold: true,
    //         zoomAnimation: false,
    //         fadeAnimation: false,
    //         markerZoomAnimation: false,
    //     }).setView(
    //         mapElement ? [50.2, 9.75] : [50, 12.5],
    //         mapElement ? 4.35 : 4.15,
    //     );

    //     L.control.scale({
    //         imperial: false,
    //         position: "bottomleft",
    //     }).addTo(state.map);

    //     mapElement.style.position = mapElement.style.position || "relative";
    //     mapElement.style.overflow = "hidden";
    //     mapElement.style.background = "#031712";

    //     prepareLocalMapHost(mapElement);

    //     // --------------------------------------------------------
    //     // MAP MOVEMENT
    //     // --------------------------------------------------------

    //     state.map.on("move", () => {
    //         if (state.localMap.visible) {
    //             scheduleLocalMapBackgroundSync();
    //         }
    //     });

    //     state.map.on("moveend zoomend viewreset resize", () => {
    //         if (state.localMap.visible) {
    //             scheduleLocalMapBackgroundSync();
    //         }
    //     });

    //     state.map.on("dragstart", () => {
    //         if (state.live) {
    //             state.liveFollow = false;
    //         }
    //     });

    //     // --------------------------------------------------------
    //     // MAP CLICK / POINT PICK
    //     // --------------------------------------------------------

    //     state.map.on("click", (event) => {
    //         if (!state.picking) {
    //             return;
    //         }

    //         const point = {
    //             latitude: event.latlng.lat,

    //             longitude: event.latlng.lng,

    //             label: `${event.latlng.lat.toFixed(6)}, ${event.latlng.lng.toFixed(6)}`,
    //         };

    //         if (state.picking === "start") {
    //             if ($("navStart")) {
    //                 $("navStart").value = point.label;
    //             }

    //             setStart(point);
    //         } else {
    //             if ($("navEnd")) {
    //                 $("navEnd").value = point.label;
    //             }

    //             setDestination(point);
    //         }

    //         state.picking = null;

    //         state.map.getContainer().style.cursor = "";

    //         showError("");
    //     });

    //     requestAnimationFrame(() => {
    //         state.map?.invalidateSize();
    //         state.localMap.map?.resize();
    //         scheduleLocalMapBackgroundSync();
    //     });

    //     window.setTimeout(() => {
    //         state.map?.invalidateSize();
    //         state.localMap.map?.resize();
    //         scheduleLocalMapBackgroundSync();
    //     }, 250);

    //     void activateLocalBaseMap(mapElement);
    // }


    // ============================================================
    // START / DESTINATION
    // ============================================================


    async function initializeMap() {
        const mapElement = $("navMap");

        if (!mapElement) {
            console.error(
                "[ProMap Navigation] #navMap nije pronađen u DOM-u.",
            );

            return false;
        }

        if (!window.ProMap?.MapLayers?.attach) {
            console.error(
                "[ProMap Navigation] MapLayers modul nije učitan.",
            );

            showError(
                "MapLibre MapLayers modul nije učitan. Proveri layout i promap-map-layers.js.",
            );

            return false;
        }

        try {
            console.info(
                "[ProMap Navigation] Initializing MapLibre map...",
            );

            const record =
                await window.ProMap.MapLayers.attach(
                    mapElement,
                    {
                        styleUrl:
                            LOCAL_MAP_STYLE,

                        pmtilesUrl:
                            PROMAP_PMTILES_ENDPOINT,
                    },
                );

            const map =
                record?.maplibreMap ??
                record?.map ??
                null;

            if (
                !map ||
                typeof map.on !== "function" ||
                typeof map.getSource !== "function" ||
                typeof map.addSource !== "function"
            ) {
                throw new Error(
                    "MapLayers.attach nije vratio validnu MapLibre mapu.",
                );
            }

            state.map = map;
            window.__promapNavigationMap = map;

            state.localMap.host =
                mapElement;

            state.localMap.map =
                state.map;

            state.localMap.ready =
                true;

            state.localMap.failed =
                false;

            state.localMap.visible =
                true;

            state.localMap.archive =
                record.archiveUrl ||
                PROMAP_PMTILES_ENDPOINT;

            console.info(
                "[ProMap Navigation] MapLibre map ready.",
                {
                    archive:
                        state.localMap.archive,

                    maplibreVersion:
                        window.maplibregl?.version ??
                        "unknown",

                    hasMap:
                        Boolean(state.map),

                    hasSources:
                        typeof state.map
                            .getSource ===
                        "function",

                    hasLayers:
                        typeof state.map
                            .getLayer ===
                        "function",
                },
            );


            /*
             * =====================================================
             * LOCAL MAP ICONS
             * =====================================================
             *
             * promap-dark.json koristi runtime image ID-jeve:
             *
             * promap-shield-motorway
             * promap-shield-trunk
             * promap-shield-primary
             * promap-shield-secondary
             * promap-shield-tertiary
             * promap-fuel
             * promap-charging
             * promap-parking
             * promap-truck-service
             * promap-hospital
             * promap-police
             * promap-airport
             *
             * Oni moraju biti registrovani na ISTOM MapLibre
             * instance-u koji je napravio MapLayers.attach().
             */

            if (window.ProMapMapIcons) {
                window.ProMapMapIcons.init(
                    state.map,
                    {
                        debug: true
                    }
                ).then(result => {
                    console.info(
                        "[ProMap Navigation] Map icons:",
                        result
                    );
                });
            } else {
                console.warn(
                    "[ProMap Navigation] ProMapMapIcons nije učitan."
                );
            }


            /*
             * =====================================================
             * MAP ENHANCEMENTS
             * =====================================================
             */

            if (
                !state.localMap.enhancements &&
                window.ProMap
                    ?.MapEnhancements
                    ?.install
            ) {
                state.localMap.enhancements =
                    window.ProMap.MapEnhancements.install(
                        state.map,
                        {
                            visibleGroups: {
                                route: true,
                                restrictions: true,
                                traffic: false,
                                incidents: false,
                                fleet: false,
                                poi: true,
                                weather: false,
                                elevation: false,
                            },
                        },
                    );

                console.info(
                    "[ProMap Navigation] Map enhancements ready.",
                );

                /*
                 * Panel sada zna da je MapLibre +
                 * MapEnhancementState spreman.
                 */
                window.dispatchEvent(
                    new CustomEvent(
                        "promap:map-enhancements-ready",
                        {
                            detail: {
                                map:
                                    state.map,

                                enhancements:
                                    state.localMap
                                        .enhancements,
                            },
                        },
                    ),
                );

                applyPendingMobilePayload();
            }

            /*
             * =====================================================
             * MAP INTERACTION
             * =====================================================
             */

            state.map.on(
                "dragstart",
                () => {
                    if (state.live) {
                        state.liveFollow =
                            false;
                    }
                },
            );

            state.map.on(
                "click",
                (event) => {
                    if (!state.picking) {
                        return;
                    }

                    const latitude =
                        Number(
                            event?.lngLat?.lat,
                        );

                    const longitude =
                        Number(
                            event?.lngLat?.lng,
                        );

                    if (
                        !Number.isFinite(
                            latitude,
                        ) ||
                        !Number.isFinite(
                            longitude,
                        )
                    ) {
                        return;
                    }

                    const point = {
                        latitude,
                        longitude,

                        label:
                            `${latitude.toFixed(6)}, ` +
                            `${longitude.toFixed(6)}`,
                    };

                    if (
                        state.picking ===
                        "start"
                    ) {
                        if ($("navStart")) {
                            $("navStart").value =
                                point.label;
                        }

                        setStart(point);
                    } else {
                        if ($("navEnd")) {
                            $("navEnd").value =
                                point.label;
                        }

                        setDestination(
                            point,
                        );
                    }

                    state.picking =
                        null;

                    if (
                        typeof state.map
                            .getCanvas ===
                        "function"
                    ) {
                        state.map
                            .getCanvas()
                            .style.cursor =
                            "";
                    }

                    showError("");
                },
            );

            /*
             * IMPORTANT:
             *
             * Nikada ne dodavati:
             *
             * state.map.on("resize", () => {
             *     state.map.resize();
             * });
             *
             * To pravi beskonačnu resize petlju.
             */

            window.setTimeout(
                () => {
                    if (
                        state.map &&
                        typeof state.map
                            .resize ===
                        "function"
                    ) {
                        state.map.resize();
                    }

                    if (
                        state.localMap?.map &&
                        state.localMap.map !==
                        state.map &&
                        typeof state.localMap
                            .map.resize ===
                        "function"
                    ) {
                        state.localMap.map
                            .resize();
                    }

                    if (
                        state.localMap
                            ?.visible
                    ) {
                        scheduleLocalMapBackgroundSync();
                    }
                },
                250,
            );

            return true;
        } catch (error) {
            console.error(
                "[ProMap Navigation] MapLibre initialization failed:",
                error,
            );

            state.map = null;

            state.localMap.ready =
                false;

            state.localMap.failed =
                true;

            state.localMap.visible =
                false;

            state.localMap.lastError =
                error;

            state.localMap.enhancements =
                null;

            showError(
                error?.message ||
                "MapLibre mapa nije mogla da se inicijalizuje.",
            );

            return false;
        }
    }

    function setStart(point) {
        const normalized = normalizePoint(point);

        if (!normalized) {
            return false;
        }

        state.start = normalized;

        setText(
            "navStartResolved",
            normalized.label ||
            `${normalized.latitude.toFixed(6)}, ` +
            `${normalized.longitude.toFixed(6)}`,
        );

        if (state.map) {
            state.startMarker?.remove();

            const label =
                normalized.label ||
                `${normalized.latitude.toFixed(6)}, ` +
                `${normalized.longitude.toFixed(6)}`;

            state.startMarker =
                createMapMarker(
                    normalized.latitude,
                    normalized.longitude,
                    "start",
                    `<strong>START</strong><br>${escapeHtml(label)}`,
                );
        }

        return true;
    }


    function createMapMarker(
        latitude,
        longitude,
        type,
        html,
    ) {
        const maplibregl = window.maplibregl;

        if (
            !maplibregl ||
            typeof maplibregl.Marker !== "function" ||
            !state.map
        ) {
            return null;
        }

        const element =
            document.createElement("div");

        element.className =
            `promap-nav-marker promap-nav-marker-${type}`;

        element.innerHTML = html;

        const marker =
            new maplibregl.Marker({
                element,
                anchor: "center",
            })
                .setLngLat([
                    longitude,
                    latitude,
                ])
                .addTo(state.map);

        if (html) {
            marker
                .setPopup(
                    new maplibregl.Popup({
                        offset: 18,
                    }).setHTML(html),
                );
        }

        return marker;
    }




    function setDestination(point) {
        const normalized = normalizePoint(point);

        if (!normalized) {
            return false;
        }

        state.destination = normalized;

        setText(
            "navEndResolved",
            normalized.label ||
            `${normalized.latitude.toFixed(6)}, ` +
            `${normalized.longitude.toFixed(6)}`,
        );

        if (state.map) {
            state.destinationMarker?.remove();

            const label =
                normalized.label ||
                `${normalized.latitude.toFixed(6)}, ` +
                `${normalized.longitude.toFixed(6)}`;

            state.destinationMarker =
                createMapMarker(
                    normalized.latitude,
                    normalized.longitude,
                    "destination",
                    `<strong>ODREDIŠTE</strong><br>${escapeHtml(label)}`,
                );
        }

        return true;
    }

    // ============================================================
    // GEOCODING
    // ============================================================

    async function geocode(query) {
        const response = await fetch(
            `/api/geocode?q=${encodeURIComponent(query)}&limit=5`,
            {
                headers: {
                    Accept: "application/json",
                },
            },
        );

        let payload = null;

        try {
            payload = await response.json();
        } catch {
            payload = null;
        }

        if (!response.ok) {
            throw new Error(
                payload?.message || `Geocoding API HTTP ${response.status}`,
            );
        }

        return Array.isArray(payload) ? payload : [];
    }

    function clearSuggestions(target) {
        const box = $(
            target === "start" ? "navStartSuggestions" : "navEndSuggestions",
        );

        if (box) {
            box.innerHTML = "";
        }
    }

    function renderSuggestions(target, items) {
        const box = $(
            target === "start" ? "navStartSuggestions" : "navEndSuggestions",
        );

        if (!box) {
            return;
        }

        box.innerHTML = "";

        for (const item of items.slice(0, 5)) {
            const point = normalizePoint(item);

            if (!point) {
                continue;
            }

            const button = document.createElement("button");

            button.type = "button";

            button.className = "nav-suggestion";

            const display =
                point.label ||
                `${point.latitude.toFixed(6)}, ${point.longitude.toFixed(6)}`;

            button.textContent = display;

            button.addEventListener("click", () => {
                if (target === "start") {
                    if ($("navStart")) {
                        $("navStart").value = display;
                    }

                    setStart({
                        ...point,
                        label: display,
                    });
                } else {
                    if ($("navEnd")) {
                        $("navEnd").value = display;
                    }

                    setDestination({
                        ...point,
                        label: display,
                    });
                }

                clearSuggestions(target);

                showError("");
            });

            box.appendChild(button);
        }
    }

    function scheduleGeocode(target) {
        const input = $(target === "start" ? "navStart" : "navEnd");

        if (!input) {
            return;
        }

        clearTimeout(state.geocodeTimers[target]);

        const value = input.value.trim();

        if (target === "start") {
            state.start = null;

            setText("navStartResolved", "Nije još potvrđeno");
        } else {
            state.destination = null;

            setText("navEndResolved", "Nije još potvrđeno");
        }

        const coordinate = parseCoordinateText(value);

        if (coordinate) {
            const resolved = {
                ...coordinate,

                label: `${coordinate.latitude.toFixed(6)}, ${coordinate.longitude.toFixed(6)}`,
            };

            target === "start" ? setStart(resolved) : setDestination(resolved);

            clearSuggestions(target);

            return;
        }

        if (value.length < 2) {
            clearSuggestions(target);

            return;
        }

        state.geocodeTimers[target] = window.setTimeout(async () => {
            try {
                const items = await geocode(value);

                renderSuggestions(target, items);
            } catch (error) {
                console.warn("[ProMap Navigation] Geocoding error:", error);
            }
        }, 300);
    }

    async function resolveInput(target) {
        const input = $(target === "start" ? "navStart" : "navEnd");

        if (!input) {
            return false;
        }

        const value = input.value.trim();

        const coordinate = parseCoordinateText(value);

        if (coordinate) {
            const resolved = {
                ...coordinate,

                label: `${coordinate.latitude.toFixed(6)}, ${coordinate.longitude.toFixed(6)}`,
            };

            return target === "start" ? setStart(resolved) : setDestination(resolved);
        }

        if (value.length < 2) {
            return false;
        }

        const items = await geocode(value);

        const point = normalizePoint(items[0]);

        if (!point) {
            return false;
        }

        const display =
            point.label ||
            `${point.latitude.toFixed(6)}, ${point.longitude.toFixed(6)}`;

        input.value = display;

        return target === "start"
            ? setStart({
                ...point,
                label: display,
            })
            : setDestination({
                ...point,
                label: display,
            });
    }

    // ============================================================
    // TRUCK / REQUEST
    // ============================================================

    function truckFromForm() {
        return {
            grossWeightT: numberValue("navWeight", 40),

            heightM: numberValue("navHeight", 4),

            widthM: numberValue("navWidth", 2.55),

            lengthM: numberValue("navLength", 16.5),

            axleLoadT: numberValue("navAxleLoad", 10),

            axles: Math.max(1, Math.round(numberValue("navAxles", 5))),

            isHgv: true,
            commercial: true,

            hazmat: $("navHazmat")?.checked ?? false,

            goods: null,

            adrClass: $("navAdrClass")?.value?.trim() || null,

            vehicleClass: "HeavyGoods",

            maxSpeedKmh: numberValue("navMaxSpeed", 90),
        };
    }

    function validateTruck() {
        if (state.profile !== "truck") {
            return null;
        }

        const checks = [
            ["navWeight", "Masa"],

            ["navHeight", "Visina"],

            ["navWidth", "Širina"],

            ["navLength", "Dužina"],

            ["navAxleLoad", "Osovinsko opterećenje"],

            ["navAxles", "Broj osovina"],

            ["navMaxSpeed", "Maksimalna brzina"],
        ];

        for (const [id, label] of checks) {
            const value = numberValue(id, NaN);

            if (!Number.isFinite(value) || value <= 0) {
                return `${label} mora biti veća od 0.`;
            }
        }

        return null;
    }

    function requestState() {
        return {
            start: state.start,

            destination: state.destination,

            profile: state.profile,

            avoidRestricted: $("navAvoid")?.checked ?? true,

            departureAt: new Date().toISOString(),

            truck: state.profile === "truck" ? truckFromForm() : null,
        };
    }

    // ============================================================
    // ROUTE RESULT
    // ============================================================

    function clearRouteLayers() {
        state.routeLayers = [];

        state.routeCoordinates = [];

        state.routeCumulativeDistances = [];

        if (state.localMap.enhancements) {
            state.localMap.enhancements.setRoute(
                {
                    routes: [],
                },
                0,
            );
        }
    }
    function clearRouteResult() {
        clearRouteLayers();

        state.routeResponse = null;

        state.selectedRouteIndex = 0;

        state.maneuvers = [];

        state.maneuverIndex = 0;

        state.routeProgressMeters = 0;

        setText("summaryDistance", "—");

        setText("summaryDuration", "—");

        setText("summaryEta", "—");

        setText("summarySafety", "READY");

        setText("summaryDiagnostics", "—");

        setText("mapDistance", "—");

        setText("mapDuration", "—");

        setText("mapEta", "—");

        setText(
            "routeSafeBadge",

            state.profile === "truck" ? "TRUCK SAFE" : "CAR ROUTE",
        );

        setText("nextInstructionIcon", "↑");

        setText("nextInstructionText", "—");

        setText("nextInstructionDistance", "—");

        setText("alternativeCount", "0");

        setText("warningCount", "0");

        setText("maneuverCount", "0");

        if ($("routeAlternatives")) {
            $("routeAlternatives").innerHTML = "";
        }

        if ($("routeWarnings")) {
            $("routeWarnings").innerHTML = "";
        }

        if ($("maneuverList")) {
            $("maneuverList").innerHTML = "";
        }

        if ($("diagnosticHighlights")) {
            $("diagnosticHighlights").innerHTML = "";
        }

        setHidden("mapRouteCard", true);

        setHidden("nextInstruction", true);

        setHidden("alternativesCard", true);

        setHidden("warningsCard", true);

        setHidden("maneuversCard", true);

        setText("diagnosticEngine", "—");

        setText("diagnosticGraph", "—");

        setText("diagnosticStates", "—");

        setText("diagnosticFallback", "—");

        setText("diagnosticStartSnap", "—");

        setText("diagnosticEndSnap", "—");

        setText("diagnosticTraversalCount", "—");

        setText("diagnosticFailureReason", "—");

        setText("diagnosticHighlightTitle", "Edge / road highlights");

        showError("");
    }

    function selectedRoute() {
        const routes = state.routeResponse?.routes;

        if (!Array.isArray(routes) || routes.length === 0) {
            return null;
        }

        return routes[state.selectedRouteIndex] || routes[0];
    }

    function routeViolations(route) {
        if (Array.isArray(route?.analysis?.violations)) {
            return route.analysis.violations;
        }

        if (Array.isArray(state.routeResponse?.violations)) {
            return state.routeResponse.violations;
        }

        return [];
    }

    function updateRouteLayerStyles() {
        if (
            !state.localMap?.map ||
            !state.localMap.enhancements
        ) {
            return;
        }

        const map =
            state.localMap.map;

        if (map.getLayer("route-main")) {
            map.setPaintProperty(
                "route-main",
                "line-opacity",
                1,
            );
        }

        if (map.getLayer("route-alternative")) {
            map.setPaintProperty(
                "route-alternative",
                "line-opacity",
                0.62,
            );
        }

        if (
            map.getLayer(
                "route-main-casing",
            )
        ) {
            map.setPaintProperty(
                "route-main-casing",
                "line-opacity",
                0.95,
            );
        }
    }

    function renderWarnings(violations) {
        const box = $("routeWarnings");

        if (!box) {
            return;
        }

        const items = Array.isArray(violations) ? violations : [];

        box.innerHTML = "";

        for (const item of items) {
            const row = document.createElement("div");

            row.className = "nav-list-item";

            row.innerHTML = `
                <span>${escapeHtml(item.type || "Restriction")}</span>

                <strong>${escapeHtml(
                item.name || item.id || "Ograničenje",
            )}</strong>

                <small>${escapeHtml(
                item.reason || "Aktivno ograničenje na izabranoj ruti.",
            )}</small>
            `;

            box.appendChild(row);
        }

        setText("warningCount", String(items.length));

        setHidden("warningsCard", items.length === 0);
    }

    // ============================================================
    // MANEUVERS
    // ============================================================

    function maneuverIcon(type, modifier) {
        const t = String(type || "").toLowerCase();

        const m = String(modifier || "").toLowerCase();

        if (t === "arrive") {
            return "●";
        }

        if (t === "depart") {
            return "↑";
        }

        if (t === "roundabout" || t === "rotary") {
            return "⟳";
        }

        if (m.includes("uturn")) {
            return "↶";
        }

        if (m.includes("sharp left")) {
            return "↙";
        }

        if (m === "left") {
            return "↰";
        }

        if (m.includes("slight left")) {
            return "↖";
        }

        if (m.includes("sharp right")) {
            return "↘";
        }

        if (m === "right") {
            return "↱";
        }

        if (m.includes("slight right")) {
            return "↗";
        }

        return "↑";
    }

    function buildManeuverInstruction(type, modifier, roadName, roundaboutExit) {
        const t = String(type || "continue").toLowerCase();

        const m = String(modifier || "").toLowerCase();

        const road = roadName ? ` na ${roadName}` : "";

        if (t === "arrive") {
            return "Stigli ste na odredište";
        }

        if (t === "depart") {
            return roadName ? `Krenite na ${roadName}` : "Krenite pravo";
        }

        if (t === "roundabout" || t === "rotary") {
            return roundaboutExit
                ? `Uđite u kružni tok i izađite na ${roundaboutExit}. izlazu${road}`
                : `Uđite u kružni tok${road}`;
        }

        if (m.includes("uturn")) {
            return `Polukružno okretanje${road}`;
        }

        if (m.includes("sharp left")) {
            return `Oštro levo${road}`;
        }

        if (m === "left") {
            return `Skrenite levo${road}`;
        }

        if (m.includes("slight left")) {
            return `Blago levo${road}`;
        }

        if (m.includes("sharp right")) {
            return `Oštro desno${road}`;
        }

        if (m === "right") {
            return `Skrenite desno${road}`;
        }

        if (m.includes("slight right")) {
            return `Blago desno${road}`;
        }

        return roadName ? `Nastavite pravo na ${roadName}` : "Nastavite pravo";
    }

    function normalizeManeuver(item) {
        if (!item) {
            return null;
        }

        const type = item.type || item.maneuver?.type || "continue";

        const modifier = item.modifier || item.maneuver?.modifier || null;

        const roadName = item.roadName || item.name || null;

        const roundaboutExit = item.roundaboutExit ?? item.maneuver?.exit ?? null;

        const distanceMeters = Number(
            item.distanceMeters ??
            item.distanceFromPreviousMeters ??
            item.distance ??
            0,
        );

        const distanceFromRouteStartMeters = Number(
            item.distanceFromRouteStartMeters ?? item.routeDistanceMeters ?? 0,
        );

        const latitude = Number(item.latitude ?? item.maneuver?.latitude);

        const longitude = Number(item.longitude ?? item.maneuver?.longitude);

        const prepared = {
            ...item,

            type,
            modifier,
            roadName,
            roundaboutExit,

            distanceMeters: Number.isFinite(distanceMeters) ? distanceMeters : 0,

            distanceFromRouteStartMeters: Number.isFinite(
                distanceFromRouteStartMeters,
            )
                ? distanceFromRouteStartMeters
                : 0,

            latitude: Number.isFinite(latitude) ? latitude : null,

            longitude: Number.isFinite(longitude) ? longitude : null,

            icon: item.icon || maneuverIcon(type, modifier),

            instruction:
                item.instruction ||
                buildManeuverInstruction(type, modifier, roadName, roundaboutExit),
        };

        if (
            window.ProMap.Maneuvers &&
            typeof window.ProMap.Maneuvers.normalize === "function"
        ) {
            const normalized = window.ProMap.Maneuvers.normalize(prepared);

            return {
                ...prepared,
                ...normalized,

                icon: normalized?.icon || prepared.icon,

                instruction: normalized?.instruction || prepared.instruction,
            };
        }

        return prepared;
    }

    function extractLegManeuvers(legs) {
        const result = [];

        if (!Array.isArray(legs)) {
            return result;
        }

        let cumulativeDistance = 0;

        for (const leg of legs) {
            const steps = Array.isArray(leg?.steps) ? leg.steps : [];

            for (const step of steps) {
                const maneuver = step?.maneuver || {};

                const location = Array.isArray(maneuver.location)
                    ? maneuver.location
                    : Array.isArray(step?.location)
                        ? step.location
                        : null;

                const distance = Number(step?.distance);

                const distanceMeters = Number.isFinite(distance) ? distance : 0;

                const latitude =
                    location && Number.isFinite(Number(location[1]))
                        ? Number(location[1])
                        : null;

                const longitude =
                    location && Number.isFinite(Number(location[0]))
                        ? Number(location[0])
                        : null;

                result.push(
                    normalizeManeuver({
                        type: maneuver.type || "continue",

                        modifier: maneuver.modifier || null,

                        instruction: step.instruction || null,

                        distanceMeters,

                        distanceFromRouteStartMeters: cumulativeDistance,

                        latitude,
                        longitude,

                        roadName: step.name || null,

                        roadRef: step.ref || null,

                        roundaboutExit: maneuver.exit ?? null,
                    }),
                );

                cumulativeDistance += distanceMeters;
            }
        }

        return result.filter(Boolean);
    }

    function buildRouteCumulativeDistances() {
        const coordinates = state.routeCoordinates;

        state.routeCumulativeDistances = [];

        if (!coordinates.length) {
            return;
        }

        state.routeCumulativeDistances = new Array(coordinates.length).fill(0);

        for (let i = 1; i < coordinates.length; i++) {
            state.routeCumulativeDistances[i] =
                state.routeCumulativeDistances[i - 1] +
                haversineMeters(
                    coordinates[i - 1][0],
                    coordinates[i - 1][1],
                    coordinates[i][0],
                    coordinates[i][1],
                );
        }
    }

    function routeProgressFromPosition(position) {
        if (!position || state.routeCoordinates.length < 2) {
            return null;
        }

        const latitude = Number(position.latitude);

        const longitude = Number(position.longitude);

        if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
            return null;
        }

        let bestDistance = Infinity;

        let bestProgress = 0;

        for (let i = 0; i < state.routeCoordinates.length; i++) {
            const point = state.routeCoordinates[i];

            const distance = haversineMeters(latitude, longitude, point[0], point[1]);

            if (distance < bestDistance) {
                bestDistance = distance;

                bestProgress = state.routeCumulativeDistances[i] ?? 0;
            }
        }

        state.routeProgressMeters = bestProgress;

        return bestProgress;
    }

    function getNextManeuver(position = getCurrentGpsPosition()) {
        if (!state.maneuvers.length) {
            return null;
        }

        if (!position) {
            return (
                state.maneuvers.find((item) => item.type !== "depart") ||
                state.maneuvers[0]
            );
        }

        const progress = routeProgressFromPosition(position);

        if (progress == null) {
            return state.maneuvers[state.maneuverIndex] || state.maneuvers[0];
        }

        const passedTolerance = 18;

        while (
            state.maneuverIndex < state.maneuvers.length - 1 &&
            Number(
                state.maneuvers[state.maneuverIndex].distanceFromRouteStartMeters || 0,
            ) <
            progress - passedTolerance
        ) {
            state.maneuverIndex++;
        }

        while (
            state.maneuverIndex < state.maneuvers.length - 1 &&
            state.maneuvers[state.maneuverIndex].type === "depart"
        ) {
            state.maneuverIndex++;
        }

        return (
            state.maneuvers[state.maneuverIndex] ||
            state.maneuvers[state.maneuvers.length - 1]
        );
    }

    function updateNextInstruction(position = getCurrentGpsPosition()) {
        const maneuver = getNextManeuver(position);

        if (!maneuver) {
            setHidden("nextInstruction", true);

            return;
        }

        let distance =
            Number(maneuver.distanceFromRouteStartMeters) -
            Number(state.routeProgressMeters || 0);

        if (!Number.isFinite(distance) || distance < 0) {
            distance = distanceToManeuverMeters(position, maneuver);
        }

        if (!Number.isFinite(distance) || distance < 0) {
            distance = Number(maneuver.distanceMeters) || 0;
        }

        setText("nextInstructionIcon", maneuver.icon || "↑");

        setText("nextInstructionText", maneuver.instruction || "Nastavite pravo");

        setText("nextInstructionDistance", formatDistance(distance));

        setHidden("nextInstruction", false);
    }

    function renderManeuvers(route) {
        const box = $("maneuverList");

        if (!box) {
            return;
        }

        let source = Array.isArray(state.routeResponse?.maneuvers)
            ? state.routeResponse.maneuvers
            : [];

        let legs = route?.legs;

        if (typeof legs === "string") {
            try {
                legs = JSON.parse(legs);
            } catch {
                legs = null;
            }
        }

        if (!source.length) {
            source = extractLegManeuvers(legs);
        }

        const normalized = source
            .map(normalizeManeuver)
            .filter(Boolean)
            .sort(
                (a, b) =>
                    Number(a.distanceFromRouteStartMeters || 0) -
                    Number(b.distanceFromRouteStartMeters || 0),
            );

        state.maneuvers = normalized;

        state.maneuverIndex = normalized.findIndex(
            (item) => item.type !== "depart",
        );

        if (state.maneuverIndex < 0) {
            state.maneuverIndex = 0;
        }

        state.routeProgressMeters = 0;

        box.innerHTML = "";

        for (const item of normalized.slice(0, 100)) {
            const row = document.createElement("div");

            row.className = "nav-list-item";

            row.innerHTML = `
                <span>${escapeHtml(item.icon || "↑")}</span>

                <strong>${escapeHtml(
                item.instruction || "Nastavi pravo",
            )}</strong>

                <small>${escapeHtml(
                formatDistance(item.distanceMeters),
            )}</small>
            `;

            box.appendChild(row);
        }

        setText("maneuverCount", String(normalized.length));

        setHidden("maneuversCard", normalized.length === 0);

        updateNextInstruction();
    }

    // ============================================================
    // ALTERNATIVES / DIAGNOSTICS / SUMMARY
    // ============================================================

    function renderAlternatives(routes) {
        const box = $("routeAlternatives");

        if (!box) {
            return;
        }

        box.innerHTML = "";

        if (!Array.isArray(routes) || routes.length <= 1) {
            setText("alternativeCount", "0");

            setHidden("alternativesCard", true);

            return;
        }

        routes.forEach((route, index) => {
            const button = document.createElement("button");

            button.type = "button";

            button.className = "nav-list-item";

            const restricted = Boolean(route?.analysis?.restricted);

            const selected = index === state.selectedRouteIndex;

            button.innerHTML = `
                    <span>Ruta ${index + 1}${selected ? " · izabrana" : ""}</span>
                    <strong>${escapeHtml(
                formatDistance(route.distance),
            )}</strong>
                    <small>${escapeHtml(
                formatDuration(route.duration),
            )}${restricted ? " · restrikcija" : ""}</small>
                `;

            button.addEventListener("click", () => selectRoute(index, true));

            box.appendChild(button);
        });

        setText("alternativeCount", String(routes.length));

        setHidden("alternativesCard", false);
    }

    function renderDiagnosticHighlights(items) {
        const box = $("diagnosticHighlights");

        if (!box) {
            return;
        }

        const highlights = Array.isArray(items)
            ? items.filter((item) => String(item || "").trim().length > 0)
            : [];

        box.innerHTML = "";

        if (!highlights.length) {
            const empty = document.createElement("div");
            empty.className = "nav-list-item muted";
            empty.textContent = "Nema dodatnih dijagnostičkih detalja za izabranu rutu.";
            box.appendChild(empty);
            return;
        }

        highlights.forEach((item, index) => {
            const row = document.createElement("div");
            row.className = "nav-list-item";
            row.innerHTML = `
                <span>Segment ${index + 1}</span>
                <strong>${escapeHtml(item)}</strong>
            `;
            box.appendChild(row);
        });
    }

    function updateDiagnostics(route = selectedRoute()) {
        const diagnostics = state.routeResponse?.diagnostics || state.lastRoutingErrorDiagnostics || {};
        const routeDebug = route?.analysis?.debug || {};
        const highlights = Array.isArray(routeDebug.highlights) && routeDebug.highlights.length
            ? routeDebug.highlights
            : diagnostics.highlights;

        setText("diagnosticEngine", diagnostics.engine || "—");

        setText(
            "diagnosticGraph",

            diagnostics.graphVersion != null ? String(diagnostics.graphVersion) : "—",
        );

        setText(
            "diagnosticStates",

            diagnostics.expandedStates != null
                ? String(diagnostics.expandedStates)
                : "—",
        );

        setText("diagnosticFallback", diagnostics.usedFallback ? "DA" : "NE");

        setText("diagnosticStartSnap", routeDebug.startSnap || diagnostics.startSnap || "—");

        setText("diagnosticEndSnap", routeDebug.endSnap || diagnostics.endSnap || "—");

        setText(
            "diagnosticTraversalCount",
            Number.isFinite(Number(routeDebug.traversalCount))
                ? String(routeDebug.traversalCount)
                : Number.isFinite(Number(diagnostics.traversalCount))
                    ? String(diagnostics.traversalCount)
                    : "—",
        );

        setText("diagnosticFailureReason", diagnostics.failureReason || "—");

        setText(
            "diagnosticHighlightTitle",
            routeDebug.summary || "Edge / road highlights",
        );

        renderDiagnosticHighlights(highlights);

        setEngine(
            diagnostics.engine || "PostGIS",

            Boolean(diagnostics.usedFallback),
        );
    }

    function updateSelectedRouteUi(route) {
        if (!route) {
            return;
        }

        const response = state.routeResponse || {};

        const summary = response.summary || {};

        const violations = routeViolations(route);

        const restricted =
            Boolean(route?.analysis?.restricted) || violations.length > 0;

        const safe = state.profile !== "truck" || !restricted;

        const distance = Number(route.distance ?? summary.distanceMeters);

        const duration = Number(route.duration ?? summary.durationSeconds);

        const eta =
            summary.estimatedArrival ||
            new Date(Date.now() + Math.max(0, duration || 0) * 1000).toISOString();

        setText("summaryDistance", formatDistance(distance));

        setText("summaryDuration", formatDuration(duration));

        setText("summaryEta", formatEta(eta, duration));

        setText("summarySafety", safe ? "SAFE" : "RESTRIKCIJA");

        setText("mapDistance", formatDistance(distance));

        setText("mapDuration", formatDuration(duration));

        setText("mapEta", formatEta(eta, duration));

        const diagnosticParts = [];

        const diagnostics = response.diagnostics || {};
        const routeDebug = route?.analysis?.debug || {};

        if (Number.isFinite(Number(diagnostics.expandedStates))) {
            diagnosticParts.push(`${diagnostics.expandedStates} states`);
        }

        if (diagnostics.graphVersion != null) {
            diagnosticParts.push(`graph ${diagnostics.graphVersion}`);
        }

        if (Number.isFinite(Number(routeDebug.traversalCount))) {
            diagnosticParts.push(`${routeDebug.traversalCount} traversals`);
        }

        if (violations.length) {
            diagnosticParts.push(`${violations.length} warnings`);
        }

        if (diagnostics.usedFallback) {
            diagnosticParts.push("fallback");
        }

        setText(
            "summaryDiagnostics",

            diagnosticParts.length ? diagnosticParts.join(" · ") : "—",
        );

        setText(
            "routeSafeBadge",

            state.profile === "truck"
                ? safe
                    ? "TRUCK SAFE"
                    : "TRUCK WARNING"
                : "CAR ROUTE",
        );

        const routeStatus = $("mapRouteStatus");

        if (routeStatus) {
            routeStatus.textContent = safe ? "Bezbedna" : "Upozorenje";

            routeStatus.classList.toggle("pm-badge-success", safe);

            routeStatus.classList.toggle("pm-badge-danger", !safe);
        }

        renderWarnings(violations);

        updateDiagnostics(route);

        updateNextInstruction();
    }

    function selectRoute(index, fit = false) {
        const routes = state.routeResponse?.routes;

        if (!Array.isArray(routes) || !routes[index]) {
            return;
        }

        state.selectedRouteIndex = index;

        const route = routes[index];

        state.routeCoordinates = geometryToLatLngs(route.geometry);

        buildRouteCumulativeDistances();

        updateRouteLayerStyles();

        updateSelectedRouteUi(route);

        renderAlternatives(routes);

        renderManeuvers(route);

        if (fit) {
            fitRoute();
        }
    }
    function renderRouteResponse(response) {
        console.debug(
            "[ProMap Navigation] Routing response:",
            response,
        );

        if (
            !response ||
            !Array.isArray(response.routes) ||
            response.routes.length === 0
        ) {
            throw new Error(
                "Routing servis nije vratio nijednu rutu.",
            );
        }

        if (
            !state.localMap.enhancements?.setRoute
        ) {
            throw new Error(
                "MapLibre route layer manager nije spreman.",
            );
        }

        state.routeResponse = response;

        const requestedIndex =
            Number(
                response.selectedRouteIndex ?? 0,
            );

        state.selectedRouteIndex =
            Number.isInteger(requestedIndex) &&
                requestedIndex >= 0 &&
                requestedIndex < response.routes.length
                ? requestedIndex
                : 0;

        const selected =
            response.routes[
            state.selectedRouteIndex
            ] || response.routes[0];

        state.routeCoordinates =
            geometryToLatLngs(
                selected?.geometry,
            );

        state.routeCumulativeDistances = [];

        state.localMap.enhancements.setRoute(
            response,
            state.selectedRouteIndex,
        );

        state.localMap.enhancements.setRestrictions(
            selected?.analysis?.violations ??
            response?.violations ??
            [],
        );

        setHidden(
            "mapRouteCard",
            false,
        );

        setHidden(
            "startLiveNavigation",
            false,
        );

        if ($("startLiveNavigation")) {
            $("startLiveNavigation").disabled =
                false;
        }

        updateDiagnostics();

        updateRouteLayerStyles();

        selectRoute(
            state.selectedRouteIndex,
            true,
        );
    }
    // ============================================================
    // ROUTING
    // ============================================================

    async function calculateRoute({ silent = false } = {}) {
        if (state.routing) {
            return false;
        }

        showError("");

        if (!state.start) {
            try {
                await resolveInput("start");
            } catch (error) {
                console.warn("[ProMap Navigation] Start resolve failed:", error);
            }
        }

        if (!state.destination) {
            try {
                await resolveInput("end");
            } catch (error) {
                console.warn("[ProMap Navigation] Destination resolve failed:", error);
            }
        }

        if (!state.start || !state.destination) {
            showError(
                "Izaberi validan start i odredište iz predloga, unesi koordinate ili izaberi tačke na mapi.",
            );

            return false;
        }

        const truckError = validateTruck();

        if (truckError) {
            showError(truckError);

            return false;
        }

        const directDistance = haversineMeters(
            state.start.latitude,
            state.start.longitude,
            state.destination.latitude,
            state.destination.longitude,
        );

        if (directDistance < 10) {
            showError("Start i odredište su preblizu.");

            return false;
        }

        if (
            !window.ProMap.Routing ||
            typeof window.ProMap.Routing.calculate !== "function"
        ) {
            showError("Routing JS modul nije učitan.");

            return false;
        }

        setRoutingUi(true);

        try {
            state.lastRoutingErrorDiagnostics = null;
            const response = await window.ProMap.Routing.calculate(requestState());

            console.debug(`[ROUTING-RESPONSE] Received response with ${response.routes?.length || 0} routes`);

            if (response.routes && response.routes.length > 0) {
                response.routes.forEach((route, idx) => {
                    const geom = route.geometry;
                    console.debug(`[ROUTING-RESPONSE] Route ${idx}: 
                        - type: ${geom?.type}
                        - coordinateCount: ${geom?.coordinates?.length || 0}
                        - distance: ${route.distance}m
                        - duration: ${route.duration}s`);

                    if (geom?.coordinates && geom.coordinates.length > 0) {
                        console.debug(`[ROUTING-RESPONSE] Route ${idx} first [lon,lat]: [${geom.coordinates[0][0]}, ${geom.coordinates[0][1]}]`);
                        console.debug(`[ROUTING-RESPONSE] Route ${idx} last [lon,lat]: [${geom.coordinates[geom.coordinates.length-1][0]}, ${geom.coordinates[geom.coordinates.length-1][1]}]`);
                    }
                });
            }

            renderRouteResponse(response);

            // ========================================================
            // PROMAP MAP ENHANCEMENTS
            // Povezuje PostGIS/OSRM rezultat sa aktivnim MapLibre slojevima
            // ========================================================

            state.localMap.enhancements?.setRoute?.(
                response,
                state.selectedRouteIndex,
            );

            const selectedRoute =
                response.routes?.[state.selectedRouteIndex] ??
                response.routes?.[0] ??
                null;

            state.localMap.enhancements?.setRestrictions?.(
                selectedRoute?.analysis?.violations ?? response?.violations ?? [],
            );

            setText("navStatus", "READY");

            if (!silent) {
                showError("");
            }

            return true;
        } catch (error) {
            console.error("[ProMap Navigation] Route calculation failed:", error, error?.payload || error?.responseText || null);
            state.lastRoutingErrorDiagnostics = error?.payload?.postGisDiagnostics || null;
            updateDiagnostics();
            setEngine(
                error?.payload?.postGisDiagnostics?.engine || error?.payload?.postGisCode || "POSTGIS",
                true,
            );
            showError(error?.message || buildRoutingErrorMessage(error));
            return false;
        } finally {
            setRoutingUi(false);
        }
    }
    // ============================================================
    // VEHICLE / POINTS
    // ============================================================

    function applyPreset(value) {
        const preset = PRESETS[value];

        if (!preset) {
            return;
        }

        const values = {
            navWeight: preset.weight,

            navHeight: preset.height,

            navWidth: preset.width,

            navLength: preset.length,

            navAxleLoad: preset.axleLoad,

            navAxles: preset.axles,

            navMaxSpeed: preset.maxSpeed,
        };

        for (const [id, nextValue] of Object.entries(values)) {
            const element = $(id);

            if (element) {
                element.value = String(nextValue);
            }
        }
    }

    function updateVehicleMode() {
        const button = $("calcRoute");

        const strong = button?.querySelector("strong");

        if (strong) {
            strong.textContent =
                state.profile === "truck"
                    ? "Izračunaj truck rutu"
                    : "Izračunaj auto rutu";
        }
    }

    function setProfile(profile) {
        state.profile = profile === "car" ? "car" : "truck";

        $("truckMode")?.classList.toggle("active", state.profile === "truck");

        $("carMode")?.classList.toggle("active", state.profile === "car");

        setText(
            "truckModeBadge",

            state.profile === "truck" ? "HGV" : "CAR",
        );

        setHidden(
            "truckFields",

            state.profile !== "truck",
        );

        setText(
            "routeSafeBadge",

            state.profile === "truck" ? "TRUCK SAFE" : "CAR ROUTE",
        );

        clearRouteResult();

        updateVehicleMode();
    }

    function swapPoints() {
        const oldStart = state.start;

        const oldDestination = state.destination;

        if (oldDestination) {
            setStart(oldDestination);

            if ($("navStart")) {
                $("navStart").value =
                    oldDestination.label ||
                    `${oldDestination.latitude.toFixed(6)}, ${oldDestination.longitude.toFixed(6)}`;
            }
        }

        if (oldStart) {
            setDestination(oldStart);

            if ($("navEnd")) {
                $("navEnd").value =
                    oldStart.label ||
                    `${oldStart.latitude.toFixed(6)}, ${oldStart.longitude.toFixed(6)}`;
            }
        }

        clearRouteResult();
    }

    function activateMapPick(target) {
        if (!state.map) {
            return;
        }

        state.picking = target === "end" ? "destination" : target;

        state.map.getContainer().style.cursor = "crosshair";

        showError(
            state.picking === "start"
                ? "Klikni na mapu da izabereš polaznu tačku."
                : "Klikni na mapu da izabereš odredište.",
        );
    }


    const mobile = window.matchMedia("(max-width: 900px)").matches;
    const wideScreen = window.matchMedia("(min-width: 1500px)").matches;
    const sidePanelWidth = mobile ? 0 : wideScreen ? 600 : 430;
    const topOverlayHeight = mobile ? 150 : 120;
    const bottomOverlayHeight = mobile ? 120 : 150;
    const leftVisualOffset = mobile ? 24 : wideScreen ? 116 : 44;

    function fitRoute() {
        const map = state.map;

        if (
            !map ||
            typeof map.fitBounds !== "function"
        ) {
            console.warn(
                "[ProMap Navigation] fitRoute: MapLibre mapa nije spremna."
            );
            return false;
        }

        const route = state.routeResponse?.routes?.[
            state.selectedRouteIndex ?? 0
        ] ?? state.routeResponse?.routes?.[0];

        if (!route) {
            console.warn(
                "[ProMap Navigation] fitRoute: ruta ne postoji."
            );
            return false;
        }

        const geometry = route.geometry;

        if (
            !geometry ||
            geometry.type !== "LineString" ||
            !Array.isArray(geometry.coordinates)
        ) {
            console.warn(
                "[ProMap Navigation] fitRoute: nevalidna LineString geometrija.",
                geometry
            );
            return false;
        }

        const coordinates = geometry.coordinates;

        if (coordinates.length === 0) {
            console.warn(
                "[ProMap Navigation] fitRoute: ruta nema koordinata."
            );
            return false;
        }

        /*
         * GeoJSON standard:
         *
         * [longitude, latitude]
         *
         * MapLibre takođe očekuje:
         *
         * [longitude, latitude]
         */

        let minLng = Infinity;
        let minLat = Infinity;
        let maxLng = -Infinity;
        let maxLat = -Infinity;

        let validCount = 0;

        for (const coordinate of coordinates) {
            if (
                !Array.isArray(coordinate) ||
                coordinate.length < 2
            ) {
                continue;
            }

            const lng = Number(coordinate[0]);
            const lat = Number(coordinate[1]);

            if (
                !Number.isFinite(lng) ||
                !Number.isFinite(lat)
            ) {
                continue;
            }

            /*
             * Geografska validacija.
             */
            if (
                lng < -180 ||
                lng > 180 ||
                lat < -90 ||
                lat > 90
            ) {
                continue;
            }

            minLng = Math.min(minLng, lng);
            minLat = Math.min(minLat, lat);
            maxLng = Math.max(maxLng, lng);
            maxLat = Math.max(maxLat, lat);

            validCount++;
        }

        if (
            validCount === 0 ||
            !Number.isFinite(minLng) ||
            !Number.isFinite(minLat) ||
            !Number.isFinite(maxLng) ||
            !Number.isFinite(maxLat)
        ) {
            console.error(
                "[ProMap Navigation] fitRoute: nije pronađena nijedna validna koordinata.",
                {
                    coordinateCount: coordinates.length,
                    first: coordinates[0],
                    last: coordinates[coordinates.length - 1],
                }
            );

            return false;
        }

        /*
         * Sprečava problem kada su start/end praktično
         * ista tačka i bounds imaju nultu širinu/visinu.
         */
        const lngSpan = maxLng - minLng;
        const latSpan = maxLat - minLat;

        const minSpan = 0.0001;

        if (lngSpan < minSpan) {
            const padding = minSpan / 2;

            minLng -= padding;
            maxLng += padding;
        }

        if (latSpan < minSpan) {
            const padding = minSpan / 2;

            minLat -= padding;
            maxLat += padding;
        }

        const bounds = [
            [minLng, minLat],
            [maxLng, maxLat],
        ];

        /*
         * Finalna zaštita pre MapLibre-a.
         */
        const allFinite = bounds.every(
            ([lng, lat]) =>
                Number.isFinite(lng) &&
                Number.isFinite(lat)
        );

        if (!allFinite) {
            console.error(
                "[ProMap Navigation] fitRoute: bounds sadrži NaN/Infinity.",
                bounds
            );

            return false;
        }

        console.info(
            "[ProMap Navigation] fitRoute:",
            {
                coordinateCount: coordinates.length,
                validCount,
                bounds,
            }
        );

        try {
            map.fitBounds(bounds, {
                padding: {
                    top: 100,
                    right: 60,
                    bottom: 180,
                    left: 60,
                },
                maxZoom: 15,
                duration: 700,
                essential: true,
            });

            return true;
        } catch (error) {
            console.error(
                "[ProMap Navigation] fitRoute MapLibre error:",
                error,
                {
                    bounds,
                    coordinateCount: coordinates.length,
                    validCount,
                }
            );

            return false;
        }
    }

    // ============================================================
    // GPS
    // ============================================================

    function centerGps() {
        const position = getCurrentGpsPosition();

        if (!position || !state.map) {
            showError("GPS trenutno nema dostupnu poziciju.");

            return;
        }

        state.liveFollow = true;
        followLivePosition(position, true);
    }

    function normalizedHeading(value) {
        const heading = Number(value);

        if (!Number.isFinite(heading)) {
            return null;
        }

        const normalized = heading % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    function effectiveHeading(position) {
        const heading = normalizedHeading(position?.heading ?? position?.coords?.heading);

        if (heading != null) {
            state.lastHeading = heading;
            return heading;
        }

        return state.lastHeading;
    }



    function followLivePosition(
        position,
        force = false,
    ) {
        if (!state.map || !position) {
            return;
        }

        const latitude = Number(
            position.latitude ??
            position.coords?.latitude,
        );

        const longitude = Number(
            position.longitude ??
            position.coords?.longitude,
        );

        if (
            !Number.isFinite(latitude) ||
            !Number.isFinite(longitude)
        ) {
            return;
        }

        const zoom = Math.max(
            16,
            state.map.getZoom() || 16,
        );

        const heading =
            effectiveHeading(position);

        const offsetMeters =
            zoom >= 17 ? 180 : 260;

        let targetLatitude = latitude;
        let targetLongitude = longitude;

        if (heading != null) {
            const radians =
                (heading * Math.PI) / 180;

            const latitudeOffset =
                (-Math.cos(radians) *
                    offsetMeters) /
                111320;

            const longitudeOffset =
                (Math.sin(radians) *
                    offsetMeters) /
                (111320 *
                    Math.max(
                        Math.cos(
                            (latitude * Math.PI) /
                            180,
                        ),
                        0.2,
                    ));

            targetLatitude += latitudeOffset;
            targetLongitude += longitudeOffset;
        } else {
            targetLatitude -= 0.0012;
        }

        state.map.easeTo({
            center: [
                targetLongitude,
                targetLatitude,
            ],
            zoom,
            duration: force ? 0 : 900,
            essential: true,
        });
    }

    function createGpsMarker(
        latitude,
        longitude,
    ) {
        const maplibregl =
            window.maplibregl;

        if (
            !maplibregl ||
            !state.map
        ) {
            return null;
        }

        const element =
            document.createElement("div");

        element.className =
            "promap-gps-marker";

        element.innerHTML =
            '<span class="promap-gps-marker-core"></span>';

        return new maplibregl.Marker({
            element,
            anchor: "center",
        })
            .setLngLat([
                longitude,
                latitude,
            ])
            .addTo(state.map);
    }


    function updateGpsMarker(position) {
        if (!state.map) {
            return;
        }

        const latitude = Number(
            position?.latitude ??
            position?.coords?.latitude,
        );

        const longitude = Number(
            position?.longitude ??
            position?.coords?.longitude,
        );

        if (
            !Number.isFinite(latitude) ||
            !Number.isFinite(longitude)
        ) {
            return;
        }

        if (!state.gpsMarker) {
            state.gpsMarker =
                createGpsMarker(
                    latitude,
                    longitude,
                );
        } else if (
            typeof state.gpsMarker.setLngLat === "function"
        ) {
            state.gpsMarker.setLngLat([
                longitude,
                latitude,
            ]);
        }

        const speed = Number(
            position?.speed ??
            position?.coords?.speed,
        );

        const accuracy = Number(
            position?.accuracy ??
            position?.coords?.accuracy,
        );

        setGpsStatus("ON", "ready");

        setText(
            "liveChip",
            "GPS ON",
        );

        setText(
            "liveSpeed",
            Number.isFinite(speed)
                ? `${Math.round(speed * 3.6)} km/h`
                : "—",
        );

        setText(
            "liveAccuracy",
            Number.isFinite(accuracy)
                ? `±${Math.round(accuracy)} m`
                : "—",
        );

        setText(
            "livePosition",
            `${latitude.toFixed(6)}, ${longitude.toFixed(6)}`,
        );

        const offRoute =
            distanceToRouteMeters(
                latitude,
                longitude,
            );

        setText(
            "liveOffRoute",
            offRoute == null
                ? "—"
                : offRoute > 100
                    ? `DA · ${Math.round(offRoute)} m`
                    : "NE",
        );

        updateNextInstruction({
            latitude,
            longitude,
            accuracy,
            speed,

            heading: Number(
                position?.heading ??
                position?.coords?.heading,
            ),

            timestamp:
                position?.timestamp ??
                position?.coords?.timestamp,
        });

        if (
            state.live &&
            state.liveFollow
        ) {
            followLivePosition({
                latitude,
                longitude,
                heading: Number(
                    position?.heading ??
                    position?.coords?.heading,
                ),
            });
        }

        if (
            state.live &&
            state.routeResponse &&
            offRoute != null &&
            offRoute > 100 &&
            Date.now() -
            state.lastRerouteAt >
            30000
        ) {
            state.lastRerouteAt =
                Date.now();

            state.start = {
                latitude,
                longitude,
                label:
                    "Trenutna GPS lokacija",
            };

            if ($("navStart")) {
                $("navStart").value =
                    `${latitude.toFixed(6)}, ${longitude.toFixed(6)}`;
            }

            setText(
                "navStartResolved",
                "Trenutna GPS lokacija",
            );

            void calculateRoute({
                silent: true,
            });
        }
    }

    function gpsErrorMessage(error) {
        switch (error?.code) {
            case 1:
                return "GPS dozvola je odbijena.";
            case 2:
                return "GPS lokacija trenutno nije dostupna.";
            case 3:
                return "GPS zahtev je istekao.";
            default:
                return "Greška pri čitanju GPS lokacije.";
        }
    }

    async function startLiveNavigation() {
        if (!window.ProMap.Gps?.isSupported?.()) {
            showError("Browser ne podržava GPS geolokaciju.");

            return;
        }

        if (!state.routeResponse) {
            const ok = await calculateRoute();

            if (!ok || !state.routeResponse) {
                showError("Ruta nije izračunata. Prvo izračunaj rutu.");

                return;
            }
        }

        window.ProMap.Gps.stop();

        state.live = true;

        state.liveFollow = true;

        state.lastRerouteAt = 0;
        state.lastHeading = null;

        setHidden("startLiveNavigation", true);

        setHidden("stopLiveNavigation", false);

        setGpsStatus("STARTING", "warning");

        setText("liveChip", "GPS STARTING");

        if (state.routeCoordinates.length) {
            fitRoute();
        }

        window.ProMap.Gps.start({
            enableHighAccuracy: true,

            maximumAge: 2000,

            timeout: 15000,

            onPosition: updateGpsMarker,

            onError: (error) => {
                console.warn("[ProMap Navigation] GPS error:", error);

                showError(gpsErrorMessage(error));

                setGpsStatus("ERROR", "danger");

                setText("liveChip", "GPS ERROR");
            },
        });
    }

    function stopLiveNavigation() {
        window.ProMap.Gps?.stop?.();

        state.live = false;

        state.liveFollow = true;
        state.lastHeading = null;

        setHidden("startLiveNavigation", false);

        setHidden("stopLiveNavigation", true);

        setGpsStatus("OFF");

        setText("liveChip", "GPS OFF");

        setText("liveSpeed", "0 km/h");

        setText("liveAccuracy", "—");

        setText("liveOffRoute", "NE");

        setText("livePosition", "Lokacija nije aktivna");
    }


    async function useCurrentLocation() {
        if (!window.ProMap.Gps?.isSupported?.()) {
            showError(
                "Browser ne podržava GPS geolokaciju.",
            );

            setGpsStatus("ERROR", "danger");

            return;
        }

        setGpsStatus(
            "LOCATING",
            "warning",
        );

        try {
            const position =
                await window.ProMap.Gps.getCurrentPosition({
                    enableHighAccuracy: true,
                    maximumAge: 0,
                    timeout: 15000,
                });

            const point = {
                latitude: Number(position.latitude),
                longitude: Number(position.longitude),
                label: "Moja trenutna lokacija",
            };

            if (
                !Number.isFinite(point.latitude) ||
                !Number.isFinite(point.longitude)
            ) {
                throw new Error(
                    "GPS je vratio nevalidne koordinate.",
                );
            }

            setStart(point);

            if ($("navStart")) {
                $("navStart").value =
                    `${point.latitude.toFixed(6)}, ${point.longitude.toFixed(6)}`;
            }

            updateGpsMarker(position);

            setGpsStatus(
                "READY",
                "ready",
            );

            setText(
                "liveChip",
                "GPS READY",
            );

            if (
                state.map &&
                typeof state.map.flyTo === "function"
            ) {
                state.map.flyTo({
                    center: [
                        point.longitude,
                        point.latitude,
                    ],

                    zoom: 14,

                    duration: 700,

                    essential: true,
                });
            }

            showError("");

        } catch (error) {
            console.warn(
                "[ProMap Navigation] useCurrentLocation GPS error:",
                error,
            );

            showError(
                gpsErrorMessage(error),
            );

            setGpsStatus(
                "ERROR",
                "danger",
            );
        }
    }
    // ============================================================
    // MAP TABS
    // ============================================================

    function setMapMode(mode) {
        const ids = {
            route: "mapRouteTab",

            restrictions: "mapRestrictionsTab",

            gps: "mapGpsTab",
        };

        Object.values(ids).forEach((id) => $(id)?.classList.remove("active"));

        $(ids[mode])?.classList.add("active");

        if (mode === "route") {
            fitRoute();
            return;
        }

        if (mode === "restrictions") {
            const warnings = $("warningsCard");

            if (warnings && !warnings.hidden) {
                warnings.scrollIntoView({
                    behavior: "smooth",
                    block: "nearest",
                });
            } else {
                showError("Izabrana ruta nema aktivna upozorenja.");
            }

            return;
        }

        if (mode === "gps") {
            if (getCurrentGpsPosition()) {
                centerGps();
            } else if (!state.live) {
                void startLiveNavigation();
            }
        }
    }

    // ============================================================
    // EVENTS
    // ============================================================

    function bindEvents() {
        $("calcRoute")?.addEventListener("click", () => void calculateRoute());

        $("swapPoints")?.addEventListener("click", swapPoints);

        $("truckMode")?.addEventListener("click", () => setProfile("truck"));

        $("carMode")?.addEventListener("click", () => setProfile("car"));

        $("truckPreset")?.addEventListener("change", (event) => {
            applyPreset(event.target.value);
        });

        $("useCurrentLocation")?.addEventListener("click", useCurrentLocation);

        $("fitRoute")?.addEventListener("click", fitRoute);

        $("centerGps")?.addEventListener("click", centerGps);

        $("pickStart")?.addEventListener("click", () => activateMapPick("start"));

        $("pickEnd")?.addEventListener("click", () =>
            activateMapPick("destination"),
        );

        $("startLiveNavigation")?.addEventListener(
            "click",
            () => void startLiveNavigation(),
        );

        $("stopLiveNavigation")?.addEventListener("click", stopLiveNavigation);

        $("mapRouteTab")?.addEventListener("click", () => setMapMode("route"));

        $("mapRestrictionsTab")?.addEventListener("click", () =>
            setMapMode("restrictions"),
        );

        $("mapGpsTab")?.addEventListener("click", () => setMapMode("gps"));

        $("navStart")?.addEventListener("input", () => scheduleGeocode("start"));

        $("navEnd")?.addEventListener("input", () => scheduleGeocode("end"));

        $("navStart")?.addEventListener("keydown", async (event) => {
            if (event.key !== "Enter") {
                return;
            }

            event.preventDefault();

            try {
                await resolveInput("start");
            } catch (error) {
                showError(error?.message || "Start nije moguće pronaći.");
            }
        });

        $("navEnd")?.addEventListener("keydown", async (event) => {
            if (event.key !== "Enter") {
                return;
            }

            event.preventDefault();

            try {
                await resolveInput("end");
            } catch (error) {
                showError(error?.message || "Odredište nije moguće pronaći.");
            }
        });

        document.addEventListener("click", (event) => {
            if (!event.target.closest(".nav-input-wrapper")) {
                clearSuggestions("start");

                clearSuggestions("end");
            }
        });

        window.addEventListener("resize", () => {
            if (
                state.map &&
                typeof state.map.resize === "function"
            ) {
                state.map.resize();
            }

            if (
                state.localMap?.map &&
                state.localMap.map !== state.map &&
                typeof state.localMap.map.resize === "function"
            ) {
                state.localMap.map.resize();
            }

            if (state.localMap?.visible) {
                scheduleLocalMapBackgroundSync();
            }
        });
    }

    // ============================================================
    // DEFAULTS / INIT
    // ============================================================

    function initializeDefaults() {
        if ($("navStart")) {
            $("navStart").value = $("navStart").value.trim() || DEFAULTS.start.label;
        }

        if ($("navEnd")) {
            $("navEnd").value =
                $("navEnd").value.trim() || DEFAULTS.destination.label;
        }

        setStart(DEFAULTS.start);

        setDestination(DEFAULTS.destination);

        applyPreset($("truckPreset")?.value || "40t");

        setProfile("truck");

        setGpsStatus("OFF");

        setText("engineHeader", "POSTGIS");

        setText("summaryEngine", "—");

        setText("diagnosticEngine", "—");

        setText("diagnosticGraph", "—");

        setText("diagnosticStates", "—");

        setText("diagnosticFallback", "—");

        setText("engineFooter", "OSM · PMTiles · Local");

        setText("liveChip", "GPS OFF");

        setText("liveSpeed", "0 km/h");

        setText("liveAccuracy", "—");

        setText("liveOffRoute", "NE");

        setText("livePosition", "Lokacija nije aktivna");

        setHidden("startLiveNavigation", false);

        setHidden("stopLiveNavigation", true);

        if ($("startLiveNavigation")) {
            $("startLiveNavigation").disabled = true;
        }
    }


    async function init() {
        /*
         * Prvo pripremamo UI i evente.
         * Zatim eksplicitno čekamo MapLibre.
         */
        initializeDefaults();

        bindEvents();

        const mapReady = await initializeMap();

        if (!mapReady) {
            console.error(
                "[ProMap Navigation] Initialization stopped: MapLibre nije spreman.",
            );

            return;
        }

        /*
         * MapLibre je sada sigurno dostupan.
         * Tek sada radimo finalni resize/sync.
         */
        window.setTimeout(() => {
            if (
                state.map &&
                typeof state.map.resize ===
                "function"
            ) {
                state.map.resize();
            }

            if (
                state.localMap?.map &&
                state.localMap.map !== state.map &&
                typeof state.localMap.map.resize ===
                "function"
            ) {
                state.localMap.map.resize();
            }

            if (
                state.localMap?.visible
            ) {
                scheduleLocalMapBackgroundSync();
            }
        }, 250);
    }



    ensureMobileBridge();

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            () => {
                void init();
            },
            {
                once: true,
            },
        );
    } else {
        void init();
    }
})();
