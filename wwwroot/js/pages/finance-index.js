(() => {
    "use strict";

    // Finansije from the database (/api/finance/overview): driver balance, active and
    // previous trip, fuel card, ledger and pending costs. "Novi trošak" saves the cost
    // with its receipt; in dispatch mode pending costs can be approved or rejected.

    const page = document.querySelector('[data-module="finance"]');
    if (!page) {
        return;
    }

    const $ = id => document.getElementById(id);

    const modal = $("expenseModal");
    const expenseForm = $("expenseForm");
    const expenseCategory = $("expenseCategory");
    const expensePaymentMethod = $("expensePaymentMethod");
    const expenseCardField = $("expenseCardField");
    const fuelFields = $("fuelSpecificFields");
    const receiptInput = $("expenseReceipt");
    const searchInput = $("financeSearch");
    const categoryFilter = $("financeCategoryFilter");
    const statusFilter = $("financeStatusFilter");
    const table = $("financeTransactionTable");
    const emptyState = $("financeTableEmpty");
    const modeButtons = document.querySelectorAll("[data-finance-mode]");
    const refreshButton = $("financeRefreshButton");
    const exportButton = $("exportFinanceButton");
    const toast = $("financeToast");
    const toastTitle = $("financeToastTitle");
    const toastText = $("financeToastText");

    // Form categories → TransportCostCategory.
    const FORM_CATEGORY = {
        fuel: "Fuel", toll: "Toll", parking: "Parking", ferry: "Ferry", service: "Maintenance",
        adblue: "AdBlue", food: "Food", hotel: "Accommodation", other: "Other"
    };

    // TransportCostCategory → icon class, icon, Serbian name, filter group.
    const CATEGORY = {
        Fuel: ["fuel", "⛽", "Gorivo", "fuel"],
        AdBlue: ["fuel", "⛽", "AdBlue", "fuel"],
        Toll: ["toll", "⇄", "Putarina", "toll"],
        Parking: ["parking", "P", "Parking", "parking"],
        Allowance: ["allowance", "€", "Dnevnica", "allowance"],
        Payment: ["payment", "↗", "Isplata", "payment"],
        Border: ["other", "B", "Granica", "other"],
        Waiting: ["other", "◷", "Čekanje", "other"],
        Ferry: ["other", "⛴", "Trajekt", "other"],
        Driver: ["other", "◇", "Vozač", "other"],
        Maintenance: ["other", "⚙", "Servis / popravka", "other"],
        Food: ["other", "☕", "Hrana", "other"],
        Accommodation: ["other", "⌂", "Smeštaj", "other"],
        Other: ["other", "◇", "Ostalo", "other"]
    };

    const STATUS = {
        Approved: ["finance-status-success", "APPROVED"],
        Pending: ["finance-status-warning", "PENDING"],
        Paid: ["finance-status-info", "PAID"],
        Rejected: ["finance-status-warning", "REJECTED"]
    };

    const PAYMENT = {
        "fuel-card": "Fuel kartica",
        "company-card": "Službena kartica",
        "cash": "Gotovina",
        "driver-card": "Lična kartica vozača"
    };

    const TRIP_STATUS = { Active: "U TOKU", Paused: "PAUZA", Planned: "PLANIRANA", Completed: "CLOSED", Cancelled: "OTKAZANA" };

    const money = new Intl.NumberFormat("sr-RS", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const liters = new Intl.NumberFormat("sr-RS", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    const state = {
        mode: "driver",
        data: null,
        canReview: false
    };

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function eur(value, signed = false) {
        if (value === null || value === undefined) return "—";
        const number = Number(value);
        const text = `${money.format(Math.abs(number))} €`;
        if (!signed) return (number < 0 ? "-" : "") + text;
        return (number < 0 ? "-" : "+") + text;
    }

    function dateTime(iso) {
        if (!iso) return "—";
        const d = new Date(iso);
        return `${d.toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit", year: "numeric" })} · ${d.toLocaleTimeString("sr-Latn-RS", { hour: "2-digit", minute: "2-digit" })}`;
    }

    function setText(id, value) {
        const element = $(id);
        if (element) element.textContent = value;
    }

    // ---- Number animation and progress (unchanged behaviour) ----

    function animateFinanceValues() {
        document.querySelectorAll("[data-finance-value]").forEach(element => {
            const target = Number(element.dataset.financeValue || 0);
            const duration = 900;
            const started = performance.now();
            const frame = now => {
                const progress = Math.min((now - started) / duration, 1);
                const eased = 1 - Math.pow(1 - progress, 3);
                element.textContent = `${money.format(target * eased)} €`;
                if (progress < 1) requestAnimationFrame(frame);
            };
            requestAnimationFrame(frame);
        });
    }

    function animateProgressBars() {
        requestAnimationFrame(() => {
            document.querySelectorAll(".finance-progress-bar").forEach(bar => {
                const width = Number(bar.dataset.width || 0);
                setTimeout(() => { bar.style.width = `${width}%`; }, 180);
            });
        });
    }

    // ---- Rendering ----

    function renderContext(data) {
        const driver = data.driver;
        const trip = data.activeTrip;
        const initials = driver ? driver.fullName.split(" ").map(part => part[0]).join("").slice(0, 2).toUpperCase() : "—";
        setText("financeContextInitials", data.mode === "dispatch" ? "D" : initials);
        setText("financeContextTitle", data.mode === "dispatch" ? "Dispatch · svi vozači" : (driver?.fullName || "Nema vozača"));
        setText("financeContextDriver", driver ? `Vozač ${driver.employeeNumber || ""}`.trim() : "—");
        setText("financeContextTrip", trip ? `Aktivna tura ${trip.number}` : "Nema aktivne ture");
        setText("financeContextVehicle", trip?.vehicle || data.fuelCard?.vehicle || "—");

        // A driver account sees only its own overview.
        modeButtons.forEach(button => {
            button.hidden = data.isDriverAccount && button.dataset.financeMode === "dispatch";
            button.classList.toggle("active", button.dataset.financeMode === data.mode);
        });
        page.classList.toggle("finance-dispatch-mode", data.mode === "dispatch");
    }

    function renderKpi(data) {
        const kpi = data.kpi;
        const balance = $("kpiBalance");
        if (balance) balance.dataset.financeValue = String(kpi.balance);
        setText("kpiBalanceDelta", `${eur(kpi.monthDelta, true)} ovaj mesec`);
        setText("kpiActiveRemaining", kpi.activeRemaining === null ? "—" : eur(kpi.activeRemaining));
        setText("kpiActiveTrip", kpi.activeTripNumber || "Nema ture");
        setText("kpiPrevious", kpi.previousNet === null ? "—" : eur(kpi.previousNet, true));
        setText("kpiPreviousTrip", kpi.previousTripNumber || "—");
        setText("kpiTripCosts", eur(kpi.tripCosts));
        setText("kpiTripCostCount", `${kpi.tripCostCount} transakcija`);
        setText("kpiPending", eur(kpi.pendingAmount));
        setText("kpiPendingCount", `${kpi.pendingCount} troška`);
    }

    function renderTrip(trip) {
        setText("tripNumber", trip?.number || "—");
        setText("tripRoute", trip?.route || "Nema aktivne ture");
        setText("tripStatus", trip ? (TRIP_STATUS[trip.status] || trip.status) : "—");
        setText("tripDriver", trip?.driver || "—");
        setText("tripVehicle", trip?.vehicle || "—");
        setText("tripStart", dateTime(trip?.startedAt));
        setText("tripArrival", dateTime(trip?.plannedArrivalAt));
        setText("tripBudget", trip?.budget != null ? eur(trip.budget) : "—");
        setText("tripSpent", trip ? eur(trip.spent) : "—");

        const used = trip?.budget ? Math.min(100, (trip.spent / trip.budget) * 100) : 0;
        const bar = $("tripProgress");
        if (bar) bar.dataset.width = used.toFixed(1);
        setText("tripBudgetUsed", trip ? `${used.toFixed(1).replace(".", ",")}% budžeta iskorišćeno` : "—");
        setText("tripRemaining", trip ? `${eur(trip.remaining)} preostalo` : "—");

        const by = trip?.byCategory || {};
        setText("tripFuel", eur(by.fuel || 0));
        setText("tripToll", eur(by.toll || 0));
        setText("tripParking", eur(by.parking || 0));
        setText("tripOther", eur(by.other || 0));
    }

    function renderFuel(card) {
        const label = card?.label || "";
        const provider = label.split(" ")[0] || "—";
        const last = (label.match(/(\d{4})\s*$/) || [])[1] || "••••";
        setText("fuelCardProvider", provider);
        $("fuelCardNumber").innerHTML = `•••• &nbsp; •••• &nbsp; •••• &nbsp; ${escapeHtml(last)}`;
        setText("fuelCardVehicle", card?.vehicle || "—");
        setText("fuelCardDriver", card?.driver || "—");
        setText("fuelCardStatus", card ? (card.active ? "ACTIVE" : "BLOCKED") : "—");
        setText("fuelLiters", card ? `${liters.format(card.liters)} L` : "—");
        setText("fuelAmount", card ? eur(card.amount) : "—");
        setText("fuelAverage", card && card.averagePrice ? `${Number(card.averagePrice).toFixed(3).replace(".", ",")} €/L` : "—");
        setText("fuelCount", String(card?.transactions?.length || 0));

        $("fuelTransactions").innerHTML = (card?.transactions || []).map(item => {
            const [statusClass, statusText] = STATUS[item.status] || STATUS.Pending;
            return `
                <div class="finance-fuel-row">
                    <div class="finance-transaction-icon fuel">⛽</div>
                    <div class="finance-transaction-main">
                        <strong>${escapeHtml(item.location || "Gorivo")}</strong>
                        <span>${escapeHtml(dateTime(item.at))}${item.countryCode ? " · " + escapeHtml(item.countryCode) : ""}</span>
                    </div>
                    <div class="finance-fuel-card-label">
                        <small>Kartica</small>
                        <strong>${escapeHtml(item.cardLabel || "—")}</strong>
                    </div>
                    <div class="finance-fuel-volume">
                        <strong>${liters.format(item.quantity)} L</strong>
                        <span>${Number(item.unitPrice).toFixed(3).replace(".", ",")} €/L</span>
                    </div>
                    <div class="finance-transaction-amount">
                        <strong>-${eur(item.amount)}</strong>
                        <span class="finance-status ${statusClass}">${statusText}</span>
                    </div>
                </div>`;
        }).join("") || '<div class="finance-fuel-row"><div class="finance-transaction-main"><strong>Nema transakcija goriva</strong><span>Aktivna tura</span></div></div>';
    }

    function rowHtml(item) {
        const [iconClass, icon, name, group] = CATEGORY[item.category] || CATEGORY.Other;
        const [statusClass, statusText] = STATUS[item.status] || STATUS.Pending;
        const positive = item.category === "Allowance";
        const detail = item.category === "Fuel" || item.category === "AdBlue" ? `${name} · ${liters.format(item.quantity)} L` : name;
        const payment = item.cardLabel || PAYMENT[item.paymentMethod] || "—";
        const at = new Date(item.at);
        const review = state.canReview && item.status === "Pending"
            ? `<div class="finance-table-sub"><button type="button" class="finance-btn finance-btn-small" data-review="Approved" data-id="${item.id}">Odobri</button> <button type="button" class="finance-btn finance-btn-small" data-review="Rejected" data-id="${item.id}">Odbij</button></div>`
            : "";
        const receipt = item.receiptFileId ? ` · <a href="/api/files/${item.receiptFileId}" target="_blank" rel="noopener">račun</a>` : "";
        const search = [item.location, name, payment, item.tripNumber, item.vehicle, item.driver, item.notes].join(" ").toLowerCase();

        return `
            <tr data-category="${group}" data-status="${item.status.toLowerCase()}" data-search="${escapeHtml(search)}">
                <td>
                    <div class="finance-table-transaction">
                        <span class="finance-transaction-icon ${iconClass}">${icon}</span>
                        <div>
                            <strong>${escapeHtml(item.location || name)}</strong>
                            <span>${escapeHtml(detail)}${state.data?.mode === "dispatch" && item.driver ? " · " + escapeHtml(item.driver) : ""}${receipt}</span>
                        </div>
                    </div>
                </td>
                <td><strong>${escapeHtml(item.tripNumber || "—")}</strong></td>
                <td><span class="finance-payment-method">${escapeHtml(payment)}</span></td>
                <td>
                    <strong>${at.toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit", year: "numeric" })}</strong>
                    <span class="finance-table-sub">${at.toLocaleTimeString("sr-Latn-RS", { hour: "2-digit", minute: "2-digit" })}</span>
                </td>
                <td><span class="finance-status ${statusClass}">${statusText}</span>${review}</td>
                <td class="align-right">
                    <strong class="${positive ? "finance-positive" : "finance-negative"}">${positive ? "+" : "-"}${eur(item.amount)}</strong>
                </td>
            </tr>`;
    }

    function renderSide(data) {
        const balance = data.balance;
        const total = balance.receivables - balance.cashAdvances - balance.payments;
        setText("balanceRing", eur(total, true));
        setText("balanceRingNote", total >= 0 ? "u korist vozača" : "dug vozača");
        setText("balanceReceivables", eur(balance.receivables, true));
        setText("balanceAdvances", eur(-balance.cashAdvances, true));
        setText("balancePayments", eur(-balance.payments, true));
        setText("balanceTotal", eur(total, true));

        const previous = data.previousTrip;
        setText("previousNumber", previous?.number || "—");
        setText("previousRoute", previous?.route || "Nema završene ture");
        setText("previousStatus", previous ? (TRIP_STATUS[previous.status] || previous.status) : "—");
        setText("previousBudget", previous?.budget != null ? eur(previous.budget) : "—");
        setText("previousSpent", previous ? eur(previous.spent) : "—");
        setText("previousNet", previous ? eur(previous.remaining, true) : "—");

        const pending = data.transactions.filter(item => item.status === "Pending");
        setText("pendingCount", String(pending.length));
        $("pendingList").innerHTML = pending.slice(0, 6).map(item => {
            const [iconClass, icon, name] = CATEGORY[item.category] || CATEGORY.Other;
            const at = new Date(item.at).toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit" });
            return `
                <button class="finance-pending-item" type="button" data-search-for="${escapeHtml(item.location || name)}">
                    <span class="finance-transaction-icon ${iconClass}">${icon}</span>
                    <span class="finance-pending-main">
                        <strong>${escapeHtml(item.location || name)}</strong>
                        <small>${at} · ${escapeHtml(item.tripNumber || "bez ture")}</small>
                    </span>
                    <span class="finance-pending-value">${eur(item.amount)}</span>
                </button>`;
        }).join("") || '<div class="finance-pending-item"><span class="finance-pending-main"><strong>Nema troškova na čekanju</strong></span></div>';

        $("assignedCards").innerHTML = data.options.cards.map(card => {
            const provider = (card.label.split(" ")[0] || "").toLowerCase();
            const logo = provider === "company" ? "visa" : provider;
            return `
                <div class="finance-assigned-card">
                    <span class="finance-assigned-logo ${escapeHtml(logo)}">${escapeHtml(provider === "company" ? "VISA" : provider.toUpperCase())}</span>
                    <div>
                        <strong>${escapeHtml(card.label)}</strong>
                        <span>${provider === "company" ? "Company payment card" : "Fuel card"} · ${escapeHtml(card.vehicle)}</span>
                    </div>
                    <span class="finance-dot-online"${card.active ? "" : ' style="opacity:.3"'}></span>
                </div>`;
        }).join("") || '<div class="finance-assigned-card"><div><strong>Nema dodeljenih kartica</strong><span>Kartica se upisuje na vozilu</span></div></div>';
    }

    function fillFormOptions(options) {
        const fill = (select, items, first) => {
            if (!select) return;
            const current = select.value;
            select.innerHTML = "";
            if (first) select.add(new Option(first, ""));
            items.forEach(([value, label]) => select.add(new Option(label, value)));
            if ([...select.options].some(option => option.value === current)) select.value = current;
        };

        fill($("expenseTrip"), options.trips.map(trip => [trip.id, trip.tripNumber || trip.id.slice(0, 8)]), "Bez ture");
        fill($("expenseVehicle"), options.vehicles.map(vehicle => [vehicle.id, vehicle.registration]), "Vozilo ture");
        fill($("expenseCard"), options.cards.map(card => [card.label, card.label]), null);

        // Active trip first: most costs belong to it.
        const active = options.trips.find(trip => trip.status === "Active" || trip.status === "Paused");
        if (active && !$("expenseTrip").value) $("expenseTrip").value = active.id;
    }

    function render() {
        const data = state.data;
        renderContext(data);
        renderKpi(data);
        renderTrip(data.activeTrip);
        renderFuel(data.fuelCard);
        table.innerHTML = data.transactions.map(rowHtml).join("");
        renderSide(data);
        fillFormOptions(data.options);
        filterTransactions();
        animateFinanceValues();
        animateProgressBars();
    }

    // ---- Data ----

    async function load() {
        if (refreshButton) refreshButton.disabled = true;
        try {
            const response = await ProMapCargo.initialData.fetch(`/api/finance/overview?mode=${state.mode}`, { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            if (response.status === 401) throw new Error("Prijavite se da biste videli finansije.");
            if (!response.ok) throw new Error(`Finansije nisu učitane (${response.status}).`);
            state.data = await response.json();
            state.mode = state.data.mode;
            state.canReview = state.data.canReview === true;
            render();
        } catch (error) {
            showToast("Greška", error.message);
        } finally {
            if (refreshButton) refreshButton.disabled = false;
        }
    }

    async function review(id, status) {
        const response = await fetch(`/api/finance/costs/${id}/status`, {
            method: "POST",
            headers: { "Content-Type": "application/json", "Accept": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify({ status })
        });
        if (!response.ok) {
            showToast("Greška", response.status === 403 ? "Odobravanje je dozvoljeno dispečeru i administratoru." : "Status troška nije promenjen.");
            return;
        }
        showToast(status === "Approved" ? "Trošak odobren" : "Trošak odbijen", "Promena je sačuvana.");
        load();
    }

    // ---- Modal and form ----

    function localNow() {
        const now = new Date();
        return new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    }

    function openExpenseModal() {
        modal.classList.add("open");
        modal.setAttribute("aria-hidden", "false");
        document.body.style.overflow = "hidden";
        if (!$("expenseDate").value) $("expenseDate").value = localNow();
        updateExpenseFields();
    }

    function closeExpenseModal() {
        modal.classList.remove("open");
        modal.setAttribute("aria-hidden", "true");
        document.body.style.overflow = "";
    }

    function updateExpenseFields() {
        fuelFields.style.display = expenseCategory.value === "fuel" ? "block" : "none";
        expenseCardField.style.display = expensePaymentMethod.value !== "cash" ? "block" : "none";
    }

    // Fuel: amount = liters × price when the amount is left empty.
    function syncFuelAmount() {
        const quantity = Number($("expenseQuantity").value);
        const price = Number($("expenseUnitPrice").value);
        if (expenseCategory.value === "fuel" && quantity > 0 && price > 0) {
            $("expenseAmount").value = (quantity * price).toFixed(2);
        }
    }

    async function saveExpense(event) {
        event.preventDefault();

        const amount = Number($("expenseAmount").value);
        if (!(amount > 0)) {
            showToast("Nedostaje iznos", "Unesite iznos troška veći od nule.");
            $("expenseAmount").focus();
            return;
        }

        const fuel = expenseCategory.value === "fuel";
        const body = new FormData();
        body.append("category", FORM_CATEGORY[expenseCategory.value] || "Other");
        body.append("amount", String(amount));
        const optional = {
            tripId: $("expenseTrip").value,
            location: $("expenseLocation").value.trim(),
            paymentMethod: expensePaymentMethod.value,
            cardLabel: expensePaymentMethod.value === "cash" ? "" : $("expenseCard").value,
            notes: $("expenseNotes").value.trim(),
            quantity: fuel ? $("expenseQuantity").value : "",
            unitPrice: fuel ? $("expenseUnitPrice").value : "",
            odometerKm: fuel ? $("expenseOdometer").value : "",
            vehicleId: fuel ? $("expenseVehicle").value : ""
        };
        Object.entries(optional).forEach(([key, value]) => { if (value) body.append(key, value); });
        if ($("expenseDate").value) body.append("incurredAt", new Date($("expenseDate").value).toISOString());
        if (receiptInput.files[0]) body.append("receipt", receiptInput.files[0]);

        const saveButton = expenseForm.querySelector('button[type="submit"]');
        const original = saveButton.innerHTML;
        saveButton.disabled = true;
        saveButton.innerHTML = "Čuvanje...";

        try {
            const response = await fetch("/api/finance/costs", { method: "POST", body, credentials: "same-origin" });
            const data = await response.json().catch(() => null);
            if (!response.ok) throw new Error(data?.message || data?.title || "Trošak nije sačuvan.");

            closeExpenseModal();
            showToast("Trošak evidentiran", "Trošak je sačuvan i čeka proveru.");
            expenseForm.reset();
            setText("expenseReceiptName", "Dodaj račun ili dokument");
            updateExpenseFields();
            load();
        } catch (error) {
            showToast("Greška", error.message);
        } finally {
            saveButton.disabled = false;
            saveButton.innerHTML = original;
        }
    }

    // ---- Filters ----

    function filterTransactions() {
        const query = (searchInput.value || "").trim().toLowerCase();
        const category = categoryFilter.value;
        const status = statusFilter.value;
        let visible = 0;

        [...table.querySelectorAll("tr")].forEach(row => {
            const show = (!query || (row.dataset.search || row.textContent).toLowerCase().includes(query))
                && (category === "all" || row.dataset.category === category)
                && (status === "all" || row.dataset.status === status);
            row.style.display = show ? "" : "none";
            if (show) visible++;
        });

        emptyState.style.display = visible === 0 ? "block" : "none";
    }

    function exportCsv() {
        const rows = (state.data?.transactions || []).map(item => [
            new Date(item.at).toISOString().slice(0, 16).replace("T", " "),
            (CATEGORY[item.category] || CATEGORY.Other)[2], item.location || "", item.tripNumber || "", item.vehicle || "", item.driver || "",
            PAYMENT[item.paymentMethod] || "", item.cardLabel || "", item.status, String(item.amount).replace(".", ","), item.currency
        ]);
        const header = ["Datum", "Kategorija", "Lokacija", "Tura", "Vozilo", "Vozač", "Plaćanje", "Kartica", "Status", "Iznos", "Valuta"];
        const csv = [header, ...rows].map(row => row.map(value => `"${String(value).replaceAll('"', '""')}"`).join(";")).join("\n");
        const link = document.createElement("a");
        link.href = URL.createObjectURL(new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8" }));
        link.download = `finansije-${new Date().toISOString().slice(0, 10)}.csv`;
        link.click();
        URL.revokeObjectURL(link.href);
    }

    // ---- Toast ----

    let toastTimeout = null;

    function showToast(title, text) {
        toastTitle.textContent = title;
        toastText.textContent = text;
        toast.classList.add("show");
        if (toastTimeout) clearTimeout(toastTimeout);
        toastTimeout = setTimeout(() => toast.classList.remove("show"), 3200);
    }

    // ---- Events ----

    $("openExpenseModal")?.addEventListener("click", openExpenseModal);
    document.querySelectorAll("[data-close-expense-modal]").forEach(button => button.addEventListener("click", closeExpenseModal));
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && modal.classList.contains("open")) closeExpenseModal();
    });
    expenseCategory?.addEventListener("change", updateExpenseFields);
    expensePaymentMethod?.addEventListener("change", updateExpenseFields);
    $("expenseQuantity")?.addEventListener("input", syncFuelAmount);
    $("expenseUnitPrice")?.addEventListener("input", syncFuelAmount);
    $("expenseReceiptButton")?.addEventListener("click", () => receiptInput.click());
    receiptInput?.addEventListener("change", () => {
        const file = receiptInput.files[0];
        setText("expenseReceiptName", file ? `${file.name} · ${Math.round(file.size / 1024)} KB` : "Dodaj račun ili dokument");
    });
    expenseForm?.addEventListener("submit", saveExpense);

    searchInput?.addEventListener("input", filterTransactions);
    categoryFilter?.addEventListener("change", filterTransactions);
    statusFilter?.addEventListener("change", filterTransactions);

    table.addEventListener("click", event => {
        const button = event.target.closest("[data-review]");
        if (button) review(button.dataset.id, button.dataset.review);
    });

    $("pendingList")?.addEventListener("click", event => {
        const item = event.target.closest("[data-search-for]");
        if (!item) return;
        searchInput.value = item.dataset.searchFor;
        statusFilter.value = "pending";
        filterTransactions();
        table.scrollIntoView({ behavior: "smooth", block: "start" });
    });

    modeButtons.forEach(button => {
        button.addEventListener("click", () => {
            state.mode = button.dataset.financeMode;
            load();
        });
    });

    refreshButton?.addEventListener("click", () => load().then(() => showToast("Finansije osvežene", "Podaci su ponovo učitani.")));
    exportButton?.addEventListener("click", exportCsv);

    $("previousTripDetails")?.addEventListener("click", () => {
        const number = state.data?.previousTrip?.number;
        if (!number) return;
        searchInput.value = number;
        categoryFilter.value = "all";
        statusFilter.value = "all";
        filterTransactions();
        table.scrollIntoView({ behavior: "smooth", block: "start" });
    });

    updateExpenseFields();
    load();
})();
