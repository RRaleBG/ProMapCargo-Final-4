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
    const detailModal = document.getElementById("tripDetailsModal");
    const detailBody = document.getElementById("tripDetailsBody");
    const detailLoading = document.getElementById("tripDetailsLoading");
    const detailError = document.getElementById("tripDetailsError");
    const state = { trips: [], drivers: [], vehicles: [], orders: [], filter: "all", query: "", selectedTripId: null };

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
        planned: "pm-status--planned",
        active: "pm-status--active",
        paused: "pm-status--paused",
        completed: "pm-status--completed",
        cancelled: "pm-status--cancelled",
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

    const formatMoney = (amount, currency = "EUR") => {
        const valueNumber = Number(amount);
        if (!Number.isFinite(valueNumber)) return "—";
        return `${new Intl.NumberFormat("sr-Latn-RS", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(valueNumber)} ${currency}`;
    };

    const nl2br = (text) => escapeHtml(text || "—").replace(/\n/g, "<br>");

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
        const tripNumber = value(trip, "tripNumber");
        const tripLabel = tripNumber ? `Tura ${tripNumber}` : id ? `Tura ${id.slice(0, 8).toUpperCase()}` : "Tura bez ID-a";
        const budget = Number(value(trip, "budgetAmount"));
        const when = (iso) => iso ? new Date(iso).toLocaleString("sr-Latn-RS", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }) : "—";
        const plan = `${when(value(trip, "startedAt") || value(trip, "plannedStartAt"))} → ${when(value(trip, "completedAt") || value(trip, "plannedArrivalAt"))}`;
        const progressNumber = Number(value(trip, "routeProgressPercent"));
        const progress = Number.isFinite(progressNumber) ? Math.min(100, Math.max(0, progressNumber)) : 0;
        const routeDistance = formatDistance(value(trip, "plannedDistanceMeters"));
        const offRoute = Boolean(value(trip, "isOffRoute"));
        const gps = gpsState(value(trip, "lastGpsAt"));
        const searchable = [
            tripLabel,
            tripNumber,
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
            html: `<tr data-trip-id="${escapeHtml(id)}" data-trip-status="${escapeHtml(status)}" data-trip-search="${escapeHtml(searchable)}" class="pm-trip-table-row pm-trip-table-row--interactive" tabindex="0" role="button" aria-label="Otvori detalje ture ${escapeHtml(tripLabel)}">
                <td >
                    <div class="pm-trip-primary">${escapeHtml(tripLabel)}</div>
                    <div class="pm-trip-secondary">Nalog ${escapeHtml(orderNumber || (orderId ? orderId.slice(0, 8).toUpperCase() : "nije povezan"))}${Number.isFinite(budget) && budget > 0 ? " · budžet " + escapeHtml(budget.toLocaleString("sr-Latn-RS")) + " " + escapeHtml(value(trip, "currency") || "EUR") : ""}</div>
                </td>
                <td >
                    <span class="pm-status ${tones[status] ?? tones.completed}">${escapeHtml(statusLabels[status] ?? "Nepoznat status")}</span>
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
                    <div class="pm-trip-secondary pm-trip-secondary--nowrap">${escapeHtml(plan)}</div>
                </td>
                <td >
                    <div class="pm-trip-gps ${gps.tone}">${escapeHtml(gps.label)}</div>
                    <div class="pm-trip-secondary pm-trip-secondary--nowrap">${escapeHtml(gps.date)}</div>
                </td>
            </tr>`,
        };
    }

    function historyTimeline(items, emptyText) {
        if (!Array.isArray(items) || !items.length) {
            return `<div class="pm-trip-empty">${escapeHtml(emptyText)}</div>`;
        }
        return items.map((item) => `
            <div class="timeline-item">
                <span class="timeline-dot"></span>
                <div>
                    <strong>${escapeHtml(item.tripNumber || item.orderNumber || "Tura")}</strong>
                    <span>${escapeHtml(item.route || "Ruta nije dostupna")}</span>
                    <span>${escapeHtml(formatDate(item.startedAt))} → ${escapeHtml(formatDate(item.completedAt))}</span>
                </div>
            </div>`).join("");
    }

    function stopsTimeline(stops) {
        if (!Array.isArray(stops) || !stops.length) {
            return '<div class="pm-trip-empty">Stanice nisu dostupne.</div>';
        }
        return stops.map((stop) => `
            <div class="timeline-item">
                <span class="timeline-dot"></span>
                <div>
                    <strong>${escapeHtml(`${stop.sequence}. ${stop.name}`)}</strong>
                    <span>${escapeHtml([stop.type, stop.city, stop.countryCode].filter(Boolean).join(" · "))}</span>
                    <span>${escapeHtml(formatDate(stop.plannedAt))}${stop.notes ? ` · ${escapeHtml(stop.notes)}` : ""}</span>
                </div>
            </div>`).join("");
    }

    function renderCosts(costs, currency) {
        const body = document.getElementById("tripDetailCostsRows");
        if (!body) return;
        if (!Array.isArray(costs) || !costs.length) {
            body.innerHTML = '<tr><td colspan="5" class="pm-trip-empty">Nema troškova.</td></tr>';
            return;
        }
        body.innerHTML = costs.map((cost) => `
            <tr>
                <td>
                    <div class="pm-trip-primary pm-trip-primary--medium">${escapeHtml(cost.category)}</div>
                    <div class="pm-trip-secondary">${escapeHtml(cost.notes || "Bez napomene")}</div>
                </td>
                <td>${escapeHtml(formatMoney(cost.amount, cost.currency || currency))}</td>
                <td>${escapeHtml([cost.paymentMethod, cost.cardLabel].filter(Boolean).join(" · ") || "—")}</td>
                <td>
                    <div class="pm-trip-secondary">${escapeHtml(formatDate(cost.at))}</div>
                    <div class="pm-trip-secondary">${escapeHtml([cost.location, cost.countryCode].filter(Boolean).join(" · ") || "—")}</div>
                </td>
                <td>${escapeHtml(cost.status)}</td>
            </tr>`).join("");
    }

    function setText(id, valueText) {
        const element = document.getElementById(id);
        if (element) element.textContent = valueText || "—";
    }

    function setHtml(id, html) {
        const element = document.getElementById(id);
        if (element) element.innerHTML = html;
    }

    function fillDetails(details) {
        const trip = details?.trip || {};
        const order = details?.order || {};
        const driver = details?.driver || {};
        const vehicle = details?.vehicle || {};
        const dispatcher = details?.dispatcher || {};
        const financial = details?.financial || {};

        setText("tripDetailsTitle", trip.tripNumber ? `Tura ${trip.tripNumber}` : "Detalji ture");
        setText("tripDetailsSubtitle", order.orderNumber ? `Nalog ${order.orderNumber} · ${order.customerName || "bez kupca"}` : "Kompletan pregled ture, naloga, vozača i troškova.");
        setText("tripDetailStatus", trip.status || "—");
        setText("tripDetailExecution", trip.executionState || "—");
        setText("tripDetailRoute", order.stops?.length ? `${order.stops[0].city || order.stops[0].name} → ${order.stops[order.stops.length - 1].city || order.stops[order.stops.length - 1].name}` : "Ruta nije definisana");
        setText("tripDetailSchedule", `${formatDate(trip.startedAt || trip.plannedStartAt)} → ${formatDate(trip.completedAt || trip.plannedArrivalAt)}`);
        setText("tripDetailProgress", `${Math.round(Number(trip.routeProgressPercent || 0))}%`);
        setText("tripDetailDistance", `${formatDistance(trip.routeCoveredMeters)} / ${formatDistance(trip.plannedDistanceMeters)}`);
        setText("tripDetailBudget", formatMoney(trip.budgetAmount, trip.currency));
        setText("tripDetailSpent", `${formatMoney(financial.spentAmount, financial.currency)} · preostalo ${formatMoney(trip.remainingBudget, trip.currency)}`);

        setText("tripDetailDriverName", driver.fullName);
        setText("tripDetailDriverContact", [driver.phone, driver.email].filter(Boolean).join(" · ") || "—");
        setText("tripDetailDriverLicense", [driver.licenseNumber, driver.licenseCategories].filter(Boolean).join(" · ") || "—");
        setText("tripDetailDriverNotes", driver.notes || "—");
        setHtml("tripDetailDriverHistory", historyTimeline(driver.tripHistory, "Vozač nema prethodnih tura."));

        setText("tripDetailVehicleRegistration", vehicle.registration);
        setText("tripDetailVehicleDescription", [vehicle.name, vehicle.make, vehicle.model, vehicle.type].filter(Boolean).join(" · ") || "—");
        setText("tripDetailVehicleCapacity", [vehicle.grossWeightTons ? `${vehicle.grossWeightTons} t` : null, vehicle.lengthMeters ? `${vehicle.lengthMeters} m` : null, vehicle.heightMeters ? `${vehicle.heightMeters} m` : null].filter(Boolean).join(" · ") || "—");
        setText("tripDetailVehicleFuel", [vehicle.fuelCard, vehicle.odometerKm ? `${vehicle.odometerKm} km` : null].filter(Boolean).join(" · ") || "—");
        setHtml("tripDetailVehicleHistory", historyTimeline(vehicle.tripHistory, "Vozilo nema prethodnih tura."));

        setText("tripDetailOrderNumber", order.orderNumber);
        setText("tripDetailCustomer", order.customerName);
        setText("tripDetailCargo", [order.cargoDescription, order.cargoWeightTons ? `${order.cargoWeightTons} t` : null, order.pallets ? `${order.pallets} pal.` : null].filter(Boolean).join(" · ") || "—");
        setText("tripDetailDispatcher", [dispatcher.displayName, dispatcher.phoneNumber].filter(Boolean).join(" · ") || "—");
        setHtml("tripDetailNotes", nl2br(order.notes || "Bez posebnih napomena za vozača."));
        setHtml("tripDetailStops", stopsTimeline(order.stops));

        setText("tripDetailCostsTotal", formatMoney(financial.spentAmount, financial.currency));
        setText("tripDetailCostsPending", `Na čekanju ${formatMoney(financial.pendingAmount, financial.currency)}`);
        setText("tripDetailDriverPaid", formatMoney(financial.driverPaidAmount, financial.currency));
        setText("tripDetailAllowances", `Dnevnice ${formatMoney(financial.allowanceAmount, financial.currency)}`);
        setText("tripDetailPaidOut", formatMoney(financial.paidOutAmount, financial.currency));
        setText("tripDetailRemaining", `Budžet saldo ${formatMoney(trip.remainingBudget, trip.currency)}`);
        renderCosts(financial.costs, financial.currency);
    }

    async function openTripDetails(tripId) {
        if (!tripId || !http?.get || !detailModal) {
            return;
        }

        state.selectedTripId = tripId;
        detailError.hidden = true;
        detailBody.hidden = true;
        detailLoading.hidden = false;
        window.ProMapCargo?.modal?.open?.(detailModal);

        try {
            const details = await http.get(`/api/business/trips/${tripId}/details`);
            fillDetails(details || {});
            detailLoading.hidden = true;
            detailBody.hidden = false;
        } catch (error) {
            detailLoading.hidden = true;
            detailError.hidden = false;
            detailError.textContent = error?.message || "Detalji ture trenutno nisu dostupni.";
        }
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

            const params = new URLSearchParams(window.location.search);
            const tripId = params.get("tripId");
            if (tripId && (params.get("open") === "details" || state.selectedTripId === null)) {
                void openTripDetails(tripId);
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

    rows.addEventListener("click", (event) => {
        const row = event.target.closest("[data-trip-id]");
        if (!row) return;
        openTripDetails(row.dataset.tripId);
    });

    rows.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") return;
        const row = event.target.closest("[data-trip-id]");
        if (!row) return;
        event.preventDefault();
        openTripDetails(row.dataset.tripId);
    });

    refreshButton.addEventListener("click", load);
    load();
})();
