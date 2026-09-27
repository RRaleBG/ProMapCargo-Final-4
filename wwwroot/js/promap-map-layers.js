(function () {
    "use strict";

    window.ProMap = window.ProMap || {};

    const manager = (window.ProMap.MapLayers = window.ProMap.MapLayers || {});
    const maps = new WeakMap();
    const leafletLayers = new WeakMap();

    const LOCAL_MAPLIBRE_JS = "/lib/maplibre-gl/dist/maplibre-gl.js";
    const LOCAL_MAPLIBRE_CSS = "/lib/maplibre-gl/dist/maplibre-gl.css";
    const LOCAL_PMTILES_JS = "/lib/pmtiles/dist/pmtiles.js";
    const LOCAL_LEAFLET_BRIDGE_JS = "/lib/maplibre-gl-leaflet/leaflet-maplibre-gl.js";
    const LOCAL_MAP_STYLE = "/styles/promap-dark3.json?v=20260924-promap-dark-v12";
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
                reject(
                    new Error(
                        `Lokalni CSS nije moguće učitati: ${href}`,
                    ),
                );

            document.head.appendChild(link);
        });
    }

    function loadScriptOnce(src, globalName) {
        return new Promise((resolve, reject) => {
            if (globalName && window[globalName]) {
                resolve(window[globalName]);
                return;
            }

            const existing = document.querySelector(
                `script[src="${src}"]`,
            );

            if (existing) {
                existing.addEventListener(
                    "load",
                    () =>
                        resolve(
                            globalName
                                ? window[globalName]
                                : undefined,
                        ),
                    { once: true },
                );

                existing.addEventListener(
                    "error",
                    () =>
                        reject(
                            new Error(
                                `Lokalni JavaScript nije moguće učitati: ${src}`,
                            ),
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
                reject(
                    new Error(
                        `Lokalni JavaScript nije moguće učitati: ${src}`,
                    ),
                );

            document.head.appendChild(script);
        });
    }

    async function ensureLibraries() {
        await loadCssOnce(
            LOCAL_MAPLIBRE_CSS,
            LOCAL_MAPLIBRE_CSS_ID,
        );

        const maplibregl = await loadScriptOnce(
            LOCAL_MAPLIBRE_JS,
            "maplibregl",
        );

        const pmtiles = await loadScriptOnce(
            LOCAL_PMTILES_JS,
            "pmtiles",
        );

        if (
            !maplibregl ||
            typeof maplibregl.Map !== "function" ||
            typeof maplibregl.addProtocol !== "function"
        ) {
            throw new Error(
                "Lokalni MapLibre paket nije validan.",
            );
        }

        if (
            !pmtiles ||
            typeof pmtiles.Protocol !== "function"
        ) {
            throw new Error(
                "Lokalni PMTiles paket nije validan.",
            );
        }

        if (!window.__promapPmtilesProtocolRegistered) {
            const protocol = new pmtiles.Protocol();

            maplibregl.addProtocol(
                "pmtiles",
                protocol.tile,
            );

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
                throw new Error(
                    `promap-dark.json HTTP ${response.status}`,
                );
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

    function prepareContainer(container) {

        container.style.position = container.style.position || "relative";
        container.style.overflow = "hidden";
        container.style.background = "#031712";
    }

    async function createMaplibreMap(
        container,
        archiveUrl,
        center,
        zoom) {
        const maplibregl = await ensureLibraries();
        const style = await buildStyle(archiveUrl);

        const map = new maplibregl.Map({
            container,
            style,
            center: [center.lng, center.lat],
            zoom,

            attributionControl: true,

            interactive: true,

            dragPan: true,
            scrollZoom: true,
            boxZoom: true,
            doubleClickZoom: true,
            dragRotate: false,
            keyboard: true,
            touchZoomRotate: true,

            maxPitch: 0,
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
                        "Lokalna PMTiles mapa nije učitana u roku od 30 sekundi.",
                    ),
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
                console.error(
                    "[ProMap MapLayers] MapLibre load error:",
                    event?.error || event,
                );

                if (settled) {
                    return;
                }

                settled = true;

                window.clearTimeout(timer);

                reject(
                    event?.error ||
                    new Error(
                        "MapLibre resource error.",
                    ),
                );
            });
        });

        return map;
    }


    async function attach(container, options = {}) {
        if (!container) {
            return null;
        }

        let mapContainer = container.querySelector(":scope > .promap-maplibre-host");
        if (!mapContainer) {
            mapContainer = document.createElement("div");
            mapContainer.className = "promap-maplibre-host";
            container.prepend(mapContainer);
        }

        prepareContainer(mapContainer);

        const archiveUrl =
            options.archiveUrl ??
            options.pmtilesUrl ??
            DEFAULT_ARCHIVE_URL;

        let record = maps.get(container);

        if (
            record?.archiveUrl === archiveUrl &&
            record.maplibreMap
        ) {
            requestAnimationFrame(() => {
                record.maplibreMap?.resize();
            });

            return record;
        }

        if (record?.maplibreMap) {
            try {
                record.maplibreMap.remove();
            } catch {
                // ignore
            }

            record.maplibreMap = null;
        }

        const fallbackCenter = {
            lat: 50.2,
            lng: 9.75,
        };

        const center = options.center || fallbackCenter;

        const zoom = Number.isFinite(options.zoom)
            ? options.zoom
            : 4.35;

        const maplibreMap = await createMaplibreMap(
            mapContainer,
            archiveUrl,
            center,
            zoom,
        );

        /*
         * IMPORTANT:
         *
         * navigation.js historically očekuje record.map,
         * dok ovaj modul interno koristi record.maplibreMap.
         *
         * Vraćamo oba aliasa da svi postojeći pozivaoci
         * koriste istu MapLibre instancu.
         */
        record = {
            container,
            mapContainer,
            archiveUrl,
            maplibreMap,
            map: maplibreMap,
        };

        maps.set(container, record);

        maplibreMap.resize();

        return record;
    }


    async function setArchive(container, archiveUrl) {
        return attach(container, {
            archiveUrl:
                archiveUrl || DEFAULT_ARCHIVE_URL,
        });
    }

    async function ensureLeafletBridge() {
        await ensureLibraries();

        if (!window.L) {
            throw new Error("Leaflet nije učitan.");
        }

        if (typeof window.L.maplibreGL !== "function") {
            await loadScriptOnce(LOCAL_LEAFLET_BRIDGE_JS, null);
        }

        if (typeof window.L.maplibreGL !== "function") {
            throw new Error("Leaflet/MapLibre bridge nije validan.");
        }

        return window.L;
    }

    /*
     * Za stranice koje koriste Leaflet kao primarnu mapu (markeri, popup-ovi,
     * fitBounds...), MapLibre GL PMTiles bazna mapa se dodaje kao Leaflet
     * layer preko maplibre-gl-leaflet mosta, umesto da se Leaflet instanca
     * prosledi attach()-u koji očekuje sirovi DOM kontejner.
     */
    async function attachToLeaflet(leafletMap, options = {}) {
        if (!leafletMap) {
            return null;
        }

        await ensureLeafletBridge();

        const archiveUrl =
            options.archiveUrl ??
            options.pmtilesUrl ??
            DEFAULT_ARCHIVE_URL;

        let record = leafletLayers.get(leafletMap);

        if (record?.archiveUrl === archiveUrl && record.glLayer) {
            return record;
        }

        if (record?.glLayer) {
            leafletMap.removeLayer(record.glLayer);
            record.glLayer = null;
        }

        const style = await buildStyle(archiveUrl);

        const glLayer = window.L.maplibreGL({
            style,
            attributionControl: false,
        });

        glLayer.addTo(leafletMap);

        record = {
            leafletMap,
            archiveUrl,
            glLayer,
            maplibreMap: glLayer.getMaplibreMap(),
        };

        leafletLayers.set(leafletMap, record);

        return record;
    }

    async function setLeafletArchive(leafletMap, archiveUrl) {
        return attachToLeaflet(leafletMap, {
            archiveUrl: archiveUrl || DEFAULT_ARCHIVE_URL,
        });
    }

    function getArchive(container) {
        return (
            maps.get(container)?.archiveUrl ||
            null
        );
    }

    function getMap(container) {
        return (
            maps.get(container)?.maplibreMap ||
            null
        );
    }

    function setTheme(container, theme) {
        const map = maps.get(container)?.maplibreMap;

        if (!map || typeof map.setPaintProperty !== "function") {
            return false;
        }

        const palettes = {
            dark: {
                background: "#091318",
                landcover: "#223c37",
                landuse: "#29413e",
                water: "#123f50",
                road: "#58d7b5",
                casing: "#0a2722",
            },
            roads: {
                background: "#e8edf0",
                landcover: "#dce8dc",
                landuse: "#e4e8e2",
                water: "#a9d5e5",
                road: "#ffffff",
                casing: "#a7b1b8",
            },
            satellite: {
                background: "#31403b",
                landcover: "#566b4c",
                landuse: "#5d684e",
                water: "#345f72",
                road: "#e8d8a4",
                casing: "#554d3d",
            },
        };

        const palette = palettes[theme] || palettes.dark;
        const set = (layer, property, value) => {
            if (map.getLayer(layer)) {
                map.setPaintProperty(layer, property, value);
            }
        };

        set("background", "background-color", palette.background);
        set("landcover", "fill-color", palette.landcover);
        set("landuse", "fill-color", palette.landuse);
        set("water", "fill-color", palette.water);

        [
            "road-motorway",
            "road-trunk",
            "road-primary",
            "road-secondary",
            "road-tertiary",
            "road-residential",
            "road-service",
            "road-minor",
        ].forEach((layer) => set(layer, "line-color", palette.road));

        [
            "road-motorway-casing",
            "road-trunk-casing",
            "road-primary-casing",
            "road-secondary-casing",
            "road-tertiary-casing",
        ].forEach((layer) => set(layer, "line-color", palette.casing));

        return true;
    }

    manager.attach = attach;
    manager.setArchive = setArchive;
    manager.getArchive = getArchive;
    manager.getMap = getMap;
    manager.setTheme = setTheme;
    manager.attachToLeaflet = attachToLeaflet;
    manager.setLeafletArchive = setLeafletArchive;
})();