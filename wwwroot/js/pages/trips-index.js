(() => {
    "use strict";

    const root = document.getElementById("tripsPage");
    if (!root) return;

    const http = window.ProMapCargo?.http;
    const rows = document.getElementById("tripsRows");
    const search = document.getElementById("tripsSearch");
    const resultCount = document.getElementById("tripsResultCount");
    const errorBox = document.getElementById("tripsError");
    const refreshButton = document.getElementById("refreshTrips");
    const state = { trips: [], drivers: [], vehicles: [], orders: [], filter: "all", query: "" };

    const tripStatusNames = ["planned", "active", "paused", "completed", "cancelled"];
    const executionNames = [
        "planned", "assigned", "readyfordeparture", "loading", "loaded", "intransit",
        "approachingstop", "atstop", "waiting", "bordercrossing", "unloading", "completed",
        "cancelled", "suspended",
    ];
    const statusLabels = {
        planned: "Planirana",
        active: "Aktivna",
        paused: "Pauzirana",
        completed: "Završena",
        cancelled: "Otkazana",
    };
    const executionLabels = {
        planned: "Planirana",
        assigned: "Dodeljena",
        readyfordeparture: "Spremna za polazak",
        loading: "Utovar",
        loaded: "Utovareno",
        intransit: "U transportu",
        approachingstop: "Prilazi stanici",
        atstop: "Na stanici",
        waiting: "Čekanje",
        bordercrossing: "Granični prelaz",
        unloading: "Istovar",
        completed: "Završena",
        cancelled: "Otkazana",
        suspended: "Obustavljena",
    };
    const tones = {
        planned: "pm-trip-status--planned",
        active: "pm-trip-status--active",
        paused: "pm-trip-status--paused",
        completed: "pm-trip-status--completed",
        cancelled: "pm-trip-status--cancelled",
    };

    const value = (record, camel, pascal = camel[0].toUpperCase() + camel.slice(1)) =>
        record?.[camel] ?? record?.[pascal] ?? null;

    const key = (raw, names) => {
        if (typeof raw === "number" && Number.isInteger(raw)) return names[raw] ?? "unknown";
        const normalized = String(raw ?? "unknown").replace(/([a-z])([A-Z])/g, "$1$2").replace(/[\s_-]/g, "").toLowerCase();
        return names.includes(normalized) ? normalized : "unknown";
    };

    const escapeHtml = (raw) => String(raw ?? "").replace(/[&<>"']/g, (character) => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;",
    })[character]);

    const indexById = (records) => new Map(records.map((item) => [
        String(value(item, "id") ?? "").toLowerCase(), item,
    ]));

    const formatDistance = (meters) => {
        const distance = Number(meters);
        if (!Number.isFinite(distance) || distance <= 0) return "—";
        return new Intl.NumberFormat("sr-Latn-RS", { maximumFractionDigits: 0 }).format(distance / 1000) + " km";
    };

    const formatDate = (dateValue) => {
        if (!dateValue) return "—";
        const date = new Date(dateValue);
        if (Number.isNaN(date.getTime())) return "—";
        return new Intl.DateTimeFormat("sr-Latn-RS", {
            day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
        }).format(date);
    };

    const gpsState = (lastGpsAt) => {
        if (!lastGpsAt) return { label: "Nema signala", tone: "pm-trip-gps--none", date: "—" };
        const date = new Date(lastGpsAt);
        if (Number.isNaN(date.getTime())) return { label: "Nepoznat", tone: "pm-trip-gps--unknown", date: "—" };
        const fresh = Date.now() - date.getTime() <= 120_000;
        return {
            label: fresh ? "Aktivan" : "Zastareo",
            tone: fresh ? "pm-trip-gps--active" : "pm-trip-gps--stale",
            date: formatDate(date),
        };
    };

    function tripRow(trip, lookups) {
        const id = String(value(trip, "id") ?? "");
        const status = key(value(trip, "status"), tripStatusNames);
        const execution = key(value(trip, "executionState"), executionNames);
        const orderId = String(value(trip, "transportOrderId") ?? "").toLowerCase();
        const driverId = String(value(trip, "driverId") ?? "").toLowerCase();
        const vehicleId = String(value(trip, "vehicleId") ?? "").toLowerCase();
        const order = lookups.orders.get(orderId);
        const driver = lookups.drivers.get(driverId);
        const vehicle = lookups.vehicles.get(vehicleId);
        const orderNumber = value(order, "orderNumber");
        const tripLabel = id ? `Tura ${id.slice(0, 8).toUpperCase()}` : "Tura bez ID-a";
        const progressNumber = Number(value(trip, "routeProgressPercent"));
        const progress = Number.isFinite(progressNumber) ? Math.min(100, Math.max(0, progressNumber)) : 0;
        const routeDistance = formatDistance(value(trip, "plannedDistanceMeters"));
        const offRoute = Boolean(value(trip, "isOffRoute"));
        const gps = gpsState(value(trip, "lastGpsAt"));
        const searchable = [
            tripLabel,
            orderNumber,
            id,
            value(driver, "fullName"),
            value(vehicle, "registration"),
            statusLabels[status],
            executionLabels[execution],
        ].filter(Boolean).join(" ").toLocaleLowerCase("sr-Latn-RS");

        return {
            status,
            searchable,
            html: `<tr data-trip-status="${escapeHtml(status)}" data-trip-search="${escapeHtml(searchable)}" class="pm-trip-table-row">
                <td >
                    <div class="pm-trip-primary">${escapeHtml(tripLabel)}</div>
                    <div class="pm-trip-secondary">Nalog ${escapeHtml(orderNumber || (orderId ? orderId.slice(0, 8).toUpperCase() : "nije povezan"))}</div>
                </td>
                <td >
                    <span class="pm-trip-status ${tones[status] ?? tones.completed}">${escapeHtml(statusLabels[status] ?? "Nepoznat status")}</span>
                    <div class="pm-trip-secondary">${escapeHtml(executionLabels[execution] ?? "—")}${offRoute ? " · van rute" : ""}</div>
                </td>
                <td >
                    <div class="pm-trip-primary pm-trip-primary--medium">${escapeHtml(value(driver, "fullName") || "Vozač nije dodeljen")}</div>
                    <div class="pm-trip-secondary">${escapeHtml(value(vehicle, "registration") || "Vozilo nije dodeljeno")}</div>
                </td>
                <td >
                    <div class="pm-trip-progress">
                        <div class="pm-trip-progress-bar" role="progressbar" aria-label="Napredak ture" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${progress}">
                            <span  style="width:${progress}%"></span>
                        </div>
                        <span class="pm-trip-progress-value">${Math.round(progress)}%</span>
                    </div>
                    <div class="pm-trip-secondary">Pređeno ${escapeHtml(formatDistance(value(trip, "routeCoveredMeters")))}</div>
                </td>
                <td >
                    <div class="pm-trip-primary pm-trip-primary--medium">${escapeHtml(routeDistance)}</div>
                    <div class="pm-trip-secondary">Preostalo ${escapeHtml(formatDistance(value(trip, "routeRemainingMeters")))}</div>
                </td>
                <td >
                    <div class="tw-font-medium ${gps.tone}">${escapeHtml(gps.label)}</div>
                    <div class="tw-mt-1 tw-whitespace-nowrap tw-text-xs tw-text-pm-muted">${escapeHtml(gps.date)}</div>
                </td>
            </tr>`,
        };
    }

    function updateSummary(items) {
        const counts = { total: items.length, active: 0, planned: 0, paused: 0, completed: 0, cancelled: 0 };
        const lookupMaps = {
            drivers: indexById(state.drivers),
            vehicles: indexById(state.vehicles),
            orders: indexById(state.orders),
        };
        const renderedRows = items.map((trip) => tripRow(trip, lookupMaps));
        for (const item of renderedRows) counts[item.status] = (counts[item.status] ?? 0) + 1;

        for (const [name, count] of Object.entries(counts)) {
            const kpi = root.querySelector(`[data-trip-kpi="${name}"]`);
            if (kpi) kpi.textContent = new Intl.NumberFormat("sr-Latn-RS").format(count);
            const filterCount = root.querySelector(`[data-trip-filter-count="${name}"]`);
            if (filterCount) filterCount.textContent = new Intl.NumberFormat("sr-Latn-RS").format(count);
        }
        const allFilterCount = root.querySelector('[data-trip-filter-count="all"]');
        if (allFilterCount) allFilterCount.textContent = new Intl.NumberFormat("sr-Latn-RS").format(items.length);
        return renderedRows;
    }

    function render() {
        const renderedRows = updateSummary(state.trips);
        const visibleRows = renderedRows.filter((item) =>
            (state.filter === "all" || item.status === state.filter) &&
            (!state.query || item.searchable.includes(state.query))
        );

        const count = visibleRows.length;
        const suffix = count % 10 === 1 && count % 100 !== 11
            ? "tura"
            : count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 12 || count % 100 > 14)
                ? "ture"
                : "tura";
        resultCount.textContent = `${count} ${suffix}`;
        if (!state.trips.length) {
            rows.innerHTML = '<tr><td colspan="6" class="pm-trip-empty"><div class="pm-trip-empty-title">Nema evidentiranih tura</div><p class="pm-trip-empty-text">Kada se ture pojave u sistemu, biće prikazane ovde.</p></td></tr>';
            return;
        }
        if (!visibleRows.length) {
            rows.innerHTML = '<tr><td colspan="6" class="pm-trip-empty">Nema tura koje odgovaraju izabranom filteru.</td></tr>';
            return;
        }
        rows.innerHTML = visibleRows.map((item) => item.html).join("");
    }

    async function load() {
        if (!http?.get) {
            errorBox.hidden = false;
            errorBox.textContent = "HTTP servis nije dostupan. Osvežite stranicu i pokušajte ponovo.";
            rows.innerHTML = '<tr><td colspan="6" class="pm-trip-empty">Podaci nisu dostupni.</td></tr>';
            return;
        }

        refreshButton.disabled = true;
        refreshButton.setAttribute("aria-busy", "true");
        errorBox.hidden = true;
        resultCount.textContent = "Učitavanje…";
        try {
            const results = await Promise.allSettled([
                http.get("/api/business/trips"),
                http.get("/api/business/drivers"),
                http.get("/api/business/vehicles"),
                http.get("/api/business/orders"),
            ]);
            const tripResult = results[0];
            if (tripResult.status === "rejected") throw tripResult.reason;

            const records = (result) => result.status === "fulfilled" && Array.isArray(result.value) ? result.value : [];
            state.trips = Array.isArray(tripResult.value) ? tripResult.value : [];
            state.drivers = records(results[1]);
            state.vehicles = records(results[2]);
            state.orders = records(results[3]);
            render();
            if (results.slice(1).some((result) => result.status === "rejected")) {
                errorBox.hidden = false;
                errorBox.textContent = "Ture su učitane, ali podaci o pojedinim dodelama nisu dostupni.";
            }
        } catch (error) {
            errorBox.hidden = false;
            errorBox.textContent = error?.message || "Ture trenutno nisu dostupne. Pokušajte ponovo.";
            resultCount.textContent = "Učitavanje nije uspelo";
            rows.innerHTML = '<tr><td colspan="6" class="pm-trip-empty pm-trip-error-text">Nije moguće učitati ture.</td></tr>';
        } finally {
            refreshButton.disabled = false;
            refreshButton.removeAttribute("aria-busy");
        }
    }

    root.querySelectorAll("[data-trip-filter]").forEach((button) => {
        button.addEventListener("click", () => {
            state.filter = button.dataset.tripFilter || "all";
            root.querySelectorAll("[data-trip-filter]").forEach((item) => {
                const selected = item === button;
                item.setAttribute("aria-pressed", String(selected));
                item.classList.toggle("pm-trip-filter--active", selected);
                
                
                
                
                
            });
            render();
        });
    });

    search.addEventListener("input", () => {
        state.query = search.value.trim().toLocaleLowerCase("sr-Latn-RS");
        render();
    });
    refreshButton.addEventListener("click", load);
    load();
})();
