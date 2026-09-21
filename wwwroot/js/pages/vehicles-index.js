(function () {
    "use strict";

    var state = {
        vehicles: [],
        drivers: [],
        trips: [],
        selectedVehicleId: null
    };

    var elements = {
        tableBody: document.getElementById("vehiclesTableBody"),

        search: document.getElementById("vehicleSearch"),
        statusFilter: document.getElementById("vehicleStatusFilter"),
        gpsFilter: document.getElementById("vehicleGpsFilter"),
        results: document.getElementById("vehicleResults"),

        refresh: document.getElementById("refreshVehicles"),
        refreshSmall: document.getElementById("refreshVehiclesSmall"),
        addVehicle: document.getElementById("addVehicle"),

        kpiTotal: document.getElementById("kpiTotal"),
        kpiActive: document.getElementById("kpiActive"),
        kpiTrips: document.getElementById("kpiTrips"),
        kpiGps: document.getElementById("kpiGps"),
        kpiMaintenance: document.getElementById("kpiMaintenance"),

        detailRegistration: document.getElementById("detailRegistration"),
        detailVehicleName: document.getElementById("detailVehicleName"),
        detailStatus: document.getElementById("detailStatus"),

        detailRegistrationValue: document.getElementById("detailRegistrationValue"),
        detailVin: document.getElementById("detailVin"),
        detailMake: document.getElementById("detailMake"),
        detailModel: document.getElementById("detailModel"),

        detailDriver: document.getElementById("detailDriver"),
        detailTrip: document.getElementById("detailTrip"),
        detailGpsStatus: document.getElementById("detailGpsStatus"),

        detailYear: document.getElementById("detailYear"),
        detailType: document.getElementById("detailType"),
        detailWeight: document.getElementById("detailWeight"),
        detailGpsTime: document.getElementById("detailGpsTime"),

        showVehicleTrip: document.getElementById("showVehicleTrip"),
        showVehicleNavigation: document.getElementById("showVehicleNavigation"),

        lastRefreshLabel: document.getElementById("lastRefreshLabel")
    };

    function escapeHtml(value) {
        return String(value == null ? "" : value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function normalizeId(value) {
        if (value == null) {
            return "";
        }

        return String(value).toLowerCase();
    }

    function getVehicleStatus(vehicle) {
        var value = vehicle && vehicle.status;

        if (typeof value === "number") {
            return [
                "Active",
                "Available",
                "Maintenance",
                "Inactive"
            ][value] || "Inactive";
        }

        return value || "Inactive";
    }

    function statusLabel(status) {
        var labels = {
            Active: "Aktivno",
            Available: "Dostupno",
            Maintenance: "Servis",
            Inactive: "Neaktivno"
        };

        return labels[status] || status || "Nepoznato";
    }

    function statusClass(status) {
        if (status === "Active") {
            return "active";
        }

        if (status === "Available") {
            return "available";
        }

        if (status === "Maintenance") {
            return "maintenance";
        }

        return "inactive";
    }

    function getGpsState(vehicle) {
        if (!vehicle || !vehicle.lastGpsAt) {
            return "offline";
        }

        var timestamp = new Date(vehicle.lastGpsAt).getTime();

        if (!Number.isFinite(timestamp)) {
            return "offline";
        }

        var ageMinutes = (Date.now() - timestamp) / 60000;

        if (ageMinutes <= 2) {
            return "online";
        }

        if (ageMinutes <= 15) {
            return "stale";
        }

        return "offline";
    }

    function gpsLabel(stateValue) {
        if (stateValue === "online") {
            return "Online";
        }

        if (stateValue === "stale") {
            return "Zastareo";
        }

        return "Offline";
    }

    function formatDate(value) {
        if (!value) {
            return "—";
        }

        var date = new Date(value);

        if (Number.isNaN(date.getTime())) {
            return "—";
        }

        return new Intl.DateTimeFormat("sr-Latn-RS", {
            dateStyle: "short",
            timeStyle: "short"
        }).format(date);
    }

    function getDriver(vehicle) {
        var id = normalizeId(vehicle && vehicle.currentDriverId);

        if (!id) {
            return null;
        }

        for (var i = 0; i < state.drivers.length; i++) {
            if (normalizeId(state.drivers[i].id) === id) {
                return state.drivers[i];
            }
        }

        return null;
    }

    function getTrip(vehicle) {
        var id = normalizeId(vehicle && vehicle.currentTripId);

        if (!id) {
            return null;
        }

        for (var i = 0; i < state.trips.length; i++) {
            if (normalizeId(state.trips[i].id) === id) {
                return state.trips[i];
            }
        }

        return null;
    }

    function getDriverName(vehicle) {
        var driver = getDriver(vehicle);

        if (!driver) {
            return "Nije dodeljen";
        }

        return driver.fullName || "Bez imena";
    }

    function getTripName(vehicle) {
        var trip = getTrip(vehicle);

        if (!trip) {
            return "Nema aktivne ture";
        }

        if (trip.code) {
            return trip.code;
        }

        if (trip.name) {
            return trip.name;
        }

        var executionState = trip.executionState;

        if (typeof executionState === "number") {
            return [
                "Planirano",
                "Dodeljeno",
                "Spremno",
                "Utovar",
                "Utovareno",
                "U transportu",
                "Prilaz stopu",
                "Na stopu",
                "Čekanje",
                "Prelaz granice",
                "Istovar",
                "Završeno",
                "Otkazano",
                "Suspendovano"
            ][executionState] || "Aktivna tura";
        }

        return executionState || "Aktivna tura";
    }

    function vehicleType(vehicle) {
        return (
            vehicle.vehicleType ||
            vehicle.type ||
            vehicle.category ||
            vehicle.bodyType ||
            "Teretno vozilo"
        );
    }

    function vehicleWeight(vehicle) {
        var weight =
            vehicle.maxWeightKg ||
            vehicle.capacityKg ||
            vehicle.weightKg ||
            vehicle.maxGrossWeightKg;

        if (!weight) {
            return "—";
        }

        var numeric = Number(weight);

        if (!Number.isFinite(numeric)) {
            return String(weight);
        }

        if (numeric >= 1000) {
            return (numeric / 1000).toLocaleString("sr-Latn-RS", {
                maximumFractionDigits: 1
            }) + " t";
        }

        return numeric.toLocaleString("sr-Latn-RS") + " kg";
    }

    function updateKpis() {
        var total = state.vehicles.length;
        var active = 0;
        var trips = 0;
        var gpsOnline = 0;
        var maintenance = 0;

        state.vehicles.forEach(function (vehicle) {
            var status = getVehicleStatus(vehicle);
            var gps = getGpsState(vehicle);

            if (status === "Active") {
                active++;
            }

            if (vehicle.currentTripId) {
                trips++;
            }

            if (gps === "online") {
                gpsOnline++;
            }

            if (status === "Maintenance") {
                maintenance++;
            }
        });

        elements.kpiTotal.textContent = total;
        elements.kpiActive.textContent = active;
        elements.kpiTrips.textContent = trips;
        elements.kpiGps.textContent = gpsOnline;
        elements.kpiMaintenance.textContent = maintenance;
    }

    function filteredVehicles() {
        var search = String(elements.search.value || "")
            .trim()
            .toLowerCase();

        var status = elements.statusFilter.value;
        var gpsFilter = elements.gpsFilter.value;

        return state.vehicles.filter(function (vehicle) {
            var vehicleStatus = getVehicleStatus(vehicle);
            var gps = getGpsState(vehicle);

            if (status && vehicleStatus !== status) {
                return false;
            }

            if (gpsFilter && gps !== gpsFilter) {
                return false;
            }

            if (!search) {
                return true;
            }

            var haystack = [
                vehicle.registration,
                vehicle.plateNumber,
                vehicle.make,
                vehicle.model,
                vehicle.vin,
                vehicle.vehicleType,
                vehicle.type
            ]
                .filter(Boolean)
                .join(" ")
                .toLowerCase();

            return haystack.indexOf(search) !== -1;
        });
    }

    function renderTable() {
        var vehicles = filteredVehicles();

        elements.results.textContent =
            vehicles.length +
            " od " +
            state.vehicles.length +
            " vozila";

        if (!vehicles.length) {
            elements.tableBody.innerHTML =
                '<tr>' +
                    '<td colspan="7">' +
                        '<div class="pm-vehicle-empty">' +
                            '<div class="pm-vehicle-state-icon">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">' +
                                    '<circle cx="11" cy="11" r="7"></circle>' +
                                    '<path d="m20 20-4-4"></path>' +
                                '</svg>' +
                            '</div>' +
                            '<div class="pm-vehicle-state-title">Nema rezultata</div>' +
                            '<div class="pm-vehicle-state-text">' +
                                'Nijedno vozilo ne odgovara izabranim filterima.' +
                            '</div>' +
                        '</div>' +
                    '</td>' +
                '</tr>';

            return;
        }

        elements.tableBody.innerHTML = vehicles.map(function (vehicle) {
            var id = normalizeId(vehicle.id);
            var status = getVehicleStatus(vehicle);
            var gps = getGpsState(vehicle);
            var driver = getDriver(vehicle);
            var trip = getTrip(vehicle);

            var registration =
                vehicle.registration ||
                vehicle.plateNumber ||
                "Bez registracije";

            var makeModel = [
                vehicle.make,
                vehicle.model
            ]
                .filter(Boolean)
                .join(" ");

            if (!makeModel) {
                makeModel = "Vozilo";
            }

            var selected =
                normalizeId(state.selectedVehicleId) === id;

            return (
                '<tr class="' +
                    (selected ? "selected" : "") +
                    '" data-vehicle-id="' +
                    escapeHtml(id) +
                    '">' +

                    '<td>' +
                        '<div class="pm-vehicle-identity">' +

                            '<div class="pm-vehicle-icon">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">' +
                                    '<path d="M3 17h3"></path>' +
                                    '<path d="M18 17h3"></path>' +
                                    '<path d="M5 17V9h10l4 4v4"></path>' +
                                    '<circle cx="8" cy="17" r="2"></circle>' +
                                    '<circle cx="17" cy="17" r="2"></circle>' +
                                    '<path d="M15 9v4h4"></path>' +
                                '</svg>' +
                            '</div>' +

                            '<div>' +
                                '<div class="pm-vehicle-registration">' +
                                    escapeHtml(registration) +
                                '</div>' +
                                '<div class="pm-vehicle-meta">' +
                                    escapeHtml(makeModel) +
                                '</div>' +
                            '</div>' +

                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<span class="pm-vehicle-status ' +
                            statusClass(status) +
                        '">' +
                            '<span class="pm-vehicle-status-dot"></span>' +
                            escapeHtml(statusLabel(status)) +
                        '</span>' +
                    '</td>' +

                    '<td>' +
                        '<div class="pm-vehicle-cell-main">' +
                            escapeHtml(vehicleType(vehicle)) +
                        '</div>' +
                        '<div class="pm-vehicle-cell-sub">' +
                            escapeHtml(vehicleWeight(vehicle)) +
                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<div class="pm-vehicle-cell-main">' +
                            escapeHtml(
                                driver
                                    ? (driver.fullName || "Bez imena")
                                    : "Nije dodeljen"
                            ) +
                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<div class="pm-vehicle-cell-main">' +
                            escapeHtml(
                                trip
                                    ? getTripName(vehicle)
                                    : "Nema aktivne ture"
                            ) +
                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<span class="pm-vehicle-gps ' +
                            gps +
                        '">' +
                            '<span class="pm-vehicle-gps-dot"></span>' +
                            escapeHtml(gpsLabel(gps)) +
                        '</span>' +
                    '</td>' +

                    '<td>' +
                        '<span class="pm-vehicle-action">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">' +
                                '<path d="m9 18 6-6-6-6"></path>' +
                            '</svg>' +
                        '</span>' +
                    '</td>' +

                '</tr>'
            );
        }).join("");

        Array.prototype.forEach.call(
            elements.tableBody.querySelectorAll("[data-vehicle-id]"),
            function (row) {
                row.addEventListener("click", function () {
                    selectVehicle(
                        row.getAttribute("data-vehicle-id")
                    );
                });
            }
        );
    }

    function findVehicle(id) {
        var normalized = normalizeId(id);

        for (var i = 0; i < state.vehicles.length; i++) {
            if (
                normalizeId(state.vehicles[i].id) === normalized
            ) {
                return state.vehicles[i];
            }
        }

        return null;
    }

    function selectVehicle(id) {
        var vehicle = findVehicle(id);

        if (!vehicle) {
            return;
        }

        state.selectedVehicleId = vehicle.id;

        var status = getVehicleStatus(vehicle);
        var gps = getGpsState(vehicle);
        var driver = getDriver(vehicle);
        var trip = getTrip(vehicle);

        var registration =
            vehicle.registration ||
            vehicle.plateNumber ||
            "Bez registracije";

        var makeModel = [
            vehicle.make,
            vehicle.model
        ]
            .filter(Boolean)
            .join(" ");

        elements.detailRegistration.textContent =
            registration;

        elements.detailVehicleName.textContent =
            makeModel || vehicleType(vehicle);

        elements.detailStatus.innerHTML =
            '<span class="pm-vehicle-status ' +
                statusClass(status) +
            '">' +
                '<span class="pm-vehicle-status-dot"></span>' +
                escapeHtml(statusLabel(status)) +
            '</span>';

        elements.detailRegistrationValue.textContent =
            registration;

        elements.detailVin.textContent =
            vehicle.vin || "Nije unet";

        elements.detailMake.textContent =
            vehicle.make || "—";

        elements.detailModel.textContent =
            vehicle.model || "—";

        elements.detailDriver.textContent =
            driver
                ? (driver.fullName || "Bez imena")
                : "Nije dodeljen";

        elements.detailTrip.textContent =
            trip
                ? getTripName(vehicle)
                : "Nema aktivne ture";

        elements.detailGpsStatus.textContent =
            gpsLabel(gps);

        elements.detailYear.textContent =
            vehicle.year ||
            vehicle.productionYear ||
            "—";

        elements.detailType.textContent =
            vehicleType(vehicle);

        elements.detailWeight.textContent =
            vehicleWeight(vehicle);

        elements.detailGpsTime.textContent =
            formatDate(vehicle.lastGpsAt);

        elements.showVehicleTrip.disabled =
            !vehicle.currentTripId;

        renderTable();
    }

    function renderError(message) {
        elements.tableBody.innerHTML =
            '<tr>' +
                '<td colspan="7">' +
                    '<div class="pm-vehicle-error">' +
                        '<div class="pm-vehicle-state-icon">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">' +
                                '<path d="M12 9v4"></path>' +
                                '<path d="M12 17h.01"></path>' +
                                '<path d="M10.3 3.8 2.8 17a2 2 0 0 0 1.75 3h14.9a2 2 0 0 0 1.75-3L13.7 3.8a2 2 0 0 0-3.4 0Z"></path>' +
                            '</svg>' +
                        '</div>' +
                        '<div class="pm-vehicle-state-title">Podaci nisu dostupni</div>' +
                        '<div class="pm-vehicle-state-text">' +
                            escapeHtml(message) +
                        '</div>' +
                    '</div>' +
                '</td>' +
            '</tr>';

        elements.results.textContent = "Greška";
    }

    async function fetchJson(url) {
        var response = await fetch(url, {
            method: "GET",
            headers: {
                "Accept": "application/json"
            },
            credentials: "same-origin",
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error(
                "HTTP " +
                response.status +
                " · " +
                url
            );
        }

        return await response.json();
    }

    async function loadVehicles() {
        elements.refresh.disabled = true;
        elements.refreshSmall.disabled = true;

        try {
            var result = await Promise.all([
                fetchJson("/api/business/vehicles"),
                fetchJson("/api/business/drivers"),
                fetchJson("/api/business/trips")
            ]);

            state.vehicles =
                Array.isArray(result[0])
                    ? result[0]
                    : [];

            state.drivers =
                Array.isArray(result[1])
                    ? result[1]
                    : [];

            state.trips =
                Array.isArray(result[2])
                    ? result[2]
                    : [];

            updateKpis();

            if (
                !state.selectedVehicleId ||
                !findVehicle(state.selectedVehicleId)
            ) {
                if (state.vehicles.length) {
                    state.selectedVehicleId =
                        state.vehicles[0].id;
                }
            }

            renderTable();

            if (state.selectedVehicleId) {
                selectVehicle(
                    state.selectedVehicleId
                );
            }

            elements.lastRefreshLabel.textContent =
                "Poslednje osvežavanje: " +
                formatDate(
                    new Date().toISOString()
                );

        } catch (error) {
            console.error(
                "Vehicles page error:",
                error
            );

            state.vehicles = [];
            state.drivers = [];
            state.trips = [];

            elements.kpiTotal.textContent = "—";
            elements.kpiActive.textContent = "—";
            elements.kpiTrips.textContent = "—";
            elements.kpiGps.textContent = "—";
            elements.kpiMaintenance.textContent = "—";

            renderError(
                "Nije moguće učitati podatke sa " +
                "/api/business/vehicles. " +
                "Proverite prijavu korisnika i dostupnost Fleet API-ja."
            );

        } finally {
            elements.refresh.disabled = false;
            elements.refreshSmall.disabled = false;
        }
    }

    elements.search.addEventListener(
        "input",
        renderTable
    );

    elements.statusFilter.addEventListener(
        "change",
        renderTable
    );

    elements.gpsFilter.addEventListener(
        "change",
        renderTable
    );

    elements.refresh.addEventListener(
        "click",
        loadVehicles
    );

    elements.refreshSmall.addEventListener(
        "click",
        loadVehicles
    );

    elements.addVehicle.addEventListener(
        "click",
        function () {
            window.location.href =
                "/vehicles/new";
        }
    );

    elements.showVehicleTrip.addEventListener(
        "click",
        function () {
            var vehicle =
                findVehicle(
                    state.selectedVehicleId
                );

            if (
                !vehicle ||
                !vehicle.currentTripId
            ) {
                return;
            }

            window.location.href =
                "/trips?tripId=" +
                encodeURIComponent(
                    vehicle.currentTripId
                );
        }
    );

    elements.showVehicleNavigation.addEventListener(
        "click",
        function () {
            var vehicle =
                findVehicle(
                    state.selectedVehicleId
                );

            if (!vehicle) {
                return;
            }

            window.location.href =
                "/navigation?vehicleId=" +
                encodeURIComponent(
                    vehicle.id
                );
        }
    );

    loadVehicles();

})();
