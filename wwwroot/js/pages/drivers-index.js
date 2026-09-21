(function () {
    "use strict";

    var state = {
        drivers: [],
        vehicles: [],
        trips: [],
        selectedDriverId: null
    };

    var elements = {
        tableBody: document.getElementById("driversTableBody"),
        search: document.getElementById("driverSearch"),
        statusFilter: document.getElementById("driverStatusFilter"),
        gpsFilter: document.getElementById("driverGpsFilter"),
        results: document.getElementById("driverResults"),
        refresh: document.getElementById("refreshDrivers"),
        refreshSmall: document.getElementById("refreshDriversSmall"),
        addDriver: document.getElementById("addDriver"),

        kpiTotal: document.getElementById("kpiTotal"),
        kpiActive: document.getElementById("kpiActive"),
        kpiTrips: document.getElementById("kpiTrips"),
        kpiGps: document.getElementById("kpiGps"),
        kpiUnavailable: document.getElementById("kpiUnavailable"),

        detailAvatar: document.getElementById("detailAvatar"),
        detailName: document.getElementById("detailName"),
        detailLicense: document.getElementById("detailLicense"),
        detailLicenseValue: document.getElementById("detailLicenseValue"),
        detailStatus: document.getElementById("detailStatus"),
        detailPhone: document.getElementById("detailPhone"),
        detailCountry: document.getElementById("detailCountry"),
        detailGps: document.getElementById("detailGps"),
        detailGpsStatus: document.getElementById("detailGpsStatus"),
        detailVehicle: document.getElementById("detailVehicle"),
        detailTrip: document.getElementById("detailTrip"),

        callDriver: document.getElementById("callDriver"),
        showDriverRoute: document.getElementById("showDriverRoute"),
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

    function getDriverStatus(driver) {
        var value = driver && driver.status;

        if (typeof value === "number") {
            return ["Active", "Inactive", "OnLeave", "Suspended"][value] || "Inactive";
        }

        return value || "Inactive";
    }

    function statusLabel(status) {
        var labels = {
            Active: "Aktivan",
            Inactive: "Neaktivan",
            OnLeave: "Na odmoru",
            Suspended: "Suspendovan"
        };

        return labels[status] || status || "Nepoznato";
    }

    function statusClass(status) {
        if (status === "Active") {
            return "active";
        }

        if (status === "OnLeave") {
            return "leave";
        }

        if (status === "Suspended") {
            return "suspended";
        }

        return "inactive";
    }

    function getGpsState(driver) {
        if (!driver || !driver.lastGpsAt) {
            return "offline";
        }

        var timestamp = new Date(driver.lastGpsAt).getTime();

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

    function gpsLabel(gpsState) {
        if (gpsState === "online") {
            return "Online";
        }

        if (gpsState === "stale") {
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

    function getInitials(name) {
        var parts = String(name || "?")
            .trim()
            .split(/\s+/)
            .filter(Boolean);

        if (!parts.length) {
            return "?";
        }

        if (parts.length === 1) {
            return parts[0].substring(0, 2).toUpperCase();
        }

        return (
            parts[0].substring(0, 1) +
            parts[parts.length - 1].substring(0, 1)
        ).toUpperCase();
    }

    function getVehicle(driver) {
        var id = normalizeId(driver && driver.currentVehicleId);

        if (!id) {
            return null;
        }

        for (var i = 0; i < state.vehicles.length; i++) {
            if (normalizeId(state.vehicles[i].id) === id) {
                return state.vehicles[i];
            }
        }

        return null;
    }

    function getTrip(driver) {
        var id = normalizeId(driver && driver.currentTripId);

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

    function vehicleLabel(driver) {
        var vehicle = getVehicle(driver);

        if (!vehicle) {
            return "Nije dodeljeno";
        }

        var registration = vehicle.registration || "Bez registracije";
        var makeModel = [vehicle.make, vehicle.model]
            .filter(Boolean)
            .join(" ");

        if (makeModel) {
            return registration + " · " + makeModel;
        }

        return registration;
    }

    function tripLabel(driver) {
        var trip = getTrip(driver);

        if (!trip) {
            return "Nema aktivne ture";
        }

        var status = trip.executionState;

        if (typeof status === "number") {
            status = [
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
            ][status] || "Aktivna tura";
        }

        return status || "Aktivna tura";
    }

    function setLoading(isLoading) {
        if (!elements.refresh) {
            return;
        }

        if (isLoading) {
            elements.refresh.disabled = true;
            elements.refreshSmall.disabled = true;
        } else {
            elements.refresh.disabled = false;
            elements.refreshSmall.disabled = false;
        }
    }

    function updateKpis() {
        var total = state.drivers.length;
        var active = 0;
        var trips = 0;
        var gpsOnline = 0;
        var unavailable = 0;

        state.drivers.forEach(function (driver) {
            var status = getDriverStatus(driver);
            var gps = getGpsState(driver);

            if (status === "Active") {
                active++;
            }

            if (driver.currentTripId) {
                trips++;
            }

            if (gps === "online") {
                gpsOnline++;
            }

            if (status === "OnLeave" || status === "Suspended") {
                unavailable++;
            }
        });

        elements.kpiTotal.textContent = total;
        elements.kpiActive.textContent = active;
        elements.kpiTrips.textContent = trips;
        elements.kpiGps.textContent = gpsOnline;
        elements.kpiUnavailable.textContent = unavailable;
    }

    function filteredDrivers() {
        var search = String(elements.search.value || "")
            .trim()
            .toLowerCase();

        var status = elements.statusFilter.value;
        var gpsFilter = elements.gpsFilter.value;

        return state.drivers.filter(function (driver) {
            var driverStatus = getDriverStatus(driver);
            var gps = getGpsState(driver);

            if (status && driverStatus !== status) {
                return false;
            }

            if (gpsFilter && gps !== gpsFilter) {
                return false;
            }

            if (!search) {
                return true;
            }

            var haystack = [
                driver.fullName,
                driver.phone,
                driver.licenseNumber
            ]
                .filter(Boolean)
                .join(" ")
                .toLowerCase();

            return haystack.indexOf(search) !== -1;
        });
    }

    function renderTable() {
        var drivers = filteredDrivers();

        elements.results.textContent =
            drivers.length +
            " od " +
            state.drivers.length +
            " vozača";

        if (!drivers.length) {
            elements.tableBody.innerHTML =
                '<tr>' +
                    '<td colspan="7">' +
                        '<div class="pm-driver-empty">' +
                            '<div class="pm-driver-empty-icon">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">' +
                                    '<circle cx="11" cy="11" r="7"></circle>' +
                                    '<path d="m20 20-4-4"></path>' +
                                '</svg>' +
                            '</div>' +
                            '<div class="pm-driver-empty-title">Nema rezultata</div>' +
                            '<div class="pm-driver-empty-text">' +
                                'Nijedan vozač ne odgovara izabranim filterima.' +
                            '</div>' +
                        '</div>' +
                    '</td>' +
                '</tr>';

            return;
        }

        elements.tableBody.innerHTML = drivers.map(function (driver) {
            var id = normalizeId(driver.id);
            var status = getDriverStatus(driver);
            var gps = getGpsState(driver);
            var vehicle = getVehicle(driver);
            var trip = getTrip(driver);

            var vehicleText = vehicle
                ? escapeHtml(vehicle.registration || "Vozilo")
                : "Nije dodeljeno";

            var tripText = trip
                ? escapeHtml(tripLabel(driver))
                : "Nema aktivne ture";

            var selected = normalizeId(state.selectedDriverId) === id;

            return (
                '<tr class="' + (selected ? "selected" : "") + '" data-driver-id="' + escapeHtml(id) + '">' +

                    '<td>' +
                        '<div class="pm-driver-person">' +
                            '<div class="pm-driver-avatar">' +
                                escapeHtml(getInitials(driver.fullName)) +
                            '</div>' +
                            '<div>' +
                                '<div class="pm-driver-name">' +
                                    escapeHtml(driver.fullName || "Bez imena") +
                                '</div>' +
                                '<div class="pm-driver-meta">' +
                                    escapeHtml(driver.licenseNumber || "Licenca nije uneta") +
                                '</div>' +
                            '</div>' +
                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<span class="pm-driver-status ' + statusClass(status) + '">' +
                            '<span class="pm-driver-status-dot"></span>' +
                            escapeHtml(statusLabel(status)) +
                        '</span>' +
                    '</td>' +

                    '<td>' +
                        '<div class="pm-driver-cell-main">' +
                            escapeHtml(driver.phone || "—") +
                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<div class="pm-driver-cell-main">' +
                            vehicleText +
                        '</div>' +
                        '<div class="pm-driver-cell-sub">' +
                            (vehicle ? escapeHtml(vehicle.make || "") : "Bez dodele") +
                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<div class="pm-driver-cell-main">' +
                            tripText +
                        '</div>' +
                    '</td>' +

                    '<td>' +
                        '<span class="pm-driver-gps ' + gps + '">' +
                            '<span class="pm-driver-gps-dot"></span>' +
                            escapeHtml(gpsLabel(gps)) +
                        '</span>' +
                    '</td>' +

                    '<td>' +
                        '<span class="pm-driver-action">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">' +
                                '<path d="m9 18 6-6-6-6"></path>' +
                            '</svg>' +
                        '</span>' +
                    '</td>' +

                '</tr>'
            );
        }).join("");

        Array.prototype.forEach.call(
            elements.tableBody.querySelectorAll("[data-driver-id]"),
            function (row) {
                row.addEventListener("click", function () {
                    selectDriver(row.getAttribute("data-driver-id"));
                });
            }
        );
    }

    function findDriver(id) {
        var normalized = normalizeId(id);

        for (var i = 0; i < state.drivers.length; i++) {
            if (normalizeId(state.drivers[i].id) === normalized) {
                return state.drivers[i];
            }
        }

        return null;
    }

    function selectDriver(id) {
        var driver = findDriver(id);

        if (!driver) {
            return;
        }

        state.selectedDriverId = driver.id;

        var status = getDriverStatus(driver);
        var gps = getGpsState(driver);
        var vehicle = getVehicle(driver);
        var trip = getTrip(driver);

        elements.detailAvatar.textContent = getInitials(driver.fullName);
        elements.detailName.textContent = driver.fullName || "Bez imena";

        elements.detailLicense.textContent =
            driver.licenseNumber
                ? "Vozačka licenca · " + driver.licenseNumber
                : "Vozačka licenca nije uneta";

        elements.detailLicenseValue.textContent =
            driver.licenseNumber || "Nije uneto";

        elements.detailPhone.textContent =
            driver.phone || "Nije uneto";

        elements.detailCountry.textContent =
            driver.currentCountryCode || "—";

        elements.detailGps.textContent =
            formatDate(driver.lastGpsAt);

        elements.detailStatus.innerHTML =
            '<span class="pm-driver-status ' +
                statusClass(status) +
            '">' +
                '<span class="pm-driver-status-dot"></span>' +
                escapeHtml(statusLabel(status)) +
            '</span>';

        elements.detailVehicle.textContent =
            vehicle
                ? (vehicle.registration || "Vozilo")
                : "Nije dodeljeno";

        elements.detailTrip.textContent =
            trip
                ? tripLabel(driver)
                : "Nema aktivne ture";

        elements.detailGpsStatus.textContent =
            gpsLabel(gps);

        elements.callDriver.disabled = !driver.phone;
        elements.showDriverRoute.disabled = !driver.currentTripId;

        renderTable();
    }

    function renderError(message) {
        elements.tableBody.innerHTML =
            '<tr>' +
                '<td colspan="7">' +
                    '<div class="pm-driver-error">' +
                        '<div class="pm-driver-error-icon">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">' +
                                '<path d="M12 9v4"></path>' +
                                '<path d="M12 17h.01"></path>' +
                                '<path d="M10.3 3.8 2.8 17a2 2 0 0 0 1.75 3h14.9a2 2 0 0 0 1.75-3L13.7 3.8a2 2 0 0 0-3.4 0Z"></path>' +
                            '</svg>' +
                        '</div>' +
                        '<div class="pm-driver-error-title">Podaci nisu dostupni</div>' +
                        '<div class="pm-driver-error-text">' +
                            escapeHtml(message || "Fleet API nije vratio podatke.") +
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

    async function loadDrivers() {
        setLoading(true);

        try {
            var result = await Promise.all([
                fetchJson("/api/business/drivers"),
                fetchJson("/api/business/vehicles"),
                fetchJson("/api/business/trips")
            ]);

            state.drivers = Array.isArray(result[0]) ? result[0] : [];
            state.vehicles = Array.isArray(result[1]) ? result[1] : [];
            state.trips = Array.isArray(result[2]) ? result[2] : [];

            updateKpis();

            if (
                !state.selectedDriverId ||
                !findDriver(state.selectedDriverId)
            ) {
                if (state.drivers.length) {
                    state.selectedDriverId = state.drivers[0].id;
                }
            }

            renderTable();

            if (state.selectedDriverId) {
                selectDriver(state.selectedDriverId);
            }

            elements.lastRefreshLabel.textContent =
                "Poslednje osvežavanje: " +
                formatDate(new Date().toISOString());

        } catch (error) {
            console.error("Drivers page error:", error);

            state.drivers = [];
            state.vehicles = [];
            state.trips = [];

            elements.kpiTotal.textContent = "—";
            elements.kpiActive.textContent = "—";
            elements.kpiTrips.textContent = "—";
            elements.kpiGps.textContent = "—";
            elements.kpiUnavailable.textContent = "—";

            renderError(
                "Nije moguće učitati podatke sa /api/business/drivers. " +
                "Proverite prijavu korisnika i dostupnost Fleet API-ja."
            );
        } finally {
            setLoading(false);
        }
    }

    elements.search.addEventListener("input", renderTable);
    elements.statusFilter.addEventListener("change", renderTable);
    elements.gpsFilter.addEventListener("change", renderTable);

    elements.refresh.addEventListener("click", loadDrivers);
    elements.refreshSmall.addEventListener("click", loadDrivers);

    elements.addDriver.addEventListener("click", function () {
        window.location.href = "/drivers/new";
    });

    elements.callDriver.addEventListener("click", function () {
        var driver = findDriver(state.selectedDriverId);

        if (!driver || !driver.phone) {
            return;
        }

        window.location.href = "tel:" + driver.phone;
    });

    elements.showDriverRoute.addEventListener("click", function () {
        var driver = findDriver(state.selectedDriverId);

        if (!driver || !driver.currentTripId) {
            return;
        }

        window.location.href =
            "/trips?tripId=" +
            encodeURIComponent(driver.currentTripId);
    });

    loadDrivers();

})();
