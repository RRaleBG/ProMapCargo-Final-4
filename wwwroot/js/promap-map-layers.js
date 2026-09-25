(function () {
    "use strict";

    window.ProMap = window.ProMap || {};

    const manager = (window.ProMap.MapLayers = window.ProMap.MapLayers || {});
    const maps = new WeakMap();

    const LOCAL_MAPLIBRE_JS = "/lib/maplibre-gl/dist/maplibre-gl.js";
    const LOCAL_MAPLIBRE_CSS = "/lib/maplibre-gl/dist/maplibre-gl.css";
    const LOCAL_PMTILES_JS = "/lib/pmtiles/dist/pmtiles.js";
    const LOCAL_MAP_STYLE = "/styles/promap-dark2.json?v=20260924-promap-dark-v11";
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
        container.style.position =
            container.style.position || "relative";

        container.style.overflow = "hidden";
        container.style.background = "#031712";
    }

    async function createMaplibreMap(
        container,
        archiveUrl,
        center,
        zoom,
    ) {
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

        prepareContainer(container);

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

        container.replaceChildren();

        const fallbackCenter = {
            lat: 50.2,
            lng: 9.75,
        };

        const center = options.center || fallbackCenter;

        const zoom = Number.isFinite(options.zoom)
            ? options.zoom
            : 4.35;

        const maplibreMap = await createMaplibreMap(
            container,
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

    manager.attach = attach;
    manager.setArchive = setArchive;
    manager.getArchive = getArchive;
    manager.getMap = getMap;
})();