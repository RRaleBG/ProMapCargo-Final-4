(function (window) {
    "use strict";

    window.ProMap = window.ProMap || {};

    const EMPTY = () => ({
        type: "FeatureCollection",
        features: [],
    });

    const GROUPS = {
        route: [
            "route-alternative-casing",
            "route-alternative",
            "route-main-casing",
            "route-main",
        ],
        restrictions: ["route-restriction", "route-warning-point"],
        fleet: ["fleet-vehicles", "fleet-vehicle-label"],
        poi: ["truck-poi"],
        elevation: ["elevation"],
    };

    const SOURCES = [
        "route-main",
        "route-alternative",
        "route-restrictions",
        "route-warnings",
        "fleet-vehicles",
        "truck-poi",    
        "elevation",
    ];

    function asFeatureCollection(value) {
        if (!value) {
            return EMPTY();
        }

        if (value.type === "FeatureCollection") {
            return value;
        }

        if (value.type === "Feature") {
            return {
                type: "FeatureCollection",
                features: [value],
            };
        }

        if (value.type && value.coordinates) {
            return {
                type: "FeatureCollection",
                features: [
                    {
                        type: "Feature",
                        geometry: value,
                        properties: {},
                    },
                ],
            };
        }

        if (Array.isArray(value)) {
            return {
                type: "FeatureCollection",
                features: value.filter(Boolean),
            };
        }

        return EMPTY();
    }

    function geometryFeature(geometry, properties = {}) {
        if (!geometry) {
            return null;
        }

        if (typeof geometry === "string") {
            try {
                geometry = JSON.parse(geometry);
            } catch {
                return null;
            }
        }

        if (geometry.type === "Feature") {
            return {
                ...geometry,
                properties: {
                    ...(geometry.properties || {}),
                    ...properties,
                },
            };
        }

        if (!geometry.type || !geometry.coordinates) {
            return null;
        }

        return {
            type: "Feature",
            geometry,
            properties,
        };
    }

    function geometryToFeature(value, properties = {}) {
        if (!value) {
            return null;
        }

        if (typeof value === "string") {
            try {
                value = JSON.parse(value);
            } catch {
                return null;
            }
        }

        if (value.type === "FeatureCollection") {
            return value.features[0] || null;
        }

        if (value.type === "Feature") {
            return {
                ...value,
                properties: {
                    ...(value.properties || {}),
                    ...properties,
                },
            };
        }

        return geometryFeature(value, properties);
    }

    function routeStatusFrom(route) {
        if (route?.analysis?.restricted) {
            return "blocked";
        }

        const violations = route?.analysis?.violations;

        if (Array.isArray(violations) && violations.length) {
            return "warning";
        }

        return "safe";
    }

    function routeToFeatureCollection(route) {
        if (!route) {
            return EMPTY();
        }

        const properties = {
            status: routeStatusFrom(route),

            distance: Number.isFinite(Number(route.distance))
                ? Number(route.distance)
                : null,

            duration: Number.isFinite(Number(route.duration))
                ? Number(route.duration)
                : null,

            routeId: route.id ?? null,
        };

        if (Array.isArray(route.segments) && route.segments.length) {
            const features = route.segments
                .map((segment) =>
                    geometryToFeature(segment.geometry ?? segment.geojson, {
                        ...properties,

                        status: segment.status ?? properties.status,

                        severity: segment.severity ?? null,

                        reason: segment.reason ?? null,
                    }),
                )
                .filter(Boolean);

            if (features.length) {
                return {
                    type: "FeatureCollection",
                    features,
                };
            }
        }

        const feature = geometryToFeature(route.geometry, properties);

        return feature
            ? {
                type: "FeatureCollection",
                features: [feature],
            }
            : EMPTY();
    }

    function routeAlternativeCollection(routes, selectedIndex) {
        const features = [];

        (Array.isArray(routes) ? routes : []).forEach((route, index) => {
            if (index === selectedIndex) {
                return;
            }

            const fc = routeToFeatureCollection({
                ...route,
                analysis: route.analysis || {},
            });

            for (const feature of fc.features) {
                feature.properties = {
                    ...(feature.properties || {}),

                    status: "alternative",

                    routeIndex: index,

                    opacity: 0.56,
                };

                features.push(feature);
            }
        });

        return {
            type: "FeatureCollection",
            features,
        };
    }

    function violationCollection(violations) {
        const features = [];

        for (const item of Array.isArray(violations) ? violations : []) {
            const props = {
                severity: item.severity || (item.blocked ? "blocked" : "warning"),

                type: item.type || "restriction",

                name: item.name || item.id || "Truck restriction",

                reason: item.reason || "Aktivno ograničenje",
            };

            const feature = geometryToFeature(
                item.geometry || item.geom || item.location || item.geojson,

                props,
            );

            if (feature) {
                features.push(feature);
                continue;
            }

            const lat = Number(item.latitude ?? item.lat);

            const lon = Number(item.longitude ?? item.lon);

            if (Number.isFinite(lat) && Number.isFinite(lon)) {
                features.push({
                    type: "Feature",

                    geometry: {
                        type: "Point",

                        coordinates: [lon, lat],
                    },

                    properties: props,
                });
            }
        }

        return {
            type: "FeatureCollection",
            features,
        };
    }

    function bbox(fc) {
        let minX = Infinity;
        let minY = Infinity;
        let maxX = -Infinity;
        let maxY = -Infinity;

        function visit(coords) {
            if (!Array.isArray(coords)) {
                return;
            }

            if (
                coords.length >= 2 &&
                Number.isFinite(Number(coords[0])) &&
                Number.isFinite(Number(coords[1]))
            ) {
                const x = Number(coords[0]);

                const y = Number(coords[1]);

                minX = Math.min(minX, x);

                minY = Math.min(minY, y);

                maxX = Math.max(maxX, x);

                maxY = Math.max(maxY, y);

                return;
            }

            for (const item of coords) {
                visit(item);
            }
        }

        for (const feature of fc.features || []) {
            visit(feature?.geometry?.coordinates);
        }

        return Number.isFinite(minX)
            ? [
                [minX, minY],
                [maxX, maxY],
            ]
            : null;
    }

    function popupHtml(feature) {
        const properties = feature?.properties || {};

        const title =
            properties.name ||
            properties.label ||
            properties.id ||
            properties.type ||
            "ProMap";

        const lines = Object.entries(properties)
            .filter(
                ([key, value]) =>
                    value !== null &&
                    value !== undefined &&
                    value !== "" &&
                    !["name", "label", "id", "type"].includes(key),
            )
            .slice(0, 10)
            .map(
                ([key, value]) =>
                    `<div>` +
                    `<strong>${escapeHtml(key)}</strong>` +
                    `: ${escapeHtml(value)}` +
                    `</div>`,
            )
            .join("");

        return `
            <div style="
                min-width:220px;
                max-width:320px;
                font-family:Inter,system-ui,sans-serif;
            ">
                <div style="
                    font-weight:800;
                    font-size:14px;
                    margin-bottom:7px;
                ">
                    ${escapeHtml(title)}
                </div>

                ${lines
                ? `
                            <hr style="
                                border:0;
                                border-top:1px solid rgba(0,0,0,.14);
                                margin:7px 0;
                            ">

                            <div style="
                                display:grid;
                                gap:4px;
                                font-size:12px;
                            ">
                                ${lines}
                            </div>
                        `
                : ""
            }
            </div>
        `;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function addSourceIfNeeded(map, sourceId, data) {
        const source = map.getSource(sourceId);

        if (source) {
            source.setData(asFeatureCollection(data));

            return;
        }

        map.addSource(sourceId, {
            type: "geojson",
            data: asFeatureCollection(data),
        });
    }

    function addOperationalLayers(map) {
        if (!map || typeof map.addLayer !== "function") {
            return;
        }

        /*
         * =========================================================
         * ROUTE
         * =========================================================
         *
         * Route layer-i već postoje u promap-dark.json.
         * Ne dodajemo ih ponovo.
         */

        /*
         * =========================================================
         * RESTRICTIONS
         * =========================================================
         *
         * Takođe očekujemo da postoje u base style-u.
         */

        /*
         * =========================================================
         * FLEET
         * =========================================================
         */

        if (!map.getLayer("fleet-vehicles")) {
            map.addLayer({
                id: "fleet-vehicles",
                type: "circle",
                source: "fleet-vehicles",
                minzoom: 5,
                paint: {
                    "circle-radius": [
                        "interpolate",
                        ["linear"],
                        ["zoom"],
                        5, 4,
                        10, 6,
                        14, 8,
                    ],

                    "circle-color": [
                        "match",
                        ["get", "status"],

                        "Online",
                        "#10b981",

                        "online",
                        "#10b981",

                        "Moving",
                        "#10b981",

                        "moving",
                        "#10b981",

                        "Idle",
                        "#f59e0b",

                        "idle",
                        "#f59e0b",

                        "Offline",
                        "#64748b",

                        "offline",
                        "#64748b",

                        "#2dd4bf",
                    ],

                    "circle-stroke-color": "#061b17",
                    "circle-stroke-width": 2,
                },
            });
        }

        if (!map.getLayer("fleet-vehicle-label")) {
            map.addLayer({
                id: "fleet-vehicle-label",
                type: "symbol",
                source: "fleet-vehicles",
                minzoom: 8,
                layout: {
                    "text-field": [
                        "coalesce",
                        ["get", "registration"],
                        ["get", "label"],
                        "TRUCK",
                    ],

                    "text-size": [
                        "interpolate",
                        ["linear"],
                        ["zoom"],
                        8, 9,
                        14, 12,
                    ],

                    "text-offset": [0, 1.35],

                    "text-anchor": "top",

                    "text-allow-overlap": false,
                },

                paint: {
                    "text-color": "#dff8ee",

                    "text-halo-color": "#061b17",

                    "text-halo-width": 1.5,
                },
            });
        }

        /*
         * =========================================================
         * TRUCK POI
         * =========================================================
         */

        if (!map.getLayer("truck-poi")) {
            map.addLayer({
                id: "truck-poi",
                type: "circle",
                source: "truck-poi",
                minzoom: 8,
                paint: {
                    "circle-radius": [
                        "interpolate",
                        ["linear"],
                        ["zoom"],
                        8, 4,
                        12, 6,
                        16, 8,
                    ],

                    "circle-color": [
                        "match",
                        ["get", "type"],

                        "fuel",
                        "#f59e0b",

                        "parking",
                        "#3b82f6",

                        "truck_parking",
                        "#3b82f6",

                        "service",
                        "#8b5cf6",

                        "weigh_station",
                        "#ef4444",

                        "customs",
                        "#06b6d4",

                        "depot",
                        "#10b981",

                        "warehouse",
                        "#14b8a6",

                        "rest_area",
                        "#22c55e",

                        "charging",
                        "#a855f7",

                        "#2dd4bf",
                    ],

                    "circle-stroke-color": "#061b17",

                    "circle-stroke-width": 2,
                },
            });
        }

        /*
         * =========================================================
         * TRAFFIC
         * =========================================================
         */

        // if (!map.getLayer("traffic")) {
        //     map.addLayer({
        //         id: "traffic",
        //         type: "line",
        //         source: "traffic",
        //         minzoom: 5,

        //         layout: {
        //             "line-cap": "round",
        //             "line-join": "round",
        //         },

        //         paint: {
        //             "line-color": [
        //                 "match",
        //                 ["get", "level"],

        //                 "free",
        //                 "#22c55e",

        //                 "normal",
        //                 "#22c55e",

        //                 "moderate",
        //                 "#facc15",

        //                 "heavy",
        //                 "#f97316",

        //                 "severe",
        //                 "#ef4444",

        //                 "#94a3b8",
        //             ],

        //             "line-width": [
        //                 "interpolate",
        //                 ["linear"],
        //                 ["zoom"],
        //                 5, 1.5,
        //                 10, 3,
        //                 14, 5,
        //             ],

        //             "line-opacity": 0.82,
        //         },
        //     });
        // }

        /*
         * =========================================================
         * INCIDENTS
         * =========================================================
         */

        // if (!map.getLayer("incident")) {
        //     map.addLayer({
        //         id: "incident",
        //         type: "circle",
        //         source: "incidents",
        //         minzoom: 7,

        //         paint: {
        //             "circle-radius": [
        //                 "interpolate",
        //                 ["linear"],
        //                 ["zoom"],
        //                 7, 4,
        //                 12, 7,
        //                 16, 9,
        //             ],

        //             "circle-color": [
        //                 "match",
        //                 ["get", "type"],

        //                 "accident",
        //                 "#ef4444",

        //                 "roadworks",
        //                 "#f97316",

        //                 "closure",
        //                 "#dc2626",

        //                 "hazard",
        //                 "#f59e0b",

        //                 "police",
        //                 "#3b82f6",

        //                 "#ef4444",
        //             ],

        //             "circle-stroke-color": "#ffffff",

        //             "circle-stroke-width": 1.5,
        //         },
        //     });
        // }

        /*
         * =========================================================
         * WEATHER
         * =========================================================
         */

        // if (!map.getLayer("weather")) {
        //     map.addLayer({
        //         id: "weather",
        //         type: "circle",
        //         source: "weather",
        //         minzoom: 5,

        //         paint: {
        //             "circle-radius": [
        //                 "interpolate",
        //                 ["linear"],
        //                 ["zoom"],
        //                 5, 3,
        //                 10, 5,
        //                 14, 7,
        //             ],

        //             "circle-color": [
        //                 "match",
        //                 ["get", "type"],

        //                 "rain",
        //                 "#3b82f6",

        //                 "snow",
        //                 "#e2e8f0",

        //                 "storm",
        //                 "#8b5cf6",

        //                 "fog",
        //                 "#94a3b8",

        //                 "wind",
        //                 "#06b6d4",

        //                 "temperature",
        //                 "#f97316",

        //                 "#2dd4bf",
        //             ],

        //             "circle-opacity": 0.88,

        //             "circle-stroke-color": "#061b17",

        //             "circle-stroke-width": 1.5,
        //         },
        //     });
        // }

        /*
         * =========================================================
         * ELEVATION
         * =========================================================
         */

        if (!map.getLayer("elevation")) {
            map.addLayer({
                id: "elevation",
                type: "line",
                source: "elevation",
                minzoom: 7,

                layout: {
                    "line-cap": "round",
                    "line-join": "round",
                },

                paint: {
                    "line-color": [
                        "interpolate",
                        ["linear"],
                        ["coalesce", ["get", "grade"], 0],

                        -0.08,
                        "#22c55e",

                        0,
                        "#84cc16",

                        0.04,
                        "#facc15",

                        0.07,
                        "#f97316",

                        0.10,
                        "#ef4444",
                    ],

                    "line-width": [
                        "interpolate",
                        ["linear"],
                        ["zoom"],
                        7, 2,
                        12, 4,
                        16, 6,
                    ],

                    "line-opacity": 0.85,
                },
            });
        }
    }


    function setLayoutVisibility(map, layerId, visible) {
        if (!map || typeof map.getLayer !== "function") {
            return false;
        }

        if (!map.getLayer(layerId)) {
            return false;
        }

        try {
            map.setLayoutProperty(
                layerId,
                "visibility",
                visible ? "visible" : "none",
            );

            return true;
        } catch (error) {
            console.warn(
                `[ProMap Layers] Failed to change visibility for "${layerId}":`,
                error,
            );

            return false;
        }
    }

    function applyVisibility(map, visibleGroups) {
        if (!map) {
            return;
        }

        for (const [group, layerIds] of Object.entries(GROUPS)) {
            const visible = visibleGroups[group] !== false;

            for (const layerId of layerIds) {
                setLayoutVisibility(
                    map,
                    layerId,
                    visible,
                );
            }
        }
    }

    function setDataSafe(map, sourceId, data) {
        const source = map.getSource(sourceId);

        if (!source) {
            return;
        }

        source.setData(asFeatureCollection(data));
    }

    function buildFleetCollection(vehicles, trips) {
        const features = [];

        const vehicleRows = Array.isArray(vehicles) ? vehicles : [];

        const tripRows = Array.isArray(trips) ? trips : [];

        const tripByVehicle = new Map();

        for (const trip of tripRows) {
            if (trip?.VehicleId || trip?.vehicleId) {
                tripByVehicle.set(String(trip.VehicleId ?? trip.vehicleId), trip);
            }
        }

        for (const vehicle of vehicleRows) {
            const lat = Number(vehicle.currentLatitude ?? vehicle.CurrentLatitude);

            const lon = Number(vehicle.currentLongitude ?? vehicle.CurrentLongitude);

            if (!Number.isFinite(lat) || !Number.isFinite(lon)) {
                continue;
            }

            const trip = tripByVehicle.get(String(vehicle.id ?? vehicle.Id));

            const speed =
                vehicle.currentSpeedKmh ??
                vehicle.CurrentSpeedKmh ??
                trip?.currentSpeedKmh ??
                trip?.CurrentSpeedKmh ??
                null;

            const bearing =
                vehicle.currentBearing ??
                vehicle.CurrentBearing ??
                trip?.currentBearing ??
                trip?.CurrentBearing ??
                0;

            const registration =
                vehicle.registration ?? vehicle.Registration ?? "TRUCK";

            const status =
                vehicle.status ??
                vehicle.Status ??
                trip?.executionState ??
                trip?.ExecutionState ??
                "Unknown";

            features.push({
                type: "Feature",

                geometry: {
                    type: "Point",

                    coordinates: [lon, lat],
                },

                properties: {
                    id: vehicle.id ?? vehicle.Id ?? null,

                    registration,

                    status,

                    speedKmh: speed,

                    bearing,

                    type: "fleet",
                },
            });
        }

        return {
            type: "FeatureCollection",
            features,
        };
    }

    async function fetchJson(url, authOptions = {}) {
        const headers = {
            Accept: "application/json",
            ...(authOptions.headers || {}),
        };

        if (authOptions.token) {
            headers["Authorization"] = `Bearer ${authOptions.token}`;
        }

        const response = await fetch(url, {
            headers,
            credentials: authOptions.credentials || "same-origin",
        });

        if (!response.ok) {
            const error = new Error(`${url} HTTP ${response.status}`);
            error.status = response.status;
            error.url = url;
            throw error;
        }

        try {
            return await response.json();
        } catch (e) {
            const text = await response.text();
            console.error(`[ProMap] JSON parse error for ${url}:`, e.message, `Response: "${text}"`);
            throw e;
        }
    }

    function clearFleetData(map) {
        setDataSafe(map, "fleet-vehicles", EMPTY());
    }

    function isUnauthorizedFleetError(error) {
        return error?.status === 401;
    }

    function handleFleetUnavailable(map) {
        clearFleetData(map);
        return { vehicles: [], trips: [] };
    }


    function install(map, options = {}) {
        if (!map || typeof map.addSource !== "function") {
            throw new Error(
                "ProMap.MapEnhancements.install zahteva MapLibre map instancu.",
            );
        }

        const opts = {
            fitPadding: options.fitPadding ?? 60,

            popup: options.popup !== false,

            visibleGroups: {
                route: true,
                restrictions: true,
                fleet: false,
                poi: false,             
                elevation: false,

                ...(options.visibleGroups || {}),
            },
        };

        /*
         * =========================================================
         * GEOJSON SOURCES
         * =========================================================
         */

        for (const sourceId of SOURCES) {
            if (!map.getSource(sourceId)) {
                map.addSource(sourceId, {
                    type: "geojson",
                    data: EMPTY(),
                });
            }
        }

        /*
         * =========================================================
         * OPERATIONAL MAP LAYERS
         * =========================================================
         */

        addOperationalLayers(map);

        /*
         * =========================================================
         * LAYER VISIBILITY
         * =========================================================
         */

        applyVisibility(
            map,
            opts.visibleGroups,
        );

        /*
         * =========================================================
         * POPUPS
         * =========================================================
         */

        if (
            opts.popup &&
            !map.__promapEnhancementPopupBound
        ) {
            map.__promapEnhancementPopupBound = true;

            const popupLayerIds = [
                "route-warning-point",           
                "truck-poi",
                "fleet-vehicles",  
               
            ].filter(
                (id) => map.getLayer(id),
            );

            if (popupLayerIds.length) {
                map.on(
                    "click",
                    popupLayerIds,
                    (event) => {
                        const feature =
                            event.features?.[0];

                        if (!feature) {
                            return;
                        }

                        new map.constructor.Popup({
                            offset: 12,
                        })
                            .setLngLat(event.lngLat)
                            .setHTML(
                                popupHtml(feature),
                            )
                            .addTo(map);
                    },
                );
            }
        }

        /*
         * =========================================================
         * FLEET
         * =========================================================
         */

        async function loadFleet() {
            try {
                const [vehicles, trips] =
                    await Promise.all([
                        fetchJson(
                            "/api/business/vehicles",
                            opts.auth,
                        ),

                        fetchJson(
                            "/api/business/trips",
                            opts.auth,
                        ),
                    ]);

                setDataSafe(
                    map,
                    "fleet-vehicles",
                    buildFleetCollection(
                        vehicles,
                        trips,
                    ),
                );

                return {
                    vehicles,
                    trips,
                };
            } catch (error) {
                if (
                    isUnauthorizedFleetError(error)
                ) {
                    return handleFleetUnavailable(
                        map,
                    );
                }

                console.warn(
                    "[ProMap] Fleet load failed:",
                    error,
                );

                clearFleetData(map);

                return null;
            }
        }

        let fleetTimer = null;

        function startFleetPolling(config = {}) {
            stopFleetPolling();

            const intervalMs = Math.max(
                5000,
                Number(
                    config.intervalMs || 15000,
                ),
            );

            const initialDelayMs = Math.max(
                0,
                Number(
                    config.initialDelayMs || 0,
                ),
            );

            const triggerLoad = () => {
                void loadFleet();
            };

            if (initialDelayMs > 0) {
                window.setTimeout(
                    triggerLoad,
                    initialDelayMs,
                );
            } else {
                triggerLoad();
            }

            fleetTimer = window.setInterval(
                triggerLoad,
                intervalMs,
            );
        }

        function stopFleetPolling() {
            if (fleetTimer !== null) {
                window.clearInterval(
                    fleetTimer,
                );

                fleetTimer = null;
            }
        }

        /*
         * =========================================================
         * ROUTE
         * =========================================================
         */

        function setRoute(
            response,
            selectedIndex = 0,
        ) {
            const routes =
                Array.isArray(response?.routes)
                    ? response.routes
                    : [];

            const selectedRoute =
                routes[selectedIndex] ||
                routes[0] ||
                null;

            const alternative =
                routeAlternativeCollection(
                    routes,
                    selectedIndex,
                );

            const restrictions =
                violationCollection(
                    selectedRoute
                        ?.analysis
                        ?.violations ??
                    response?.violations ??
                    [],
                );

            setDataSafe(
                map,
                "route-main",
                routeToFeatureCollection(
                    selectedRoute,
                ),
            );

            setDataSafe(
                map,
                "route-alternative",
                alternative,
            );

            setDataSafe(
                map,
                "route-restrictions",
                restrictions,
            );

            setDataSafe(
                map,
                "route-warnings",
                restrictions,
            );
        }

        /*
         * =========================================================
         * FLEET / POI / TRAFFIC / ETC.
         * =========================================================
         */

        function setFleet(
            vehicles,
            trips,
        ) {
            setDataSafe(
                map,
                "fleet-vehicles",
                buildFleetCollection(
                    vehicles,
                    trips,
                ),
            );
        }

        function setRestrictions(
            violations,
        ) {
            const data =
                violationCollection(
                    violations,
                );

            setDataSafe(
                map,
                "route-restrictions",
                data,
            );

            setDataSafe(
                map,
                "route-warnings",
                data,
            );
        }

        function setPoi(value) {
            setDataSafe(
                map,
                "truck-poi",
                value,
            );
        }

        function setElevation(value) {
            setDataSafe(
                map,
                "elevation",
                value,
            );
        }

        /*
         * =========================================================
         * LAYER GROUP CONTROL
         * =========================================================
         */

        const visibleGroups = {
            ...opts.visibleGroups,
        };

        function setGroupVisible(
            group,
            visible,
        ) {
            if (!GROUPS[group]) {
                console.warn(
                    `[ProMap Layers] Nepoznata layer grupa: "${group}".`,
                );

                return false;
            }

            const isVisible =
                visible === true;

            visibleGroups[group] =
                isVisible;

            const ids =
                GROUPS[group];

            let changed = 0;

            for (const layerId of ids) {
                if (
                    setLayoutVisibility(
                        map,
                        layerId,
                        isVisible,
                    )
                ) {
                    changed++;
                }
            }

            console.info(
                `[ProMap Layers] ${group}: ${isVisible ? "ON" : "OFF"
                } (${changed}/${ids.length} layers)`,
            );

            return true;
        }

        /*
         * =========================================================
         * FIT DATA
         * =========================================================
         */

        function fitData(value) {
            const collection =
                asFeatureCollection(
                    value,
                );

            const bounds =
                bbox(collection);

            if (!bounds) {
                return false;
            }

            map.fitBounds(
                bounds,
                {
                    padding:
                        opts.fitPadding,

                    maxZoom: 17,
                },
            );

            return true;
        }

        /*
         * =========================================================
         * PUBLIC API
         * =========================================================
         */

        const api = {
            map,

            setRoute,
            setFleet,
            setRestrictions,

            setPoi,
      
            setElevation,

            setGroupVisible,

            fitData,

            loadFleet,
            startFleetPolling,
            stopFleetPolling,

            getVisibleGroups() {
                return {
                    ...visibleGroups,
                };
            },
        };

        /*
         * Global state API za layer panel.
         *
         * navigation.js kreira jednu MapLibre mapu,
         * a panel koristi ovaj objekat.
         */
        window.ProMap.MapEnhancementState = api;

        return api;
    }


    window.ProMap.MapEnhancements = {
        install,
    };
})(window);
