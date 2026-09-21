(function (window) {
    "use strict";

    window.ProMap = window.ProMap || {};

    const EMPTY = () => ({
        type: "FeatureCollection",
        features: []
    });

    const GROUPS = {
        route: [
            "route-alternative-casing",
            "route-alternative",
            "route-main-casing",
            "route-main"
        ],
        restrictions: [
            "route-restriction",
            "route-warning-point"
        ],
        traffic: ["traffic"],
        incidents: ["incident"],
        fleet: ["fleet-vehicles", "fleet-vehicle-label"],
        poi: ["truck-poi"],
        weather: ["weather"],
        elevation: ["elevation"]
    };

    const SOURCES = [
        "route-main",
        "route-alternative",
        "route-restrictions",
        "route-warnings",
        "fleet-vehicles",
        "truck-poi",
        "traffic",
        "incidents",
        "weather",
        "elevation"
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
                features: [value]
            };
        }

        if (value.type && value.coordinates) {
            return {
                type: "FeatureCollection",
                features: [{
                    type: "Feature",
                    geometry: value,
                    properties: {}
                }]
            };
        }

        if (Array.isArray(value)) {
            return {
                type: "FeatureCollection",
                features: value.filter(Boolean)
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
                    ...properties
                }
            };
        }

        if (!geometry.type || !geometry.coordinates) {
            return null;
        }

        return {
            type: "Feature",
            geometry,
            properties
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
                    ...properties
                }
            };
        }

        return geometryFeature(value, properties);
    }

    function routeStatusFrom(route) {
        if (route?.analysis?.restricted) {
            return "blocked";
        }

        const violations = route?.analysis?.violations;

        if (
            Array.isArray(violations) &&
            violations.length
        ) {
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

            distance:
                Number.isFinite(Number(route.distance))
                    ? Number(route.distance)
                    : null,

            duration:
                Number.isFinite(Number(route.duration))
                    ? Number(route.duration)
                    : null,

            routeId:
                route.id ?? null
        };

        if (
            Array.isArray(route.segments) &&
            route.segments.length
        ) {
            const features =
                route.segments
                    .map(
                        segment =>
                            geometryToFeature(
                                segment.geometry ??
                                segment.geojson,
                                {
                                    ...properties,

                                    status:
                                        segment.status ??
                                        properties.status,

                                    severity:
                                        segment.severity ??
                                        null,

                                    reason:
                                        segment.reason ??
                                        null
                                }
                            )
                    )
                    .filter(Boolean);

            if (features.length) {
                return {
                    type: "FeatureCollection",
                    features
                };
            }
        }

        const feature =
            geometryToFeature(
                route.geometry,
                properties
            );

        return feature
            ? {
                type: "FeatureCollection",
                features: [feature]
            }
            : EMPTY();
    }

    function routeAlternativeCollection(
        routes,
        selectedIndex
    ) {
        const features = [];

        (
            Array.isArray(routes)
                ? routes
                : []
        ).forEach(
            (route, index) => {
                if (index === selectedIndex) {
                    return;
                }

                const fc =
                    routeToFeatureCollection({
                        ...route,
                        analysis:
                            route.analysis || {}
                    });

                for (const feature of fc.features) {
                    feature.properties = {
                        ...(feature.properties || {}),

                        status:
                            "alternative",

                        routeIndex:
                            index,

                        opacity:
                            0.56
                    };

                    features.push(feature);
                }
            }
        );

        return {
            type: "FeatureCollection",
            features
        };
    }

    function violationCollection(violations) {
        const features = [];

        for (
            const item of
            Array.isArray(violations)
                ? violations
                : []
        ) {
            const props = {
                severity:
                    item.severity ||
                    (
                        item.blocked
                            ? "blocked"
                            : "warning"
                    ),

                type:
                    item.type ||
                    "restriction",

                name:
                    item.name ||
                    item.id ||
                    "Truck restriction",

                reason:
                    item.reason ||
                    "Aktivno ograničenje"
            };

            const feature =
                geometryToFeature(
                    item.geometry ||
                    item.geom ||
                    item.location ||
                    item.geojson,

                    props
                );

            if (feature) {
                features.push(feature);
                continue;
            }

            const lat =
                Number(
                    item.latitude ??
                    item.lat
                );

            const lon =
                Number(
                    item.longitude ??
                    item.lon
                );

            if (
                Number.isFinite(lat) &&
                Number.isFinite(lon)
            ) {
                features.push({
                    type: "Feature",

                    geometry: {
                        type: "Point",

                        coordinates: [
                            lon,
                            lat
                        ]
                    },

                    properties: props
                });
            }
        }

        return {
            type: "FeatureCollection",
            features
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
                Number.isFinite(
                    Number(coords[0])
                ) &&
                Number.isFinite(
                    Number(coords[1])
                )
            ) {
                const x =
                    Number(coords[0]);

                const y =
                    Number(coords[1]);

                minX =
                    Math.min(minX, x);

                minY =
                    Math.min(minY, y);

                maxX =
                    Math.max(maxX, x);

                maxY =
                    Math.max(maxY, y);

                return;
            }

            for (
                const item of coords
            ) {
                visit(item);
            }
        }

        for (
            const feature of
            fc.features || []
        ) {
            visit(
                feature?.geometry?.coordinates
            );
        }

        return Number.isFinite(minX)
            ? [
                [minX, minY],
                [maxX, maxY]
            ]
            : null;
    }

    function popupHtml(feature) {
        const properties =
            feature?.properties || {};

        const title =
            properties.name ||
            properties.label ||
            properties.id ||
            properties.type ||
            "ProMap";

        const lines =
            Object.entries(properties)
                .filter(
                    ([key, value]) =>
                        value !== null &&
                        value !== undefined &&
                        value !== "" &&
                        ![
                            "name",
                            "label",
                            "id",
                            "type"
                        ].includes(key)
                )
                .slice(0, 10)
                .map(
                    ([key, value]) =>
                        `<div>` +
                        `<strong>${escapeHtml(key)}</strong>` +
                        `: ${escapeHtml(value)}` +
                        `</div>`
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

    function addSourceIfNeeded(
        map,
        sourceId,
        data
    ) {
        const source =
            map.getSource(sourceId);

        if (source) {
            source.setData(
                asFeatureCollection(data)
            );

            return;
        }

        map.addSource(
            sourceId,
            {
                type: "geojson",
                data:
                    asFeatureCollection(data)
            }
        );
    }

    function setLayoutVisibility(
        map,
        layerId,
        visible
    ) {
        if (!map.getLayer(layerId)) {
            return;
        }

        map.setLayoutProperty(
            layerId,
            "visibility",
            visible
                ? "visible"
                : "none"
        );
    }

    function applyVisibility(
        map,
        visibleGroups
    ) {
        for (
            const [
                group,
                layerIds
            ] of Object.entries(GROUPS)
        ) {
            const visible =
                visibleGroups[group] !== false;

            for (
                const layerId of layerIds
            ) {
                setLayoutVisibility(
                    map,
                    layerId,
                    visible
                );
            }
        }
    }

    function setDataSafe(
        map,
        sourceId,
        data
    ) {
        const source =
            map.getSource(sourceId);

        if (!source) {
            return;
        }

        source.setData(
            asFeatureCollection(data)
        );
    }

    function buildFleetCollection(
        vehicles,
        trips
    ) {
        const features = [];

        const vehicleRows =
            Array.isArray(vehicles)
                ? vehicles
                : [];

        const tripRows =
            Array.isArray(trips)
                ? trips
                : [];

        const tripByVehicle =
            new Map();

        for (
            const trip of tripRows
        ) {
            if (
                trip?.VehicleId ||
                trip?.vehicleId
            ) {
                tripByVehicle.set(
                    String(
                        trip.VehicleId ??
                        trip.vehicleId
                    ),
                    trip
                );
            }
        }

        for (
            const vehicle of vehicleRows
        ) {
            const lat =
                Number(
                    vehicle.currentLatitude ??
                    vehicle.CurrentLatitude
                );

            const lon =
                Number(
                    vehicle.currentLongitude ??
                    vehicle.CurrentLongitude
                );

            if (
                !Number.isFinite(lat) ||
                !Number.isFinite(lon)
            ) {
                continue;
            }

            const trip =
                tripByVehicle.get(
                    String(
                        vehicle.id ??
                        vehicle.Id
                    )
                );

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
                vehicle.registration ??
                vehicle.Registration ??
                "TRUCK";

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

                    coordinates: [
                        lon,
                        lat
                    ]
                },

                properties: {
                    id:
                        vehicle.id ??
                        vehicle.Id ??
                        null,

                    registration,

                    status,

                    speedKmh:
                        speed,

                    bearing,

                    type:
                        "fleet"
                }
            });
        }

        return {
            type: "FeatureCollection",
            features
        };
    }

    async function fetchJson(
        url
    ) {
        const response =
            await fetch(
                url,
                {
                    headers: {
                        Accept:
                            "application/json"
                    },

                    credentials:
                        "same-origin"
                }
            );

        if (!response.ok) {
            throw new Error(
                `${url} HTTP ${response.status}`
            );
        }

        return response.json();
    }

    function install(
        map,
        options = {}
    ) {
        if (
            !map ||
            typeof map.addSource !==
            "function"
        ) {
            throw new Error(
                "ProMap.MapEnhancements.install zahteva MapLibre map instancu."
            );
        }

        const opts = {
            fitPadding:
                options.fitPadding ?? 60,

            popup:
                options.popup !== false,

            visibleGroups: {
                route: true,
                restrictions: true,
                traffic: false,
                incidents: false,
                fleet: false,
                poi: false,
                weather: false,
                elevation: false,

                ...(options.visibleGroups || {})
            }
        };

        for (
            const sourceId of SOURCES
        ) {
            if (
                !map.getSource(
                    sourceId
                )
            ) {
                map.addSource(
                    sourceId,
                    {
                        type: "geojson",
                        data: EMPTY()
                    }
                );
            }
        }

        applyVisibility(
            map,
            opts.visibleGroups
        );

        if (
            opts.popup &&
            !map.__promapEnhancementPopupBound
        ) {
            map.__promapEnhancementPopupBound =
                true;

            const popupLayerIds = [
                "route-warning-point",
                "incident",
                "truck-poi",
                "fleet-vehicles",
                "weather"
            ].filter(
                id =>
                    map.getLayer(id)
            );

            if (popupLayerIds.length) {
                map.on(
                    "click",
                    popupLayerIds,
                    event => {
                        const feature =
                            event.features?.[0];

                        if (!feature) {
                            return;
                        }

                        new map.constructor.Popup({
                            offset: 12
                        })
                            .setLngLat(
                                event.lngLat
                            )
                            .setHTML(
                                popupHtml(
                                    feature
                                )
                            )
                            .addTo(map);
                    }
                );
            }
        }

        async function loadFleet() {
            try {
                const [
                    vehicles,
                    trips
                ] =
                    await Promise.all([
                        fetchJson(
                            "/api/business/vehicles"
                        ),

                        fetchJson(
                            "/api/business/trips"
                        )
                    ]);

                setDataSafe(
                    map,
                    "fleet-vehicles",
                    buildFleetCollection(
                        vehicles,
                        trips
                    )
                );

                return {
                    vehicles,
                    trips
                };
            } catch (error) {
                console.warn(
                    "[ProMap] Fleet load failed:",
                    error
                );

                return null;
            }
        }

        let fleetTimer = null;

        function startFleetPolling(
            config = {}
        ) {
            stopFleetPolling();

            const intervalMs =
                Math.max(
                    5000,
                    Number(
                        config.intervalMs ||
                        15000
                    )
                );

            void loadFleet();

            fleetTimer =
                window.setInterval(
                    () => {
                        void loadFleet();
                    },
                    intervalMs
                );
        }

        function stopFleetPolling() {
            if (
                fleetTimer !== null
            ) {
                window.clearInterval(
                    fleetTimer
                );

                fleetTimer = null;
            }
        }

        function setRoute(
            response,
            selectedIndex = 0
        ) {
            const routes =
                Array.isArray(
                    response?.routes
                )
                    ? response.routes
                    : [];

            const selectedRoute =
                routes[selectedIndex] ||
                routes[0] ||
                null;

            const alternative =
                routeAlternativeCollection(
                    routes,
                    selectedIndex
                );

            const restrictions =
                violationCollection(
                    selectedRoute
                        ?.analysis
                        ?.violations ??
                    response?.violations ??
                    []
                );

            setDataSafe(
                map,
                "route-main",
                routeToFeatureCollection(
                    selectedRoute
                )
            );

            setDataSafe(
                map,
                "route-alternative",
                alternative
            );

            setDataSafe(
                map,
                "route-restrictions",
                restrictions
            );

            setDataSafe(
                map,
                "route-warnings",
                restrictions
            );
        }

        function setFleet(
            vehicles,
            trips
        ) {
            setDataSafe(
                map,
                "fleet-vehicles",
                buildFleetCollection(
                    vehicles,
                    trips
                )
            );
        }

        function setRestrictions(
            violations
        ) {
            const data =
                violationCollection(
                    violations
                );

            setDataSafe(
                map,
                "route-restrictions",
                data
            );

            setDataSafe(
                map,
                "route-warnings",
                data
            );
        }

        function setPoi(
            value
        ) {
            setDataSafe(
                map,
                "truck-poi",
                value
            );
        }

        function setTraffic(
            value
        ) {
            setDataSafe(
                map,
                "traffic",
                value
            );
        }

        function setIncidents(
            value
        ) {
            setDataSafe(
                map,
                "incidents",
                value
            );
        }

        function setWeather(
            value
        ) {
            setDataSafe(
                map,
                "weather",
                value
            );
        }

        function setElevation(
            value
        ) {
            setDataSafe(
                map,
                "elevation",
                value
            );
        }

        function setGroupVisible(
            group,
            visible
        ) {
            const ids =
                GROUPS[group] || [];

            for (
                const layerId of ids
            ) {
                setLayoutVisibility(
                    map,
                    layerId,
                    visible
                );
            }
        }

        function fitData(
            value
        ) {
            const collection =
                asFeatureCollection(
                    value
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
                    maxZoom: 17
                }
            );

            return true;
        }

        return {
            map,

            setRoute,
            setFleet,
            setRestrictions,

            setPoi,
            setTraffic,
            setIncidents,
            setWeather,
            setElevation,

            setGroupVisible,

            fitData,

            loadFleet,
            startFleetPolling,
            stopFleetPolling
        };
    }

    window.ProMap.MapEnhancements = {
        install
    };

})(window);