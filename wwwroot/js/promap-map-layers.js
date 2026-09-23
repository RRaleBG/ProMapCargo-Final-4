(function () {
    "use strict";

    window.ProMap = window.ProMap || {};

    const manager = (window.ProMap.MapLayers = window.ProMap.MapLayers || {});

    const maps = new WeakMap();

    const LOCAL_MAPLIBRE_JS = "/lib/maplibre-gl/dist/maplibre-gl.js";
    const LOCAL_MAPLIBRE_CSS = "/lib/maplibre-gl/dist/maplibre-gl.css";
    const LOCAL_PMTILES_JS = "/lib/pmtiles/dist/pmtiles.js";
    const LOCAL_MAP_STYLE = "/styles/promap-dark.json?v=20260922-road-style-fix";
    const DEFAULT_ARCHIVE_URL = "/maps/europe.pmtiles";
    const LOCAL_MAPLIBRE_CSS_ID = "promap-shared-maplibre-css";

    function loadCssOnce(href, id) {
        return new Promise((resolve, reject) => {
            if (id && document.getElementById(id)) {
                resolve();
                return;
            }

            const existing = document.querySelector(`link[href="${href}"]`);

            if (existing) {
                resolve();
                return;
            }

            const link = document.createElement("link");
            link.rel = "stylesheet";
            link.href = href;

            if (id) {
                link.id = id;
            }

            link.onload = () => resolve();
            link.onerror = () =>
                reject(new Error(`Lokalni CSS nije moguće učitati: ${href}`));
            document.head.appendChild(link);
        });
    }

    function loadScriptOnce(src, globalName) {
        return new Promise((resolve, reject) => {
            if (globalName && window[globalName]) {
                resolve(window[globalName]);
                return;
            }

            const existing = document.querySelector(`script[src="${src}"]`);

            if (existing) {
                existing.addEventListener(
                    "load",
                    () => resolve(globalName ? window[globalName] : undefined),
                    { once: true },
                );
                existing.addEventListener(
                    "error",
                    () =>
                        reject(new Error(`Lokalni JavaScript nije moguće učitati: ${src}`)),
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
                reject(new Error(`Lokalni JavaScript nije moguće učitati: ${src}`));
            document.head.appendChild(script);
        });
    }

    async function ensureLibraries() {
        await loadCssOnce(LOCAL_MAPLIBRE_CSS, LOCAL_MAPLIBRE_CSS_ID);

        const maplibregl = await loadScriptOnce(LOCAL_MAPLIBRE_JS, "maplibregl");
        const pmtiles = await loadScriptOnce(LOCAL_PMTILES_JS, "pmtiles");

        if (
            !maplibregl ||
            typeof maplibregl.Map !== "function" ||
            typeof maplibregl.addProtocol !== "function"
        ) {
            throw new Error("Lokalni MapLibre paket nije validan.");
        }

        if (!pmtiles || typeof pmtiles.Protocol !== "function") {
            throw new Error("Lokalni PMTiles paket nije validan.");
        }

        if (!window.__promapPmtilesProtocolRegistered) {
            const protocol = new pmtiles.Protocol();
            maplibregl.addProtocol("pmtiles", protocol.tile);
            window.__promapPmtilesProtocol = protocol;
            window.__promapPmtilesProtocolRegistered = true;
        }

        return maplibregl;
    }

    let rawStylePromise = null;

    async function buildStyle(archiveUrl) {
        rawStylePromise ??= fetch(LOCAL_MAP_STYLE, {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json",
            },
        }).then(async (response) => {
            if (!response.ok) {
                throw new Error(`promap-dark.json HTTP ${response.status}`);
            }

            return response.json();
        });

        const rawStyle = await rawStylePromise;

        return JSON.parse(
            JSON.stringify(rawStyle).replaceAll(
                "__PROMAP_PM_TILES_URL__",
                archiveUrl,
            ),
        );
    }

    function ensureHost(map) {
        const container = map.getContainer();
        container.style.position = container.style.position || "relative";
        container.style.overflow = "hidden";
        container.style.background = "#031712";

        let host = container.querySelector(":scope > .promap-local-basemap-host");

        if (host) {
            return host;
        }

        host = document.createElement("div");
        host.className = "promap-local-basemap-host";
        Object.assign(host.style, {
            position: "absolute",
            inset: "0",
            width: "100%",
            height: "100%",
            zIndex: "0",
            pointerEvents: "none",
            visibility: "hidden",
            overflow: "hidden",
            background: "#031712",
        });

        container.appendChild(host);
        return host;
    }

    function sync(record) {
        if (!record?.maplibreMap || !record?.leafletMap) {
            return;
        }

        const center = record.leafletMap.getCenter();
        const zoom = record.leafletMap.getZoom();
        const roundedZoom = Number(zoom.toFixed(2));
        const roundedLat = Number(center.lat.toFixed(6));
        const roundedLng = Number(center.lng.toFixed(6));

        if (
            record.lastSyncZoom === roundedZoom &&
            record.lastSyncCenter?.lat === roundedLat &&
            record.lastSyncCenter?.lng === roundedLng
        ) {
            return;
        }

        try {
            record.maplibreMap.jumpTo({
                center: [center.lng, center.lat],
                zoom,
                bearing: 0,
                pitch: 0,
            });
            record.lastSyncZoom = roundedZoom;
            record.lastSyncCenter = {
                lat: roundedLat,
                lng: roundedLng,
            };
            record.maplibreMap.resize();
        } catch { }
    }

    async function createMaplibreMap(host, archiveUrl, center, zoom) {
        const maplibregl = await ensureLibraries();
        const style = await buildStyle(archiveUrl);

        const map = new maplibregl.Map({
            container: host,
            style,
            center: [center.lng, center.lat],
            zoom,
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
                    new Error("Lokalna PMTiles mapa nije učitana u roku od 30 sekundi."),
                );
            }, 30000);

            map.once("load", () => {
                if (settled) {
                    return;
                }

                settled = true;
                window.clearTimeout(timer);
                resolve();
            });

            map.once("error", (event) => {
                console.error("[ProMap MapLayers] MapLibre load error:", event?.error || event);
                if (settled) {
                    return;
                }

                settled = true;
                window.clearTimeout(timer);
                reject(event?.error || new Error("MapLibre resource error."));
            });
        });

        return map;
    }

    async function attach(map, options) {
        if (!map) {
            return null;
        }

        const archiveUrl = options?.archiveUrl || DEFAULT_ARCHIVE_URL;
        let record = maps.get(map);

        if (record?.archiveUrl === archiveUrl && record.maplibreMap) {
            sync(record);
            return record;
        }

        const host = record?.host || ensureHost(map);
        const center = map.getCenter();
        const zoom = map.getZoom();

        if (!record) {
            record = {
                leafletMap: map,
                host,
                archiveUrl,
                maplibreMap: null,
                bound: false,
                lastSyncZoom: null,
                lastSyncCenter: null,
            };
        }

        if (record.maplibreMap) {
            record.maplibreMap.remove();
            record.maplibreMap = null;
        }

        host.replaceChildren();
        const maplibreMap = await createMaplibreMap(host, archiveUrl, center, zoom);
        host.style.visibility = "visible";
        map.getContainer().style.background = "transparent";

        record.host = host;
        record.archiveUrl = archiveUrl;
        record.maplibreMap = maplibreMap;

        if (!record.bound) {
            const syncView = () => sync(record);
            map.on("move", syncView);
            map.on("zoom", syncView);
            map.on("resize", () => record.maplibreMap?.resize());
            record.bound = true;
        }

        maps.set(map, record);
        sync(record);
        return record;
    }

    async function setArchive(map, archiveUrl) {
        return attach(map, {
            archiveUrl: archiveUrl || DEFAULT_ARCHIVE_URL,
        });
    }

    function getArchive(map) {
        return maps.get(map)?.archiveUrl || null;
    }

    manager.attach = attach;
    manager.setArchive = setArchive;
    manager.getArchive = getArchive;
})();
