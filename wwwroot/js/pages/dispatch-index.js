(() => {
    "use strict";

    const state = {
        map: null,
        markers: [],
        markerGroups: {
            all: [],
            active: [],
            alerts: [],
        },
    };

    const fleet = [
        {
            id: "BG-241-AA",
            lat: 44.8178,
            lon: 20.4573,
            route: "Beograd → München",
            status: "IN TRANSIT",
            group: "active",
        },
        {
            id: "NS-882-KK",
            lat: 45.2551,
            lon: 19.8335,
            route: "Novi Sad → Graz",
            status: "APPROACHING",
            group: "active",
        },
        {
            id: "NI-401-TT",
            lat: 43.32,
            lon: 21.9,
            route: "Niš → Zagreb",
            status: "BORDER CHECK",
            group: "alerts",
        },
        {
            id: "KG-119-MP",
            lat: 44.0128,
            lon: 20.9114,
            route: "Kragujevac → Milano",
            status: "LOADING",
            group: "active",
        },
        {
            id: "SU-552-RT",
            lat: 46.1,
            lon: 19.667,
            route: "Subotica → Budapest",
            status: "IN TRANSIT",
            group: "active",
        },
        {
            id: "CA-731-BG",
            lat: 45.2671,
            lon: 19.8335,
            route: "Novi Sad → Beograd",
            status: "DELAY",
            group: "alerts",
        },
    ];

    function initMap() {
        const element = document.getElementById("dispatchMap");

        if (!element || !window.L) {
            return;
        }

        if (state.map) {
            return;
        }

        state.map = L.map(element, {
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
            tapHold: true,
        }).setView([15, 50], 4);

        L.control
            .scale({
                imperial: false,
                position: "bottomleft",
            })
            .addTo(state.map);

        void window.ProMap.MapLayers.attach(state.map, {
            archiveUrl: "/maps/europe.pmtiles",
        }).catch(console.error);

        fleet.forEach(addFleetMarker);

        setTimeout(() => {
            state.map.invalidateSize();
        }, 150);
    }

    function addFleetMarker(vehicle) {
        if (!state.map) {
            return;
        }

        const marker = L.circleMarker([vehicle.lat, vehicle.lon], {
            radius: 9,
            weight: 2,
            fillOpacity: 0.9,
        }).addTo(state.map);

        marker.bindPopup(
            `
                            <strong>${escapeHtml(vehicle.id)}</strong><br>
                            ${escapeHtml(vehicle.route)}<br>
                            <span>${escapeHtml(vehicle.status)}</span>
                            `,
        );

        state.markers.push({
            marker,
            group: vehicle.group,
            id: vehicle.id,
        });

        state.markerGroups.all.push(marker);

        if (vehicle.group === "active") {
            state.markerGroups.active.push(marker);
        }

        if (vehicle.group === "alerts") {
            state.markerGroups.alerts.push(marker);
        }
    }

    function setMapFilter(filter) {
        state.markers.forEach((item) => {
            const visible = filter === "all" || item.group === filter;

            if (visible) {
                if (!state.map.hasLayer(item.marker)) {
                    item.marker.addTo(state.map);
                }
            } else {
                if (state.map.hasLayer(item.marker)) {
                    item.marker.remove();
                }
            }
        });

        document.querySelectorAll("[data-map-filter]").forEach((button) => {
            button.classList.toggle("active", button.dataset.mapFilter === filter);
        });
    }

    function fitMap() {
        if (!state.map || !fleet.length) {
            return;
        }

        const bounds = L.latLngBounds(
            fleet.map((vehicle) => [vehicle.lat, vehicle.lon]),
        );

        state.map.fitBounds(bounds, {
            padding: [35, 35],
        });
    }

    function centerMap() {
        if (!state.map) {
            return;
        }

        state.map.setView([44.9, 20.5], 7, {
            animate: true,
        });
    }

    function clearEvents() {
        const container = document.getElementById("dispatchEvents");

        if (!container) {
            return;
        }

        container.innerHTML = `
                            <div class="pm-dispatch-empty">
                                Nema novih događaja.
                            </div>
                        `;
    }

    function refreshPage() {
        const button = document.getElementById("dispatchRefresh");

        if (!button) {
            return;
        }

        const original = button.textContent;

        button.disabled = true;
        button.textContent = "Osvežavanje…";

        window.setTimeout(() => {
            button.disabled = false;
            button.textContent = original;
        }, 700);
    }

    function focusTrip(id) {
        const vehicle = fleet.find((item) => item.id === id);

        if (!vehicle || !state.map) {
            return;
        }

        state.map.setView([vehicle.lat, vehicle.lon], 11, {
            animate: true,
        });

        const markerItem = state.markers.find((item) => item.id === id);

        if (markerItem) {
            markerItem.marker.openPopup();
        }
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function bindEvents() {
        document.querySelectorAll("[data-map-filter]").forEach((button) => {
            button.addEventListener("click", () => {
                setMapFilter(button.dataset.mapFilter || "all");
            });
        });

        document
            .getElementById("dispatchFitMap")
            ?.addEventListener("click", fitMap);

        document
            .getElementById("dispatchCenterMap")
            ?.addEventListener("click", centerMap);

        document
            .getElementById("dispatchRefresh")
            ?.addEventListener("click", refreshPage);

        document
            .getElementById("dispatchClearEvents")
            ?.addEventListener("click", clearEvents);

        document.querySelectorAll(".pm-dispatch-trip").forEach((trip) => {
            trip.addEventListener("click", () => {
                const id = trip.dataset.trip;

                if (id) {
                    focusTrip(id);
                }
            });
        });
    }

    function initialize() {
        initMap();
        bindEvents();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, {
            once: true,
        });
    } else {
        initialize();
    }
})();
