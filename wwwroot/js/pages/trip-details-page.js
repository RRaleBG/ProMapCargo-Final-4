(() => {
    "use strict";

    const root = document.querySelector(".pm-page--trip-details");
    if (!root) return;

    const loading = document.getElementById("tripDetailsLoading");
    const body = document.getElementById("tripDetailsBody");
    const error = document.getElementById("tripDetailsError");
    const pathParts = window.location.pathname.split("/").filter(Boolean);
    const tripId = pathParts[pathParts.length - 1];
    const http = window.ProMapCargo?.http;
    const detailUrl = `/api/business/trips/${encodeURIComponent(tripId)}/details`;

    const formatDate = (dateValue) => {
        if (!dateValue) return "—";
        const date = new Date(dateValue);
        if (Number.isNaN(date.getTime())) return "—";
        return new Intl.DateTimeFormat("sr-Latn-RS", {
            day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
        }).format(date);
    };

    const formatDistance = (meters) => {
        const distance = Number(meters);
        if (!Number.isFinite(distance) || distance <= 0) return "—";
        return new Intl.NumberFormat("sr-Latn-RS", { maximumFractionDigits: 0 }).format(distance / 1000) + " km";
    };

    const formatMoney = (amount, currency = "EUR") => {
        const valueNumber = Number(amount);
        if (!Number.isFinite(valueNumber)) return "—";
        return `${new Intl.NumberFormat("sr-Latn-RS", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(valueNumber)} ${currency}`;
    };

    const escapeHtml = (raw) => String(raw ?? "").replace(/[&<>"']/g, (character) => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;",
    })[character]);

    const nl2br = (text) => escapeHtml(text || "—").replace(/\n/g, "<br>");

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
        const costsBody = document.getElementById("tripDetailCostsRows");
        if (!costsBody) return;
        if (!Array.isArray(costs) || !costs.length) {
            costsBody.innerHTML = '<tr><td colspan="5" class="pm-trip-empty">Nema troškova.</td></tr>';
            return;
        }
        costsBody.innerHTML = costs.map((cost) => `
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

    function setText(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = value || "—";
    }

    function setHtml(id, value) {
        const element = document.getElementById(id);
        if (element) element.innerHTML = value;
    }

    function setLink(id, href, enabled) {
        const element = document.getElementById(id);
        if (!element) return;
        element.setAttribute("href", href || "#");
        element.setAttribute("aria-disabled", enabled ? "false" : "true");
        element.classList.toggle("pm-btn-disabled", !enabled);
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
        setLink("tripDetailDriverLink", driver.id ? `/drivers/details/${encodeURIComponent(driver.id)}` : "/drivers", Boolean(driver.id));

        setText("tripDetailVehicleRegistration", vehicle.registration);
        setText("tripDetailVehicleDescription", [vehicle.name, vehicle.make, vehicle.model, vehicle.type].filter(Boolean).join(" · ") || "—");
        setText("tripDetailVehicleCapacity", [vehicle.grossWeightTons ? `${vehicle.grossWeightTons} t` : null, vehicle.lengthMeters ? `${vehicle.lengthMeters} m` : null, vehicle.heightMeters ? `${vehicle.heightMeters} m` : null].filter(Boolean).join(" · ") || "—");
        setText("tripDetailVehicleFuel", [vehicle.fuelCard, vehicle.odometerKm ? `${vehicle.odometerKm} km` : null].filter(Boolean).join(" · ") || "—");
        setHtml("tripDetailVehicleHistory", historyTimeline(vehicle.tripHistory, "Vozilo nema prethodnih tura."));
        setLink("tripDetailVehicleLink", vehicle.id ? `/vehicles?vehicleId=${encodeURIComponent(vehicle.id)}` : "/vehicles", Boolean(vehicle.id));

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
        fillEarningAndAdvance(financial);
        renderCosts(financial.costs, financial.currency);
        applyViewer(details, order);
    }

    // Price, driver's share and the cash advance against the business expenses he paid himself.
    function fillEarningAndAdvance(financial) {
        const currency = financial.currency || "EUR";
        const hasNumber = (raw) => raw !== null && raw !== undefined && Number.isFinite(Number(raw));

        setText("tripDetailPrice", hasNumber(financial.transportPrice) ? formatMoney(financial.transportPrice, currency) : "Nije uneta");
        setText("tripDetailEarning", hasNumber(financial.driverEarningAmount) ? formatMoney(financial.driverEarningAmount, currency) : "Nije obračunata");
        setText("tripDetailEarningNote", hasNumber(financial.driverEarningPercent)
            ? `${new Intl.NumberFormat("sr-Latn-RS", { maximumFractionDigits: 2 }).format(financial.driverEarningPercent)} % od cene prevoza`
            : "od cene prevoza");

        if (!hasNumber(financial.advanceAmount)) {
            setText("tripDetailAdvance", "Nije data");
            setText("tripDetailAdvanceSpent", `Poslovni troškovi vozača ${formatMoney(financial.advanceSpentAmount ?? 0, currency)}`);
            setText("tripDetailAdvanceBalance", "—");
            setText("tripDetailAdvanceBalanceNote", "bez akontacije");
            return;
        }

        const balance = Number(financial.advanceBalance);
        setText("tripDetailAdvance", formatMoney(financial.advanceAmount, currency));
        setText("tripDetailAdvanceSpent", `Opravdano troškovima ${formatMoney(financial.advanceSpentAmount ?? 0, currency)}`);
        setText("tripDetailAdvanceBalance", formatMoney(Math.abs(balance), currency));
        setText("tripDetailAdvanceBalanceNote", balance > 0
            ? "vozač vraća firmi"
            : balance < 0 ? "firma duguje vozaču" : "izmireno");
    }

    // A driver sees his own trip without the price, budget and dispatch notes.
    function applyViewer(details, order) {
        const driverView = details?.driverView === true;
        root.querySelectorAll("[data-dispatch-only]").forEach((element) => {
            element.hidden = driverView;
        });
        setText("tripDetailFinanceHint", driverView
            ? "Vaša zarada, akontacija i troškovi koje opravdavate."
            : "Cena prevoza, zarada vozača, akontacija i svi troškovi ture.");

        const editOrder = document.getElementById("tripDetailEditOrder");
        if (editOrder) {
            editOrder.hidden = driverView || !order?.id;
            if (order?.id) editOrder.href = `/orders/new?id=${encodeURIComponent(order.id)}`;
        }
    }

    async function load() {
        if (!tripId || !http?.get) {
            if (error) {
                error.hidden = false;
                error.textContent = "Trip details nisu dostupni.";
            }
            if (loading) loading.hidden = true;
            return;
        }

        try {
            const seeded = window.ProMapCargo?.initialData?.take?.(detailUrl);
            const details = seeded !== undefined ? seeded : await http.get(detailUrl);
            if (!details) {
                throw new Error("Trip details nisu dostupni.");
            }

            fillDetails(details);
            if (error) {
                error.hidden = true;
                error.textContent = "";
            }
            if (loading) loading.hidden = true;
            if (body) body.hidden = false;
        } catch (loadError) {
            if (loading) loading.hidden = true;
            if (body) body.hidden = true;
            if (error) {
                error.hidden = false;
                error.textContent = loadError?.message || "Detalji ture trenutno nisu dostupni.";
            }
        }
    }

    load().catch(function (loadError) {
        if (loading) loading.hidden = true;
        if (body) body.hidden = true;
        if (error) {
            error.hidden = false;
            error.textContent = loadError?.message || "Detalji ture trenutno nisu dostupni.";
        }
    });
})();
