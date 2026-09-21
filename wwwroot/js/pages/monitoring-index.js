"use strict";

(() => {

    const page = document.querySelector(
        '[data-module="monitoring"]'
    );

    if (!page) {
        return;
    }

    const $ = (id) => document.getElementById(id);

    const state = {
        map: null,
        currentBasemapArchive: "/maps/europe.pmtiles",
        markers: new Map(),
        vehicles: [],
        selectedVehicleId: null,
        filter: "all",
        signalRConnection: null,
        signalRStarted: false,
        demoMode: false,
        demoTimer: null,
        lastUpdated: null
    };

    const DEFAULT_CENTER = [44.8125, 20.4612];

    const DEMO_VEHICLES = [
        {
            id: "demo-001",
            registration: "BG 123-AA",
            name: "Mercedes Actros 01",
            driver: "Marko Petrović",
            status: "moving",
            latitude: 44.8125,
            longitude: 20.4612,
            speed: 64,
            heading: 35,
            accuracy: 7,
            trip: "TR-2026-0041",
            destination: "Novi Sad",
            lastGpsAt: new Date().toISOString()
        },
        {
            id: "demo-002",
            registration: "NS 742-BB",
            name: "Volvo FH 02",
            driver: "Nikola Jovanović",
            status: "moving",
            latitude: 45.2671,
            longitude: 19.8335,
            speed: 52,
            heading: 190,
            accuracy: 8,
            trip: "TR-2026-0042",
            destination: "Beograd",
            lastGpsAt: new Date().toISOString()
        },
        {
            id: "demo-003",
            registration: "NI 551-CC",
            name: "Scania R 03",
            driver: "Milan Stojanović",
            status: "idle",
            latitude: 43.3209,
            longitude: 21.8958,
            speed: 0,
            heading: 0,
            accuracy: 12,
            trip: "TR-2026-0038",
            destination: "Niš",
            lastGpsAt: new Date().toISOString()
        },
        {
            id: "demo-004",
            registration: "SU 913-DD",
            name: "DAF XF 04",
            driver: "Petar Ilić",
            status: "moving",
            latitude: 46.1005,
            longitude: 19.6676,
            speed: 71,
            heading: 210,
            accuracy: 9,
            trip: "TR-2026-0044",
            destination: "Subotica",
            lastGpsAt: new Date().toISOString()
        },
        {
            id: "demo-005",
            registration: "KG 284-EE",
            name: "MAN TGX 05",
            driver: "Stefan Nikolić",
            status: "offline",
            latitude: 43.999,
            longitude: 20.9114,
            speed: 0,
            heading: 0,
            accuracy: null,
            trip: "—",
            destination: "—",
            lastGpsAt: new Date(Date.now() - 1000 * 60 * 45).toISOString()
        }
    ];

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function numberOrNull(value) {
        const n = Number(value);

        return Number.isFinite(n)
            ? n
            : null;
    }

    function firstValue(object, keys) {

        for (const key of keys) {

            if (
                object &&
                object[key] !== undefined &&
                object[key] !== null &&
                object[key] !== ""
            ) {
                return object[key];
            }
        }

        return null;
    }

    function normalizeCoordinate(item) {

        if (!item) {
            return null;
        }

        const latitude = numberOrNull(
            firstValue(
                item,
                [
                    "latitude",
                    "Latitude",
                    "lat",
                    "Lat",
                    "currentLatitude",
                    "CurrentLatitude"
                ]
            )
        );

        const longitude = numberOrNull(
            firstValue(
                item,
                [
                    "longitude",
                    "Longitude",
                    "lon",
                    "Lon",
                    "lng",
                    "currentLongitude",
                    "CurrentLongitude"
                ]
            )
        );

        if (
            latitude === null ||
            longitude === null ||
            latitude < -90 ||
            latitude > 90 ||
            longitude < -180 ||
            longitude > 180
        ) {
            return null;
        }

        return {
            latitude,
            longitude
        };
    }

    function normalizeVehicle(item, index) {

        const point = normalizeCoordinate(item);

        if (!point) {
            return null;
        }

        const speed = numberOrNull(
            firstValue(
                item,
                [
                    "speed",
                    "Speed",
                    "speedKmh",
                    "SpeedKmh",
                    "currentSpeedKmh",
                    "CurrentSpeedKmh"
                ]
            )
        ) ?? 0;

        let status = String(
            firstValue(
                item,
                [
                    "status",
                    "Status",
                    "vehicleStatus",
                    "VehicleStatus"
                ]
            ) || ""
        ).toLowerCase();

        if (
            status.includes("offline") ||
            status.includes("disconnected")
        ) {
            status = "offline";
        }
        else if (
            status.includes("idle") ||
            status.includes("stopped") ||
            status.includes("stationary")
        ) {
            status = "idle";
        }
        else if (
            speed > 5 ||
            status.includes("moving") ||
            status.includes("driving") ||
            status.includes("active")
        ) {
            status = "moving";
        }
        else {
            status = "idle";
        }

        return {
            id: String(
                firstValue(
                    item,
                    [
                        "id",
                        "Id",
                        "vehicleId",
                        "VehicleId"
                    ]
                ) ?? `vehicle-${index}`
            ),

            registration: String(
                firstValue(
                    item,
                    [
                        "registration",
                        "Registration",
                        "registrationNumber",
                        "RegistrationNumber",
                        "plate",
                        "Plate"
                    ]
                ) || `VEH-${index + 1}`
            ),

            name: String(
                firstValue(
                    item,
                    [
                        "name",
                        "Name",
                        "vehicleName",
                        "VehicleName",
                        "model",
                        "Model"
                    ]
                ) || "Teretno vozilo"
            ),

            driver: String(
                firstValue(
                    item,
                    [
                        "driverName",
                        "DriverName",
                        "driver",
                        "Driver",
                        "currentDriver",
                        "CurrentDriver"
                    ]
                ) || "Vozač nije dodeljen"
            ),

            status,

            latitude: point.latitude,
            longitude: point.longitude,

            speed,

            heading:
                numberOrNull(
                    firstValue(
                        item,
                        [
                            "heading",
                            "Heading",
                            "bearing",
                            "Bearing",
                            "currentBearing",
                            "CurrentBearing"
                        ]
                    )
                ) ?? 0,

            accuracy:
                numberOrNull(
                    firstValue(
                        item,
                        [
                            "accuracy",
                            "Accuracy",
                            "gpsAccuracy",
                            "GpsAccuracy"
                        ]
                    )
                ),

            trip: String(
                firstValue(
                    item,
                    [
                        "tripNumber",
                        "TripNumber",
                        "tripCode",
                        "TripCode",
                        "tripId",
                        "TripId",
                        "currentTrip",
                        "CurrentTrip"
                    ]
                ) || "—"
            ),

            destination: String(
                firstValue(
                    item,
                    [
                        "destination",
                        "Destination",
                        "destinationName",
                        "DestinationName"
                    ]
                ) || "—"
            ),

            lastGpsAt:
                firstValue(
                    item,
                    [
                        "lastGpsAt",
                        "LastGpsAt",
                        "lastPositionAt",
                        "LastPositionAt",
                        "timestamp",
                        "Timestamp"
                    ]
                ) || null,

            raw: item
        };
    }

    function formatSpeed(value) {

        const speed = numberOrNull(value) ?? 0;

        return Math.round(speed);
    }

    function formatAccuracy(value) {

        const accuracy = numberOrNull(value);

        if (accuracy === null) {
            return "—";
        }

        return `${Math.round(accuracy)} m`;
    }

    function formatTime(value) {

        if (!value) {
            return "—";
        }

        const date = new Date(value);

        if (Number.isNaN(date.getTime())) {
            return "—";
        }

        return date.toLocaleTimeString(
            "sr-RS",
            {
                hour: "2-digit",
                minute: "2-digit",
                second: "2-digit"
            }
        );
    }

    function timeAgo(value) {

        if (!value) {
            return "Nema GPS podatka";
        }

        const timestamp = new Date(value).getTime();

        if (!Number.isFinite(timestamp)) {
            return "Nema GPS podatka";
        }

        const seconds = Math.max(
            0,
            Math.round(
                (Date.now() - timestamp) / 1000
            )
        );

        if (seconds < 10) {
            return "upravo sada";
        }

        if (seconds < 60) {
            return `pre ${seconds}s`;
        }

        const minutes = Math.round(seconds / 60);

        if (minutes < 60) {
            return `pre ${minutes} min`;
        }

        const hours = Math.round(minutes / 60);

        return `pre ${hours} h`;
    }

    function statusLabel(status) {

        switch (status) {

            case "moving":
                return "U vožnji";

            case "idle":
                return "Zaustavljeno";

            case "offline":
                return "Offline";

            default:
                return "Nepoznato";
        }
    }

    function statusClass(status) {

        if (
            status === "idle" ||
            status === "offline"
        ) {
            return status;
        }

        return "";
    }

    function setText(id, value) {

        const element = $(id);

        if (element) {
            element.textContent = value;
        }
    }

    function showToast(message) {

        const toast = $("monitoringToast");

        if (!toast) {
            return;
        }

        toast.textContent = message;

        toast.classList.add("show");

        clearTimeout(
            showToast.timer
        );

        showToast.timer = setTimeout(
            () => {
                toast.classList.remove("show");
            },
            2800
        );
    }

    function animateCounter(
        element,
        target
    ) {

        if (!element) {
            return;
        }

        const finalValue = Number(target) || 0;
        const start = Number(element.dataset.value || 0);
        const duration = 450;
        const startTime = performance.now();

        function tick(now) {

            const progress = Math.min(
                1,
                (now - startTime) / duration
            );

            const eased =
                1 -
                Math.pow(
                    1 - progress,
                    3
                );

            const value = Math.round(
                start +
                (finalValue - start) *
                eased
            );

            element.textContent = value;

            if (progress < 1) {
                requestAnimationFrame(tick);
            }
            else {
                element.dataset.value = String(
                    finalValue
                );
            }
        }

        requestAnimationFrame(tick);
    }

    function updateKpis() {

        const total =
            state.vehicles.length;

        const online =
            state.vehicles.filter(
                vehicle =>
                    vehicle.status !== "offline"
            ).length;

        const moving =
            state.vehicles.filter(
                vehicle =>
                    vehicle.status === "moving"
            ).length;

        const idle =
            state.vehicles.filter(
                vehicle =>
                    vehicle.status === "idle"
            ).length;

        const alerts =
            state.vehicles.filter(
                vehicle =>
                    vehicle.status === "offline"
            ).length;

        animateCounter(
            $("kpiTotal"),
            total
        );

        animateCounter(
            $("kpiOnline"),
            online
        );

        animateCounter(
            $("kpiMoving"),
            moving
        );

        animateCounter(
            $("kpiIdle"),
            idle
        );

        animateCounter(
            $("kpiAlerts"),
            alerts
        );

        setText(
            "mapVehicleCount",
            `${total} vozila`
        );

        setText(
            "alertCount",
            String(alerts)
        );
    }

    function createVehicleIcon(vehicle) {

        const status =
            statusClass(
                vehicle.status
            );

        const direction =
            Number.isFinite(
                vehicle.heading
            )
                ? vehicle.heading
                : 0;

        return L.divIcon({
            className: "",
            html: `
                <div
                    class="pm-monitor-vehicle-marker ${escapeHtml(status)}"
                    style="transform: rotate(${direction}deg);">
                    <div class="pm-monitor-vehicle-marker-core">
                        🚚
                    </div>
                    <div
                        class="pm-monitor-vehicle-marker-label"
                        style="transform: rotate(${-direction}deg);">
                        ${escapeHtml(vehicle.registration)}
                    </div>
                </div>
            `,
            iconSize: [38, 38],
            iconAnchor: [19, 19]
        });
    }

    function initializeMap() {

        if (
            typeof L === "undefined"
        ) {
            showToast(
                "Leaflet nije učitan."
            );

            return;
        }

        state.map =
            L.map(
                "monitoringMap",
                {
                    zoomControl: true,
                    preferCanvas: true,
                    minZoom: 3,
                    maxZoom: 22,
                    zoomSnap: 0.25,
                    zoomDelta: 0.5,
                    wheelPxPerZoomLevel: 90,
                    attributionControl: true,
                    scrollWheelZoom: true,
                    dragging: true,
                    doubleClickZoom: true,
                    boxZoom: true,
                    keyboard: true,
                    touchZoom: true,
                    tapHold: true
                }
            ).setView(
                DEFAULT_CENTER,
                7
            );

        L.control.scale({
            imperial: false,
            position: "bottomleft"
        }).addTo(state.map);

        void window.ProMap.MapLayers
            .attach(
                state.map,
                {
                    archiveUrl:
                        state.currentBasemapArchive
                }
            )
            .catch(console.error);

        setTimeout(
            () => {
                state.map.invalidateSize();
            },
            200
        );
    }

    async function toggleMapLayer() {

        if (!state.map) {
            return;
        }

        state.currentBasemapArchive =
            state.currentBasemapArchive === "/maps/europe.pmtiles"
                ? "/maps/serbia.pmtiles"
                : "/maps/europe.pmtiles";

        try {
            await window.ProMap.MapLayers.setArchive(
                state.map,
                state.currentBasemapArchive
            );

            setText(
                "toggleDarkMap",
                state.currentBasemapArchive === "/maps/europe.pmtiles"
                    ? "Evropa mapa"
                    : "Srbija mapa"
            );
        } catch (error) {
            state.currentBasemapArchive =
                state.currentBasemapArchive === "/maps/europe.pmtiles"
                    ? "/maps/serbia.pmtiles"
                    : "/maps/europe.pmtiles";

            console.error(error);
        }
    }

    function haversineDistance(
        lat1,
        lon1,
        lat2,
        lon2
    ) {

        const earthRadius = 6371000;

        const toRad =
            value =>
                value *
                Math.PI /
                180;

        const dLat =
            toRad(
                lat2 - lat1
            );

        const dLon =
            toRad(
                lon2 - lon1
            );

        const a =
            Math.sin(dLat / 2) *
            Math.sin(dLat / 2) +
            Math.cos(
                toRad(lat1)
            ) *
            Math.cos(
                toRad(lat2)
            ) *
            Math.sin(dLon / 2) *
            Math.sin(dLon / 2);

        return (
            earthRadius *
            2 *
            Math.atan2(
                Math.sqrt(a),
                Math.sqrt(1 - a)
            )
        );
    }

    function animateMarkerTo(
        marker,
        targetLatLng,
        duration = 900
    ) {

        if (!marker) {
            return;
        }

        const start =
            marker.getLatLng();

        const end =
            L.latLng(
                targetLatLng[0],
                targetLatLng[1]
            );

        const started =
            performance.now();

        function animate(now) {

            const progress =
                Math.min(
                    1,
                    (now - started) /
                    duration
                );

            const eased =
                1 -
                Math.pow(
                    1 - progress,
                    3
                );

            const latitude =
                start.lat +
                (end.lat - start.lat) *
                eased;

            const longitude =
                start.lng +
                (end.lng - start.lng) *
                eased;

            marker.setLatLng(
                [
                    latitude,
                    longitude
                ]
            );

            if (
                progress < 1
            ) {
                requestAnimationFrame(
                    animate
                );
            }
        }

        requestAnimationFrame(
            animate
        );
    }

    function updateMarkers() {

        if (!state.map) {
            return;
        }

        const visibleIds =
            new Set(
                state.vehicles.map(
                    vehicle =>
                        vehicle.id
                )
            );

        for (
            const [
                id,
                marker
            ]
            of state.markers
        ) {

            if (
                !visibleIds.has(id)
            ) {
                marker.remove();

                state.markers.delete(
                    id
                );
            }
        }

        for (
            const vehicle
            of state.vehicles
        ) {

            const latLng = [
                vehicle.latitude,
                vehicle.longitude
            ];

            let marker =
                state.markers.get(
                    vehicle.id
                );

            if (!marker) {

                marker =
                    L.marker(
                        latLng,
                        {
                            icon:
                                createVehicleIcon(
                                    vehicle
                                ),
                            zIndexOffset:
                                vehicle.id ===
                                state.selectedVehicleId
                                    ? 1000
                                    : 100
                        }
                    );

                marker.addTo(
                    state.map
                );

                marker.on(
                    "click",
                    () => {
                        selectVehicle(
                            vehicle.id,
                            true
                        );
                    }
                );

                state.markers.set(
                    vehicle.id,
                    marker
                );
            }
            else {

                animateMarkerTo(
                    marker,
                    latLng
                );

                marker.setIcon(
                    createVehicleIcon(
                        vehicle
                    )
                );

                marker.setZIndexOffset(
                    vehicle.id ===
                    state.selectedVehicleId
                        ? 1000
                        : 100
                );
            }

            marker.bindTooltip(
                `
                    <strong>
                        ${escapeHtml(vehicle.registration)}
                    </strong><br>
                    ${escapeHtml(vehicle.name)}<br>
                    ${formatSpeed(vehicle.speed)} km/h
                `,
                {
                    direction: "top",
                    offset: [0, -14]
                }
            );
        }
    }

    function getFilteredVehicles() {

        if (
            state.filter === "all"
        ) {
            return [
                ...state.vehicles
            ];
        }

        return state.vehicles.filter(
            vehicle => {

                if (
                    state.filter ===
                    "moving"
                ) {
                    return vehicle.status ===
                        "moving";
                }

                if (
                    state.filter ===
                    "idle"
                ) {
                    return vehicle.status ===
                        "idle";
                }

                if (
                    state.filter ===
                    "offline"
                ) {
                    return vehicle.status ===
                        "offline";
                }

                return true;
            }
        );
    }

    function renderVehicleList() {

        const list =
            $("monitoringVehicleList");

        if (!list) {
            return;
        }

        const vehicles =
            getFilteredVehicles();

        list.innerHTML = "";

        if (
            vehicles.length === 0
        ) {

            list.innerHTML = `
                <div class="monitoring-empty">
                    Nema vozila za izabrani filter.
                </div>
            `;

            return;
        }

        vehicles.forEach(
            (
                vehicle,
                index
            ) => {

                const row =
                    document.createElement(
                        "div"
                    );

                row.className =
                    `monitoring-vehicle ${statusClass(vehicle.status)} ${
                        vehicle.id ===
                        state.selectedVehicleId
                            ? "selected"
                            : ""
                    }`;

                row.style.animationDelay =
                    `${index * 35}ms`;

                row.innerHTML = `
                    <div class="monitoring-vehicle-icon">
                        🚚
                    </div>

                    <div class="monitoring-vehicle-main">
                        <div class="monitoring-vehicle-name">
                            ${escapeHtml(vehicle.registration)}
                        </div>

                        <div class="monitoring-vehicle-driver">
                            ${escapeHtml(vehicle.driver)}
                        </div>
                    </div>

                    <div class="monitoring-vehicle-right">
                        <div class="monitoring-speed">
                            ${formatSpeed(vehicle.speed)}
                            <small>km/h</small>
                        </div>

                        <div class="monitoring-status-text">
                            ${statusLabel(vehicle.status)}
                        </div>
                    </div>
                `;

                row.addEventListener(
                    "click",
                    () => {
                        selectVehicle(
                            vehicle.id,
                            true
                        );
                    }
                );

                list.appendChild(
                    row
                );
            }
        );
    }

    function renderVehicleDetail(
        vehicle
    ) {

        const box =
            $("vehicleDetail");

        if (!box) {
            return;
        }

        if (!vehicle) {

            box.innerHTML = `
                <div class="monitoring-detail-empty">
                    <div>
                        <div class="monitoring-detail-empty-icon">
                            ⌖
                        </div>
                        Izaberi vozilo na mapi ili iz fleet liste
                        da prikažeš detaljnu telemetriju.
                    </div>
                </div>
            `;

            return;
        }

        box.innerHTML = `

            <div class="monitoring-detail-top">

                <div class="monitoring-detail-icon">
                    🚚
                </div>

                <div>
                    <div class="monitoring-detail-name">
                        ${escapeHtml(vehicle.registration)}
                    </div>

                    <div class="monitoring-detail-status">
                        ${statusLabel(vehicle.status)}
                    </div>
                </div>

            </div>

            <div class="monitoring-detail-grid">

                <div class="monitoring-detail-stat">
                    <div class="monitoring-detail-label">
                        Vozilo
                    </div>
                    <div class="monitoring-detail-value">
                        ${escapeHtml(vehicle.name)}
                    </div>
                </div>

                <div class="monitoring-detail-stat">
                    <div class="monitoring-detail-label">
                        Vozač
                    </div>
                    <div class="monitoring-detail-value">
                        ${escapeHtml(vehicle.driver)}
                    </div>
                </div>

                <div class="monitoring-detail-stat">
                    <div class="monitoring-detail-label">
                        Brzina
                    </div>
                    <div class="monitoring-detail-value">
                        ${formatSpeed(vehicle.speed)} km/h
                    </div>
                </div>

                <div class="monitoring-detail-stat">
                    <div class="monitoring-detail-label">
                        GPS accuracy
                    </div>
                    <div class="monitoring-detail-value">
                        ${formatAccuracy(vehicle.accuracy)}
                    </div>
                </div>

                <div class="monitoring-detail-stat">
                    <div class="monitoring-detail-label">
                        Tura
                    </div>
                    <div class="monitoring-detail-value">
                        ${escapeHtml(vehicle.trip)}
                    </div>
                </div>

                <div class="monitoring-detail-stat">
                    <div class="monitoring-detail-label">
                        Poslednji GPS
                    </div>
                    <div class="monitoring-detail-value">
                        ${timeAgo(vehicle.lastGpsAt)}
                    </div>
                </div>

            </div>
        `;

        setText(
            "detailUpdatedAt",
            formatTime(
                vehicle.lastGpsAt
            )
        );
    }

    function renderAlerts() {

        const box =
            $("monitoringAlerts");

        if (!box) {
            return;
        }

        const offline =
            state.vehicles.filter(
                vehicle =>
                    vehicle.status ===
                    "offline"
            );

        box.innerHTML = "";

        if (
            offline.length === 0
        ) {

            box.innerHTML = `
                <div class="monitoring-empty">
                    Nema aktivnih operativnih upozorenja.
                </div>
            `;

            return;
        }

        offline.forEach(
            (
                vehicle,
                index
            ) => {

                const alert =
                    document.createElement(
                        "div"
                    );

                alert.className =
                    "monitoring-alert";

                alert.style.animationDelay =
                    `${index * 70}ms`;

                alert.innerHTML = `
                    <div class="monitoring-alert-icon">
                        !
                    </div>

                    <div>
                        <div class="monitoring-alert-title">
                            GPS signal nije aktivan ·
                            ${escapeHtml(vehicle.registration)}
                        </div>

                        <div class="monitoring-alert-meta">
                            Poslednji signal:
                            ${timeAgo(vehicle.lastGpsAt)}
                        </div>
                    </div>
                `;

                alert.addEventListener(
                    "click",
                    () => {
                        selectVehicle(
                            vehicle.id,
                            true
                        );
                    }
                );

                box.appendChild(
                    alert
                );
            }
        );
    }

    function selectVehicle(
        vehicleId,
        flyTo
    ) {

        const vehicle =
            state.vehicles.find(
                item =>
                    item.id ===
                    vehicleId
            );

        if (!vehicle) {
            return;
        }

        state.selectedVehicleId =
            vehicle.id;

        renderVehicleList();

        renderVehicleDetail(
            vehicle
        );

        updateMarkers();

        if (
            flyTo &&
            state.map
        ) {

            state.map.flyTo(
                [
                    vehicle.latitude,
                    vehicle.longitude
                ],
                Math.max(
                    state.map.getZoom(),
                    14
                ),
                {
                    duration: .8
                }
            );

            const marker =
                state.markers.get(
                    vehicle.id
                );

            if (marker) {
                setTimeout(
                    () => {
                        marker.openTooltip();
                    },
                    500
                );
            }
        }
    }

    function fitFleet() {

        if (
            !state.map ||
            state.vehicles.length === 0
        ) {
            return;
        }

        const points =
            state.vehicles
                .filter(
                    vehicle =>
                        Number.isFinite(
                            vehicle.latitude
                        ) &&
                        Number.isFinite(
                            vehicle.longitude
                        )
                )
                .map(
                    vehicle => [
                        vehicle.latitude,
                        vehicle.longitude
                    ]
                );

        if (
            points.length === 0
        ) {
            return;
        }

        if (
            points.length === 1
        ) {

            state.map.flyTo(
                points[0],
                14,
                {
                    duration: .7
                }
            );

            return;
        }

        state.map.fitBounds(
            L.latLngBounds(
                points
            ),
            {
                padding: [
                    60,
                    60
                ],
                maxZoom: 13,
                animate: true,
                duration: .8
            }
        );
    }

    function centerSerbia() {

        if (!state.map) {
            return;
        }

        state.map.flyTo(
            DEFAULT_CENTER,
            7,
            {
                duration: .8
            }
        );
    }

    function setVehicles(
        vehicles,
        source = "api"
    ) {

        const normalized =
            vehicles
                .map(
                    (
                        item,
                        index
                    ) =>
                        normalizeVehicle(
                            item,
                            index
                        )
                )
                .filter(
                    Boolean
                );

        if (
            normalized.length === 0
        ) {
            return false;
        }

        state.vehicles =
            normalized;

        state.lastUpdated =
            new Date();

        state.demoMode =
            source === "demo";

        updateKpis();

        renderVehicleList();

        renderAlerts();

        updateMarkers();

        setText(
            "fleetUpdatedAt",
            formatTime(
                state.lastUpdated
            )
        );

        return true;
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
                    cache:
                        "no-store"
                }
            );

        if (
            !response.ok
        ) {
            throw new Error(
                `HTTP ${response.status}`
            );
        }

        return await response.json();
    }

    function extractArray(
        payload
    ) {

        if (
            Array.isArray(payload)
        ) {
            return payload;
        }

        if (
            Array.isArray(
                payload?.vehicles
            )
        ) {
            return payload.vehicles;
        }

        if (
            Array.isArray(
                payload?.items
            )
        ) {
            return payload.items;
        }

        if (
            Array.isArray(
                payload?.data
            )
        ) {
            return payload.data;
        }

        if (
            Array.isArray(
                payload?.result
            )
        ) {
            return payload.result;
        }

        return [];
    }

    async function loadFleetFromApi() {

        const urls = [
            "/api/business/vehicles",
            "/api/vehicles"
        ];

        let lastError =
            null;

        for (
            const url
            of urls
        ) {

            try {

                const payload =
                    await fetchJson(
                        url
                    );

                const array =
                    extractArray(
                        payload
                    );

                if (
                    setVehicles(
                        array,
                        "api"
                    )
                ) {
                    return true;
                }

            }
            catch (error) {

                lastError =
                    error;
            }
        }

        if (lastError) {
            console.warn(
                "Monitoring fleet API nije dostupan:",
                lastError
            );
        }

        return false;
    }

    function startDemoMode() {

        if (
            state.demoTimer
        ) {
            clearInterval(
                state.demoTimer
            );
        }

        setVehicles(
            DEMO_VEHICLES,
            "demo"
        );

        showToast(
            "Monitoring API trenutno nije dostupan — prikazan je lokalni telemetry preview."
        );

        state.demoTimer =
            setInterval(
                () => {

                    const now =
                        Date.now();

                    state.vehicles =
                        state.vehicles.map(
                            vehicle => {

                                if (
                                    vehicle.status !==
                                    "moving"
                                ) {
                                    return {
                                        ...vehicle,
                                        lastGpsAt:
                                            vehicle.status ===
                                            "idle"
                                                ? new Date(now).toISOString()
                                                : vehicle.lastGpsAt
                                    };
                                }

                                const headingRad =
                                    (
                                        vehicle.heading ||
                                        0
                                    ) *
                                    Math.PI /
                                    180;

                                const latitudeDelta =
                                    Math.cos(
                                        headingRad
                                    ) *
                                    .00055;

                                const longitudeDelta =
                                    Math.sin(
                                        headingRad
                                    ) *
                                    .00055 /
                                    Math.max(
                                        Math.cos(
                                            vehicle.latitude *
                                            Math.PI /
                                            180
                                        ),
                                        .25
                                    );

                                let latitude =
                                    vehicle.latitude +
                                    latitudeDelta;

                                let longitude =
                                    vehicle.longitude +
                                    longitudeDelta;

                                if (
                                    latitude < 42 ||
                                    latitude > 47
                                ) {
                                    latitude =
                                        vehicle.latitude -
                                        latitudeDelta;

                                    vehicle.heading =
                                        (
                                            vehicle.heading +
                                            180
                                        ) %
                                        360;
                                }

                                if (
                                    longitude < 18 ||
                                    longitude > 23
                                ) {
                                    longitude =
                                        vehicle.longitude -
                                        longitudeDelta;

                                    vehicle.heading =
                                        (
                                            vehicle.heading +
                                            180
                                        ) %
                                        360;
                                }

                                return {
                                    ...vehicle,
                                    latitude,
                                    longitude,
                                    speed:
                                        Math.max(
                                            35,
                                            Math.min(
                                                85,
                                                vehicle.speed +
                                                (
                                                    Math.random() -
                                                    .5
                                                ) *
                                                6
                                            )
                                        ),
                                    lastGpsAt:
                                        new Date(
                                            now
                                        ).toISOString()
                                };
                            }
                        );

                    state.lastUpdated =
                        new Date();

                    updateKpis();

                    renderVehicleList();

                    renderAlerts();

                    updateMarkers();

                    setText(
                        "fleetUpdatedAt",
                        formatTime(
                            state.lastUpdated
                        )
                    );

                    if (
                        state.selectedVehicleId
                    ) {

                        const selected =
                            state.vehicles.find(
                                vehicle =>
                                    vehicle.id ===
                                    state.selectedVehicleId
                            );

                        if (selected) {
                            renderVehicleDetail(
                                selected
                            );
                        }
                    }

                },
                2500
            );
    }

    function setLoading(
        value
    ) {

        const loading =
            $("monitoringLoading");

        if (!loading) {
            return;
        }

        loading.classList.toggle(
            "hidden",
            !value
        );
    }

    async function refreshMonitoring() {

        setLoading(true);

        const success =
            await loadFleetFromApi();

        if (!success) {

            if (
                state.vehicles.length === 0
            ) {
                startDemoMode();
            }
        }

        setLoading(false);

        if (success) {
            showToast(
                "Fleet monitoring je osvežen."
            );
        }
    }

    function normalizeSignalRPosition(
        payload
    ) {

        if (!payload) {
            return null;
        }

        const vehicleId =
            firstValue(
                payload,
                [
                    "vehicleId",
                    "VehicleId",
                    "id",
                    "Id"
                ]
            );

        const point =
            normalizeCoordinate(
                payload
            );

        if (
            vehicleId === null ||
            !point
        ) {
            return null;
        }

        return {
            vehicleId: String(
                vehicleId
            ),
            latitude:
                point.latitude,
            longitude:
                point.longitude,

            speed:
                numberOrNull(
                    firstValue(
                        payload,
                        [
                            "speed",
                            "Speed",
                            "speedKmh",
                            "SpeedKmh",
                            "currentSpeedKmh",
                            "CurrentSpeedKmh"
                        ]
                    )
                ),

            heading:
                numberOrNull(
                    firstValue(
                        payload,
                        [
                            "heading",
                            "Heading",
                            "bearing",
                            "Bearing",
                            "currentBearing",
                            "CurrentBearing"
                        ]
                    )
                ),

            accuracy:
                numberOrNull(
                    firstValue(
                        payload,
                        [
                            "accuracy",
                            "Accuracy",
                            "gpsAccuracy",
                            "GpsAccuracy"
                        ]
                    )
                ),

            timestamp:
                firstValue(
                    payload,
                    [
                        "timestamp",
                        "Timestamp",
                        "recordedAt",
                        "RecordedAt",
                        "lastGpsAt",
                        "LastGpsAt"
                    ]
                ) ||
                new Date().toISOString()
        };
    }

    function applyLivePosition(
        payload
    ) {

        const position =
            normalizeSignalRPosition(
                payload
            );

        if (!position) {
            return;
        }

        const index =
            state.vehicles.findIndex(
                vehicle =>
                    vehicle.id ===
                    position.vehicleId
            );

        if (
            index < 0
        ) {
            return;
        }

        const current =
            state.vehicles[index];

        const speed =
            position.speed ??
            current.speed ??
            0;

        const status =
            speed > 5
                ? "moving"
                : "idle";

        state.vehicles[index] = {
            ...current,
            latitude:
                position.latitude,
            longitude:
                position.longitude,
            speed,
            heading:
                position.heading ??
                current.heading,
            accuracy:
                position.accuracy ??
                current.accuracy,
            status,
            lastGpsAt:
                position.timestamp
        };

        state.lastUpdated =
            new Date();

        updateKpis();

        renderVehicleList();

        renderAlerts();

        updateMarkers();

        setText(
            "fleetUpdatedAt",
            formatTime(
                state.lastUpdated
            )
        );

        if (
            state.selectedVehicleId ===
            position.vehicleId
        ) {

            const vehicle =
                state.vehicles[index];

            renderVehicleDetail(
                vehicle
            );
        }
    }

    async function startSignalR() {

        if (
            typeof signalR === "undefined"
        ) {
            console.warn(
                "SignalR client nije učitan."
            );

            setMonitoringLive(
                false,
                "LIVE REST"
            );

            return;
        }

        if (
            state.signalRStarted
        ) {
            return;
        }

        try {

            const connection =
                new signalR.HubConnectionBuilder()
                    .withUrl(
                        "/hubs/navigation"
                    )
                    .withAutomaticReconnect()
                    .configureLogging(
                        signalR.LogLevel.Warning
                    )
                    .build();

            connection.on(
                "vehiclePositionUpdated",
                applyLivePosition
            );

            connection.on(
                "VehiclePositionUpdated",
                applyLivePosition
            );

            connection.onreconnecting(
                () => {
                    setMonitoringLive(
                        false,
                        "RECONNECTING"
                    );
                }
            );

            connection.onreconnected(
                async () => {

                    setMonitoringLive(
                        true,
                        "LIVE SIGNALR"
                    );

                    try {
                        await connection.invoke(
                            "JoinCompany"
                        );
                    }
                    catch (error) {
                        console.warn(
                            "JoinCompany failed:",
                            error
                        );
                    }
                }
            );

            connection.onclose(
                () => {

                    state.signalRStarted =
                        false;

                    setMonitoringLive(
                        false,
                        "LIVE OFFLINE"
                    );
                }
            );

            await connection.start();

            state.signalRConnection =
                connection;

            state.signalRStarted =
                true;

            try {
                await connection.invoke(
                    "JoinCompany"
                );
            }
            catch (error) {
                console.warn(
                    "JoinCompany nije dostupan:",
                    error
                );
            }

            setMonitoringLive(
                true,
                "LIVE SIGNALR"
            );

        }
        catch (error) {

            console.warn(
                "SignalR monitoring nije dostupan:",
                error
            );

            setMonitoringLive(
                false,
                "LIVE REST"
            );
        }
    }

    function setMonitoringLive(
        live,
        text
    ) {

        const badge =
            $("monitoringLiveBadge");

        const mapStatus =
            $("mapStatus");

        if (badge) {

            badge.classList.toggle(
                "offline",
                !live
            );
        }

        setText(
            "monitoringLiveText",
            text
        );

        if (mapStatus) {

            mapStatus.textContent =
                live
                    ? "LIVE TELEMETRY"
                    : "REST TELEMETRY";
        }
    }

    function bindEvents() {

        $("refreshMonitoring")
            ?.addEventListener(
                "click",
                refreshMonitoring
            );

        $("fitMonitoringMap")
            ?.addEventListener(
                "click",
                fitFleet
            );

        $("fitFleet")
            ?.addEventListener(
                "click",
                fitFleet
            );

        $("centerSerbia")
            ?.addEventListener(
                "click",
                centerSerbia
            );

        $("toggleDarkMap")
            ?.addEventListener(
                "click",
                toggleMapLayer
            );

        document
            .querySelectorAll(
                ".monitoring-filter"
            )
            .forEach(
                button => {

                    button.addEventListener(
                        "click",
                        () => {

                            document
                                .querySelectorAll(
                                    ".monitoring-filter"
                                )
                                .forEach(
                                    item =>
                                        item.classList.remove(
                                            "active"
                                        )
                                );

                            button.classList.add(
                                "active"
                            );

                            state.filter =
                                button.dataset.filter ||
                                "all";

                            renderVehicleList();
                        }
                    );
                }
            );
    }

    async function initialize() {

        bindEvents();

        initializeMap();

        setLoading(true);

        const loaded =
            await loadFleetFromApi();

        if (!loaded) {
            startDemoMode();
        }

        setLoading(false);

        await startSignalR();

        if (
            state.vehicles.length > 0
        ) {
            setTimeout(
                fitFleet,
                300
            );
        }

        setTimeout(
            () => {
                state.map?.invalidateSize();
            },
            500
        );
    }

    initialize();

})();
