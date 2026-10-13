(() => {
    "use strict";

    // Command Center from the database (/api/dispatch/overview): fleet on the map (local
    // PMTiles layer), active trips, operational events, fleet status and border waits.

    const $ = id => document.getElementById(id);

    const state = {
        map: null,
        markers: [],
        data: null,
        hiddenEventsBefore: 0
    };

    const TRIP_STATE = {
        InTransit: ["green", "IN TRANSIT"], ApproachingStop: ["blue", "APPROACHING"], AtStop: ["blue", "AT STOP"],
        Loading: ["blue", "LOADING"], Unloading: ["blue", "UNLOADING"], BorderCrossing: ["yellow", "BORDER CHECK"],
        Waiting: ["yellow", "PAUSE"], Assigned: ["blue", "ASSIGNED"], Planned: ["blue", "PLANNED"],
        ReadyForDeparture: ["blue", "READY"], Loaded: ["green", "LOADED"], Suspended: ["yellow", "SUSPENDED"]
    };

    const EVENT_ICON = { Critical: ["danger", "!"], Warning: ["warning", "!"], Info: ["info", "i"] };

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function clock(iso) {
        return iso ? new Date(iso).toLocaleTimeString("sr-Latn-RS", { hour: "2-digit", minute: "2-digit" }) : "—";
    }

    function ago(iso) {
        const minutes = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
        if (minutes < 60) return `${minutes} min`;
        if (minutes < 1440) return `${Math.round(minutes / 60)} h`;
        return `${Math.round(minutes / 1440)} d`;
    }

    function setText(id, value) {
        const element = $(id);
        if (element) element.textContent = value;
    }

    // ---- Map ----

    function initMap() {
        const element = $("dispatchMap");
        if (!element || !window.L || state.map) {
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
            attributionControl: true
        }).setView([44.9, 20.5], 7);

        L.control.scale({ imperial: false, position: "bottomleft" }).addTo(state.map);

        const archive = element.getAttribute("data-map-archive") || "/maps/europe.pmtiles";
        void window.ProMap?.MapLayers?.attachToLeaflet(state.map, { archiveUrl: archive }).catch(console.error);

        setTimeout(() => state.map.invalidateSize(), 150);
    }

    function renderMarkers(fleet) {
        if (!state.map) return;
        state.markers.forEach(item => item.marker.remove());
        state.markers = [];

        fleet.filter(vehicle => vehicle.lat != null && vehicle.lon != null).forEach(vehicle => {
            const group = vehicle.hasAlert ? "alerts" : vehicle.status === "active" ? "active" : "all";
            const marker = L.circleMarker([vehicle.lat, vehicle.lon], {
                radius: 9,
                weight: 2,
                fillOpacity: 0.9,
                color: vehicle.hasAlert ? "#f59e0b" : "#10b981"
            }).addTo(state.map);

            marker.bindPopup(`
                <strong>${escapeHtml(vehicle.registration)}</strong><br>
                ${escapeHtml(vehicle.route || "")}<br>
                <span>${escapeHtml(vehicle.driver || "")}${vehicle.speedKmh ? " · " + Math.round(vehicle.speedKmh) + " km/h" : ""}</span>`);

            state.markers.push({ marker, group, id: vehicle.tripId || vehicle.id, active: vehicle.status === "active", alert: vehicle.hasAlert });
        });
    }

    function setMapFilter(filter) {
        state.markers.forEach(item => {
            const visible = filter === "all" || (filter === "active" && item.active) || (filter === "alerts" && item.alert);
            if (visible && !state.map.hasLayer(item.marker)) item.marker.addTo(state.map);
            if (!visible && state.map.hasLayer(item.marker)) item.marker.remove();
        });
        document.querySelectorAll("[data-map-filter]").forEach(button => button.classList.toggle("active", button.dataset.mapFilter === filter));
    }

    function fitMap() {
        if (!state.map || state.markers.length === 0) return;
        const bounds = L.latLngBounds(state.markers.map(item => item.marker.getLatLng()));
        state.map.fitBounds(bounds, { padding: [35, 35], maxZoom: 11 });
    }

    function centerMap() {
        state.map?.setView([44.9, 20.5], 7, { animate: true });
    }

    function focusTrip(id) {
        const item = state.markers.find(marker => marker.id === id);
        if (!item || !state.map) return;
        state.map.setView(item.marker.getLatLng(), 11, { animate: true });
        item.marker.openPopup();
    }

    // ---- Panels ----

    function renderKpi(kpi) {
        setText("kpiActiveTrips", kpi.activeTrips);
        setText("kpiDispatchers", kpi.dispatchers);
        setText("kpiDispatchersMeta", `${kpi.dispatchersOnline} trenutno online`);
        setText("kpiVehicles", kpi.vehicles);
        setText("kpiDelays", kpi.delays);
        setText("kpiBorders", kpi.borders);
        setText("kpiIncidents", kpi.incidents);
        setText("mapFleetCount", kpi.vehicles);
    }

    function renderTrips(trips) {
        $("dispatchTrips").innerHTML = trips.map(trip => {
            const [color, label] = TRIP_STATE[trip.state] || ["blue", trip.status.toUpperCase()];
            const planned = trip.status === "Planned";
            const progress = Math.round(trip.routeProgressPercent || 0);
            return `
                <div class="pm-dispatch-trip" data-trip="${trip.id}">
                    <div class="pm-dispatch-trip-top">
                        <div>
                            <div class="pm-dispatch-trip-route">${escapeHtml(trip.vehicle || "—")} · ${escapeHtml(trip.route)}</div>
                            <div class="pm-dispatch-trip-id">${escapeHtml(trip.tripNumber || "")}</div>
                        </div>
                        <div class="pm-dispatch-trip-status ${color}">${label}</div>
                    </div>
                    <div class="pm-dispatch-trip-meta">
                        <div class="pm-dispatch-trip-stat">
                            <div class="pm-dispatch-trip-stat-label">Vozač</div>
                            <div class="pm-dispatch-trip-stat-value">${escapeHtml(trip.driver || "—")}</div>
                        </div>
                        <div class="pm-dispatch-trip-stat">
                            <div class="pm-dispatch-trip-stat-label">${planned ? "Polazak" : "ETA"}</div>
                            <div class="pm-dispatch-trip-stat-value">${clock(planned ? trip.plannedStartAt : trip.eta)}</div>
                        </div>
                        <div class="pm-dispatch-trip-stat">
                            <div class="pm-dispatch-trip-stat-label">Napredak</div>
                            <div class="pm-dispatch-trip-stat-value">${progress}%</div>
                        </div>
                    </div>
                    <div class="pm-dispatch-progress"><span style="width:${progress}%"></span></div>
                </div>`;
        }).join("") || '<div class="pm-dispatch-empty">Nema aktivnih ni planiranih tura.</div>';
    }

    function renderEvents(events) {
        const visible = events.filter(item => new Date(item.createdAt).getTime() > state.hiddenEventsBefore);
        $("dispatchEvents").innerHTML = visible.map(item => {
            const [cls, icon] = EVENT_ICON[item.severity] || EVENT_ICON.Info;
            return `
                <div class="pm-dispatch-event">
                    <div class="pm-dispatch-event-icon ${cls}">${icon}</div>
                    <div>
                        <div class="pm-dispatch-event-title">${escapeHtml(item.type)}${item.status === "Resolved" ? " · rešeno" : ""}</div>
                        <div class="pm-dispatch-event-text">${escapeHtml(item.vehicle ? item.vehicle + ": " : "")}${escapeHtml(item.message)}</div>
                    </div>
                    <div class="pm-dispatch-event-time">${ago(item.createdAt)}</div>
                </div>`;
        }).join("") || '<div class="pm-dispatch-empty">Nema novih događaja.</div>';
    }

    function renderFleet(status) {
        const total = Math.max(1, status.inTransit + status.loading + status.available + status.maintenance);
        const row = (label, value) => `
            <div class="pm-dispatch-fleet-row">
                <div class="pm-dispatch-fleet-label">${label}</div>
                <div class="pm-dispatch-fleet-track"><div class="pm-dispatch-fleet-fill" style="width:${Math.round(value / total * 100)}%"></div></div>
                <div class="pm-dispatch-fleet-value">${value}</div>
            </div>`;
        $("dispatchFleet").innerHTML = row("In transit", status.inTransit) + row("Loading", status.loading) + row("Available", status.available) + row("Maintenance", status.maintenance);
    }

    function renderBorders(borders) {
        $("dispatchBorders").innerHTML = borders.map(border => `
            <div class="pm-dispatch-border-item">
                <div>
                    <div class="pm-dispatch-border-name">${escapeHtml(border.borderPoint)}</div>
                    <div class="pm-dispatch-border-meta">${escapeHtml(border.countryFrom)} → ${escapeHtml(border.countryTo)} · ${border.vehicles} vozil${border.vehicles === 1 ? "o" : "a"}</div>
                </div>
                <div class="pm-dispatch-border-wait">${border.waitingMinutes} min</div>
            </div>`).join("") || '<div class="pm-dispatch-empty">Nema vozila na granici.</div>';
    }

    async function load() {
        const button = $("dispatchRefresh");
        if (button) button.disabled = true;
        try {
            const response = await fetch("/api/dispatch/overview", { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            if (response.status === 401) throw new Error("Prijavite se da biste videli dispečerski centar.");
            if (!response.ok) throw new Error(`Podaci nisu učitani (${response.status}).`);
            state.data = await response.json();
            renderKpi(state.data.kpi);
            renderTrips(state.data.trips);
            renderEvents(state.data.events);
            renderFleet(state.data.fleetStatus);
            renderBorders(state.data.borders);
            renderMarkers(state.data.fleet);
        } catch (error) {
            $("dispatchTrips").innerHTML = `<div class="pm-dispatch-empty">${escapeHtml(error.message)}</div>`;
        } finally {
            if (button) button.disabled = false;
        }
    }

    function bindEvents() {
        document.querySelectorAll("[data-map-filter]").forEach(button => {
            button.addEventListener("click", () => setMapFilter(button.dataset.mapFilter || "all"));
        });
        $("dispatchFitMap")?.addEventListener("click", fitMap);
        $("dispatchCenterMap")?.addEventListener("click", centerMap);
        $("dispatchRefresh")?.addEventListener("click", load);
        // "Očisti" hides the events shown so far; they stay in the alert center.
        $("dispatchClearEvents")?.addEventListener("click", () => {
            state.hiddenEventsBefore = Date.now();
            renderEvents(state.data?.events || []);
        });
        $("dispatchTrips")?.addEventListener("click", event => {
            const trip = event.target.closest("[data-trip]");
            if (trip) focusTrip(trip.dataset.trip);
        });
    }

    function initialize() {
        initMap();
        bindEvents();
        load();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
