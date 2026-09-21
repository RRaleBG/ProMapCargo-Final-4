/*
 * ============================================================
 * PROMAP CARGO
 * Navigation + PMTiles / MapLibre integration
 * ============================================================
 *
 * Ovaj modul:
 *
 * 1. koristi lokalni promap-dark.json
 * 2. koristi lokalni /api/map/europe PMTiles endpoint
 * 3. registruje PMTiles protocol
 * 4. ostavlja postojeći Leaflet navigation layer netaknutim
 * 5. povezuje route rezultat sa MapLibre enhancement layerima
 *
 * ============================================================
 */


/* ============================================================
   1. MAP CONSTANTS
   ============================================================
*/

const PROMAP_STYLE_ENDPOINT =
    "/styles/promap-dark.json";

const PROMAP_PMTILES_ENDPOINT =
    "/api/map/europe";


/* ============================================================
   2. PMTILES PROTOCOL
   ============================================================
*/

async function ensureProMapPmtilesProtocol(
    maplibregl
) {
    if (
        window.__promapPmtilesProtocolRegistered
    ) {
        return;
    }

    if (
        !window.pmtiles?.Protocol
    ) {
        await new Promise(
            (resolve, reject) => {
                const existing =
                    document.querySelector(
                        'script[data-promap-pmtiles="1"]'
                    );

                if (existing) {
                    if (
                        existing.dataset.loaded === "1"
                    ) {
                        resolve();
                        return;
                    }

                    existing.addEventListener(
                        "load",
                        resolve,
                        {
                            once: true
                        }
                    );

                    existing.addEventListener(
                        "error",
                        reject,
                        {
                            once: true
                        }
                    );

                    return;
                }

                const script =
                    document.createElement(
                        "script"
                    );

                script.src =
                    "https://unpkg.com/pmtiles@4.5.0/dist/pmtiles.js";

                script.async =
                    true;

                script.crossOrigin =
                    "anonymous";

                script.dataset.promapPmtiles =
                    "1";

                script.onload = () => {
                    script.dataset.loaded =
                        "1";

                    resolve();
                };

                script.onerror =
                    () => {
                        reject(
                            new Error(
                                "PMTiles biblioteka nije mogla da se učita."
                            )
                        );
                    };

                document.head.appendChild(
                    script
                );
            }
        );
    }

    if (
        !window.pmtiles?.Protocol
    ) {
        throw new Error(
            "PMTiles Protocol nije dostupan."
        );
    }

    const protocol =
        new window.pmtiles.Protocol({
            metadata: true
        });

    maplibregl.addProtocol(
        "pmtiles",
        protocol.tile
    );

    window.__promapPmtilesProtocol =
        protocol;

    window.__promapPmtilesProtocolRegistered =
        true;
}


/* ============================================================
   3. LOAD LOCAL PROMAP DARK STYLE
   ============================================================
*/

async function loadProMapDarkStyle(
    mapElement,
    state,
    loadMapLibre
) {
    const maplibregl =
        await loadMapLibre();

    await ensureProMapPmtilesProtocol(
        maplibregl
    );

    let host =
        document.getElementById(
            "promap-dark-map"
        );

    if (!host) {
        host =
            document.createElement(
                "div"
            );

        host.id =
            "promap-dark-map";

        host.style.position =
            "absolute";

        host.style.inset =
            "0";

        host.style.width =
            "100%";

        host.style.height =
            "100%";

        host.style.zIndex =
            "1";

        host.style.pointerEvents =
            "none";

        host.style.visibility =
            "hidden";

        host.style.overflow =
            "hidden";

        host.style.borderRadius =
            "inherit";

        mapElement.appendChild(
            host
        );
    }

    const response =
        await fetch(
            PROMAP_STYLE_ENDPOINT,
            {
                headers: {
                    Accept:
                        "application/json"
                },

                cache:
                    "no-store",

                credentials:
                    "same-origin"
            }
        );

    if (!response.ok) {
        throw new Error(
            `ProMap Dark style HTTP ${response.status}`
        );
    }

    const rawStyle =
        await response.json();

    const style =
        JSON.parse(
            JSON.stringify(
                rawStyle
            ).replaceAll(
                "__PROMAP_PM_TILES_URL__",
                PROMAP_PMTILES_ENDPOINT
            )
        );

    const map =
        new maplibregl.Map({
            container:
                host,

            style,

            center: [
                20.46,
                44.82
            ],

            zoom:
                7,

            bearing:
                0,

            pitch:
                0,

            attributionControl:
                true,

            interactive:
                false,

            dragPan:
                false,

            scrollZoom:
                false,

            boxZoom:
                false,

            doubleClickZoom:
                false,

            dragRotate:
                false,

            keyboard:
                false,

            touchZoomRotate:
                false,

            preserveDrawingBuffer:
                false
        });

    await new Promise(
        (resolve, reject) => {
            let settled =
                false;

            const finish =
                (
                    fn,
                    value
                ) => {
                    if (
                        settled
                    ) {
                        return;
                    }

                    settled =
                        true;

                    fn(value);
                };

            const timer =
                window.setTimeout(
                    () => {
                        finish(
                            reject,
                            new Error(
                                "ProMap Dark PMTiles style timeout (20s)."
                            )
                        );
                    },
                    20000
                );

            map.once(
                "load",
                () => {
                    window.clearTimeout(
                        timer
                    );

                    finish(
                        resolve
                    );
                }
            );

            map.once(
                "error",
                event => {
                    window.clearTimeout(
                        timer
                    );

                    finish(
                        reject,
                        new Error(
                            event?.error?.message ||
                            "MapLibre PMTiles resource error."
                        )
                    );
                }
            );
        }
    );

    state.tomTom =
        state.tomTom || {};

    state.tomTom.host =
        host;

    state.tomTom.map =
        map;

    state.tomTom.ready =
        true;

    state.tomTom.failed =
        false;

    host.style.visibility =
        "visible";

    map.resize();

    window.ProMap.Map =
        map;

    if (
        window.ProMap.MapEnhancements
            ?.install
    ) {
        window.ProMap
            .MapEnhancementState =
            window.ProMap
                .MapEnhancements
                .install(
                    map,
                    {
                        visibleGroups: {
                            route:
                                true,

                            restrictions:
                                true,

                            traffic:
                                false,

                            incidents:
                                false,

                            fleet:
                                true,

                            poi:
                                true,

                            weather:
                                false,

                            elevation:
                                false
                        }
                    }
                );

        window.ProMap
            .MapEnhancementState
            ?.startFleetPolling?.({
                intervalMs:
                    15000
            });
    }

    return map;
}


/* ============================================================
   4. UPDATE MAP ROUTE LAYERS
   ============================================================
*/

function updateProMapMapLayers(
    response,
    selectedRouteIndex
) {
    if (
        !window.ProMap
            .MapEnhancementState
    ) {
        return;
    }

    window.ProMap
        .MapEnhancementState
        .setRoute?.(
            response,
            Number.isInteger(
                selectedRouteIndex
            )
                ? selectedRouteIndex
                : 0
        );
}


/* ============================================================
   5. UPDATE RESTRICTIONS
   ============================================================
*/

function updateProMapRestrictions(
    violations
) {
    window.ProMap
        .MapEnhancementState
        ?.setRestrictions?.(
            violations || []
        );
}


/* ============================================================
   6. OPTIONAL POI
   ============================================================
*/

function updateProMapPoi(
    geojson
) {
    window.ProMap
        .MapEnhancementState
        ?.setPoi?.(
            geojson
        );
}


/* ============================================================
   7. OPTIONAL TRAFFIC
   ============================================================
*/

function updateProMapTraffic(
    geojson
) {
    window.ProMap
        .MapEnhancementState
        ?.setTraffic?.(
            geojson
        );
}


/* ============================================================
   8. OPTIONAL INCIDENTS
   ============================================================
*/

function updateProMapIncidents(
    geojson
) {
    window.ProMap
        .MapEnhancementState
        ?.setIncidents?.(
            geojson
        );
}


/* ============================================================
   9. OPTIONAL WEATHER
   ============================================================
*/

function updateProMapWeather(
    geojson
) {
    window.ProMap
        .MapEnhancementState
        ?.setWeather?.(
            geojson
        );
}


/* ============================================================
   10. OPTIONAL ELEVATION
   ============================================================
*/

function updateProMapElevation(
    geojson
) {
    window.ProMap
        .MapEnhancementState
        ?.setElevation?.(
            geojson
        );
}


/* ============================================================
   11. LAYER TOGGLE
   ============================================================
*/

function toggleProMapLayer(
    group,
    visible
) {
    window.ProMap
        .MapEnhancementState
        ?.setGroupVisible?.(
            group,
            Boolean(visible)
        );
}


/* ============================================================
   12. EXAMPLES
   ============================================================
 *
 * toggleProMapLayer("fleet", true);
 * toggleProMapLayer("poi", true);
 * toggleProMapLayer("traffic", true);
 * toggleProMapLayer("incidents", true);
 * toggleProMapLayer("weather", true);
 * toggleProMapLayer("elevation", true);
 *
 */


/* ============================================================
   13. ROUTE RESPONSE HOOK
   ============================================================
 *
 * U navigation.js:
 *
 * renderRouteResponse(response);
 *
 * odmah nakon toga:
 *
 * updateProMapMapLayers(
 *     response,
 *     state.selectedRouteIndex
 * );
 *
 */


/* ============================================================
   14. RESTRICTION HOOK
   ============================================================
 *
 * Ako želiš eksplicitno:
 *
 * updateProMapRestrictions(
 *     route?.analysis?.violations ||
 *     response?.violations ||
 *     []
 * );
 *
 */


/* ============================================================
   15. MAP READY HOOK
   ============================================================
*/

window.ProMap =
    window.ProMap || {};

window.ProMap.NavigationMapIntegration = {
    loadProMapDarkStyle,
    updateProMapMapLayers,
    updateProMapRestrictions,
    updateProMapPoi,
    updateProMapTraffic,
    updateProMapIncidents,
    updateProMapWeather,
    updateProMapElevation,
    toggleProMapLayer
};