(() => {
    "use strict";

    // Transport orders from the database (/api/business/orders/overview): KPIs, filters,
    // the table with paging and the detail panel of the selected order.

    const PAGE_SIZE = 10;

    // TransportOrderStatus → filter group used by the chips and the status select.
    const STATUS_GROUP = {
        0: "planned", 1: "planned", 2: "planned",
        3: "loading", 4: "loading", 5: "loading",
        6: "in-transit", 7: "in-transit", 8: "in-transit",
        9: "delivered", 10: "delivered",
        11: "cancelled"
    };

    const GROUP_BADGE = {
        "planned": ["neutral", "Planned"],
        "loading": ["blue", "Loading"],
        "in-transit": ["green", "In Transit"],
        "delivered": ["green", "Delivered"],
        "delayed": ["red", "Delayed"],
        "cancelled": ["neutral", "Cancelled"]
    };

    const PRIORITY = [["low", "Low"], ["medium", "Normal"], ["high", "High"], ["high", "Critical"]];
    const STOP_TYPES = ["Utovar", "Istovar", "Utovar i istovar", "Granica", "Odmor", "Gorivo", "Ostalo"];

    const $ = id => document.getElementById(id);
    const searchInput = $("orderSearch");
    const customerSelect = $("orderCustomer");
    const pickupSelect = $("orderPickup");
    const statusSelect = $("orderStatus");
    const dateSelect = $("orderDate");
    const tableBody = $("ordersTableBody");
    const emptyState = $("ordersEmpty");

    const state = {
        orders: [],
        chip: "all",
        page: 1,
        selectedId: null
    };

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function normalize(value) {
        return String(value || "").trim().toLowerCase();
    }

    function formatDate(iso) {
        if (!iso) return "—";
        const d = new Date(iso);
        return d.toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit", year: "numeric" });
    }

    function formatTime(iso) {
        if (!iso) return "";
        return new Date(iso).toLocaleTimeString("sr-Latn-RS", { hour: "2-digit", minute: "2-digit" });
    }

    function sameDay(a, b) {
        return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
    }

    // Delayed: a stop's planned time has passed without the stop being done.
    function groupOf(item) {
        const group = STATUS_GROUP[item.order.status] || "planned";
        if (group === "delivered" || group === "cancelled") return group;
        const now = Date.now();
        const late = item.stops.some(stop => stop.status !== 3 && stop.status !== 4 && stop.plannedAt && new Date(stop.plannedAt).getTime() < now - 30 * 60000);
        return late ? "delayed" : group;
    }

    function pickupOf(item) {
        return item.stops.find(stop => stop.type === 0 || stop.type === 2) || item.stops[0];
    }

    function deliveryOf(item) {
        const unloads = item.stops.filter(stop => stop.type === 1 || stop.type === 2);
        return unloads[unloads.length - 1] || item.stops[item.stops.length - 1];
    }

    function place(stop) {
        return stop ? (stop.city || stop.name) : "—";
    }

    // ---- Filters ----

    function filtered() {
        const search = normalize(searchInput?.value);
        const customer = customerSelect?.value || "";
        const pickup = pickupSelect?.value || "";
        const status = statusSelect?.value || "";
        const date = dateSelect?.value || "";
        const today = new Date();
        const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
        const weekEnd = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 7);

        return state.orders.filter(item => {
            const group = groupOf(item);
            const first = pickupOf(item);
            const text = normalize([
                item.order.orderNumber, item.order.customerName, item.order.cargoDescription, item.tripNumber,
                item.vehicleRegistration, item.driverName, ...item.stops.map(s => s.city + " " + s.name)
            ].join(" "));
            const planned = first?.plannedAt ? new Date(first.plannedAt) : null;

            const dateMatch = !date ||
                (date === "today" && planned && sameDay(planned, today)) ||
                (date === "tomorrow" && planned && sameDay(planned, tomorrow)) ||
                (date === "week" && planned && planned >= new Date(today.toDateString()) && planned < weekEnd);

            return (!search || text.includes(search))
                && (!customer || item.order.customerName === customer)
                && (!pickup || place(first) === pickup)
                && (!status || group === status)
                && (state.chip === "all" || group === state.chip)
                && dateMatch;
        });
    }

    // ---- Rendering ----

    function rowHtml(item) {
        const group = groupOf(item);
        const [badgeClass, badgeText] = GROUP_BADGE[group];
        const [priorityClass, priorityText] = PRIORITY[item.order.priority] || PRIORITY[1];
        const pickup = pickupOf(item);
        const delivery = deliveryOf(item);
        const selected = item.order.id === state.selectedId ? " selected" : "";

        return `
            <tr class="pm-order-row${selected}" data-order-id="${escapeHtml(item.order.id)}" data-status="${group}" data-customer="${escapeHtml(item.order.customerName)}">
                <td>
                    <div class="pm-orders-order-id">${escapeHtml(item.order.orderNumber)}</div>
                    <div class="pm-orders-order-ref">${escapeHtml(item.tripNumber || "bez ture")}</div>
                </td>
                <td>
                    <div class="pm-orders-customer">${escapeHtml(item.order.customerName)}</div>
                </td>
                <td>
                    <div class="pm-orders-location">
                        <span class="pm-orders-location-point"></span>
                        <div class="pm-orders-location-copy">
                            <strong>${escapeHtml(place(pickup))}</strong>
                            <small>Pickup · ${escapeHtml(formatTime(pickup?.plannedAt) || "—")}</small>
                        </div>
                    </div>
                    <div class="pm-orders-route-arrow">↓</div>
                    <div class="pm-orders-location">
                        <span class="pm-orders-location-point delivery"></span>
                        <div class="pm-orders-location-copy">
                            <strong>${escapeHtml(place(delivery))}</strong>
                            <small>Delivery · ${escapeHtml(formatDate(delivery?.plannedAt))}</small>
                        </div>
                    </div>
                </td>
                <td>
                    <div class="pm-orders-cargo">${escapeHtml(item.order.cargoDescription || "—")}</div>
                    <small>${item.order.hazmat ? "ADR" : "FTL"}</small>
                </td>
                <td>
                    <div class="pm-orders-weight">${item.order.cargoWeightTons != null ? escapeHtml(item.order.cargoWeightTons) + " t" : "—"}</div>
                    <small>${item.order.pallets != null ? escapeHtml(item.order.pallets) + " paleta" : ""}</small>
                </td>
                <td>
                    <div class="pm-orders-date">${escapeHtml(formatDate(pickup?.plannedAt))}</div>
                    <small>${escapeHtml(formatTime(pickup?.plannedAt))}</small>
                </td>
                <td>
                    <span class="pm-orders-badge ${badgeClass}">${badgeText}</span>
                </td>
                <td>
                    <span class="pm-orders-priority ${priorityClass}"><i></i>${priorityText}</span>
                </td>
                <td>
                    <div class="pm-orders-actions">
                        <button type="button" class="pm-orders-action" data-action="details" title="Detalji">→</button>
                        <button type="button" class="pm-orders-action" data-action="edit" title="Izmeni">⋮</button>
                    </div>
                </td>
            </tr>`;
    }

    function renderPagination(total) {
        const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
        state.page = Math.min(state.page, pages);
        const from = total === 0 ? 0 : (state.page - 1) * PAGE_SIZE + 1;
        const to = Math.min(total, state.page * PAGE_SIZE);

        const info = $("ordersPaginationInfo");
        if (info) info.textContent = `Prikaz ${from}–${to} od ${total} naloga`;

        const container = $("ordersPagination");
        if (!container) return;
        let html = `<button type="button" class="pm-pagination-btn" data-page="${state.page - 1}" ${state.page === 1 ? "disabled" : ""}>‹</button>`;
        for (let page = 1; page <= pages; page++) {
            html += `<button type="button" class="pm-pagination-btn${page === state.page ? " active" : ""}" data-page="${page}">${page}</button>`;
        }
        html += `<button type="button" class="pm-pagination-btn" data-page="${state.page + 1}" ${state.page === pages ? "disabled" : ""}>›</button>`;
        container.innerHTML = html;
    }

    function render() {
        const items = filtered();
        renderPagination(items.length);
        const page = items.slice((state.page - 1) * PAGE_SIZE, state.page * PAGE_SIZE);
        tableBody.innerHTML = page.map(rowHtml).join("");

        const count = $("visibleOrderCount");
        if (count) count.textContent = String(items.length);
        if (emptyState) emptyState.style.display = items.length === 0 ? "block" : "none";

        if (!state.orders.some(item => item.order.id === state.selectedId)) {
            state.selectedId = items[0]?.order.id || null;
        }
        renderDetail();
    }

    function setText(id, value) {
        const element = $(id);
        if (element) element.textContent = value;
    }

    function renderDetail() {
        const item = state.orders.find(entry => entry.order.id === state.selectedId);
        tableBody.querySelectorAll(".pm-order-row").forEach(row => row.classList.toggle("selected", row.dataset.orderId === state.selectedId));

        if (!item) {
            setText("detailOrderId", "Nema naloga");
            setText("detailCustomer", "Kreirajte prvi nalog dugmetom „Novi nalog“.");
            return;
        }

        const group = groupOf(item);
        const pickup = pickupOf(item);
        const delivery = deliveryOf(item);
        const badge = $("detailStatus");
        if (badge) {
            badge.className = "pm-orders-badge " + GROUP_BADGE[group][0];
            badge.textContent = GROUP_BADGE[group][1];
        }

        setText("detailOrderId", item.order.orderNumber);
        setText("detailCustomer", item.order.customerName + (item.tripNumber ? " · " + item.tripNumber : ""));
        setText("detailPickupLabel", pickup ? STOP_TYPES[pickup.type] : "Pickup");
        setText("detailPickupPlace", pickup ? [pickup.name, pickup.city, pickup.countryCode].filter(Boolean).join(", ") : "—");
        setText("detailPickupTime", pickup ? `${formatDate(pickup.plannedAt)} · ${formatTime(pickup.plannedAt)}` : "—");
        setText("detailDeliveryLabel", delivery ? STOP_TYPES[delivery.type] : "Delivery");
        setText("detailDeliveryPlace", delivery ? [delivery.name, delivery.city, delivery.countryCode].filter(Boolean).join(", ") : "—");
        setText("detailDeliveryTime", delivery ? `${formatDate(delivery.plannedAt)} · ${formatTime(delivery.plannedAt)}` : "—");
        setText("detailCargo", item.order.cargoDescription || "—");
        setText("detailOperation", (item.order.hazmat ? "ADR · " : "") + (item.order.cargoVolumeM3 != null ? item.order.cargoVolumeM3 + " m³" : "FTL"));
        setText("detailWeight", item.order.cargoWeightTons != null ? item.order.cargoWeightTons + " t" : "—");
        setText("detailPallets", item.order.pallets != null ? String(item.order.pallets) : "—");
        setText("detailTrip", item.tripNumber || "—");
        setText("detailVehicle", item.vehicleRegistration || "—");
        setText("detailDriver", item.driverName || "—");
        setText("detailProgress", item.tripProgressPercent != null ? Math.round(item.tripProgressPercent) + "%" : "—");
    }

    function renderSummary() {
        const groups = state.orders.map(groupOf);
        const count = group => groups.filter(g => g === group).length;
        const today = new Date();
        const deliveriesToday = state.orders.filter(item => {
            const delivery = deliveryOf(item);
            return delivery?.plannedAt && sameDay(new Date(delivery.plannedAt), today);
        });

        setText("kpiTotal", String(state.orders.length));
        setText("kpiTransit", String(count("in-transit")));
        setText("kpiPlanned", String(count("planned")));
        setText("kpiDelayed", String(count("delayed")));
        setText("kpiDeliveredToday", String(deliveriesToday.length));
        setText("kpiDeliveredMeta", `${deliveriesToday.filter(item => groupOf(item) === "delivered").length} uspešno završeno`);

        document.querySelectorAll(".pm-orders-status-chip").forEach(chip => {
            const counter = chip.querySelector(".count");
            if (counter) counter.textContent = String(chip.dataset.status === "all" ? state.orders.length : count(chip.dataset.status));
        });

        fillSelect(customerSelect, state.orders.map(item => item.order.customerName));
        fillSelect(pickupSelect, state.orders.map(item => place(pickupOf(item))).filter(value => value !== "—"));
    }

    function fillSelect(select, values) {
        if (!select) return;
        const current = select.value;
        const first = select.options[0];
        select.innerHTML = "";
        select.add(first);
        [...new Set(values)].sort((a, b) => a.localeCompare(b, "sr")).forEach(value => select.add(new Option(value, value)));
        select.value = current;
    }

    // ---- Data ----

    async function load() {
        const button = $("ordersRefresh");
        if (button) button.disabled = true;

        try {
            const response = await ProMapCargo.initialData.fetch("/api/business/orders/overview", { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            if (response.status === 401) throw new Error("Prijavite se da biste videli naloge.");
            if (!response.ok) throw new Error("Nalozi nisu učitani (" + response.status + ").");
            state.orders = await response.json();
            renderSummary();
            render();
        } catch (error) {
            tableBody.innerHTML = "";
            if (emptyState) {
                emptyState.textContent = error.message;
                emptyState.style.display = "block";
            }
        } finally {
            if (button) button.disabled = false;
        }
    }

    function editSelected(id) {
        const orderId = id || state.selectedId;
        if (orderId) window.location.href = "/orders/new?id=" + encodeURIComponent(orderId);
    }

    function resetFilters() {
        [searchInput, customerSelect, pickupSelect, statusSelect, dateSelect].forEach(input => { if (input) input.value = ""; });
        state.chip = "all";
        state.page = 1;
        document.querySelectorAll(".pm-orders-status-chip").forEach(chip => chip.classList.toggle("active", chip.dataset.status === "all"));
        render();
    }

    function bindEvents() {
        const rerender = () => { state.page = 1; render(); };
        searchInput?.addEventListener("input", rerender);
        [customerSelect, pickupSelect, statusSelect, dateSelect].forEach(select => select?.addEventListener("change", rerender));
        $("applyFilters")?.addEventListener("click", rerender);
        $("resetFilters")?.addEventListener("click", resetFilters);
        $("ordersRefresh")?.addEventListener("click", load);
        $("editOrderButton")?.addEventListener("click", () => editSelected());
        $("orderMoreButton")?.addEventListener("click", () => {
            const item = state.orders.find(entry => entry.order.id === state.selectedId);
            if (item?.tripId) window.location.href = "/trips";
            else editSelected();
        });

        document.querySelectorAll(".pm-orders-status-chip").forEach(chip => {
            chip.addEventListener("click", () => {
                document.querySelectorAll(".pm-orders-status-chip").forEach(item => item.classList.remove("active"));
                chip.classList.add("active");
                state.chip = chip.dataset.status || "all";
                rerender();
            });
        });

        $("ordersPagination")?.addEventListener("click", event => {
            const button = event.target.closest("[data-page]");
            if (!button || button.disabled) return;
            state.page = Number(button.dataset.page);
            render();
        });

        tableBody.addEventListener("click", event => {
            const row = event.target.closest(".pm-order-row");
            if (!row) return;
            const action = event.target.closest(".pm-orders-action")?.dataset.action;
            if (action === "edit") {
                editSelected(row.dataset.orderId);
                return;
            }
            state.selectedId = row.dataset.orderId;
            renderDetail();
        });
    }

    function initialize() {
        bindEvents();
        load();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
