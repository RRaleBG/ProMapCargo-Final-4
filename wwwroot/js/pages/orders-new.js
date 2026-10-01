(() => {
    "use strict";

    const root = window.ProMapCargo = window.ProMapCargo || {};

    const state = {
        initialized: false,
        activeTab: "basic",
        stopIndex: 0,
        stopTypes: [
            ["Loading", "Utovar"],
            ["Unloading", "Istovar"],
            ["LoadingAndUnloading", "Utovar + istovar"],
            ["Border", "Granica"],
            ["Rest", "Odmor"],
            ["Fuel", "Gorivo"],
            ["Other", "Ostalo"]
        ]
    };

    const tabs = ["basic", "cargo", "stops", "review"];
    const $ = id => document.getElementById(id);

    function toast(type, message) {
        const service = root.toast;
        if (service?.[type]) {
            service[type](message);
        }
    }

    function validation() {
        return root.validation;
    }

    function setFieldError(input, message) {
        validation()?.setFieldError(input, message);
    }

    function clearErrors() {
        document.querySelectorAll("#orderForm input, #orderForm select, #orderForm textarea")
            .forEach(input => setFieldError(input, ""));
    }

    function setActiveTab(tabName) {
        if (!tabs.includes(tabName)) {
            tabName = "basic";
        }

        state.activeTab = tabName;

        document.querySelectorAll("[data-order-tab]").forEach(tab => {
            const active = tab.dataset.orderTab === tabName;
            tab.classList.toggle("is-active", active);
            tab.setAttribute("aria-selected", String(active));
            tab.tabIndex = active ? 0 : -1;
        });

        document.querySelectorAll("[data-order-panel]").forEach(panel => {
            const active = panel.dataset.orderPanel === tabName;
            panel.hidden = !active;
            panel.classList.toggle("is-active", active);
        });

        const step = tabs.indexOf(tabName) + 1;
        const status = $("orderStepStatus");
        if (status) {
            status.textContent = `Korak ${step} od ${tabs.length}`;
        }

        if (tabName === "review") {
            updateReview();
        }
    }

    function validateBasic(showToast = true) {
        let valid = true;
        const orderNumber = $("orderNumber");
        const customerName = $("customerName");

        if (!validation()?.required(orderNumber?.value)) {
            setFieldError(orderNumber, "Broj naloga je obavezan.");
            valid = false;
        }

        if (!validation()?.required(customerName?.value)) {
            setFieldError(customerName, "Kupac je obavezan.");
            valid = false;
        }

        if (!valid && showToast) {
            setActiveTab("basic");
            toast("warning", "Popunite obavezna polja u osnovnim podacima.");
        }

        return valid;
    }

    function validateCargo(showToast = true) {
        let valid = true;

        [
            [$("cargoWeightTons"), "Težina ne može biti negativna."],
            [$("cargoVolumeM3"), "Zapremina ne može biti negativna."],
            [$("pallets"), "Broj paleta ne može biti negativan."]
        ].forEach(([input, message]) => {
            if (!input?.value.trim()) {
                return;
            }

            const value = Number(input.value);
            if (!Number.isFinite(value) || value < 0) {
                setFieldError(input, message);
                valid = false;
            }
        });

        if (!valid && showToast) {
            setActiveTab("cargo");
            toast("warning", "Proverite podatke o teretu.");
        }

        return valid;
    }

    function getStopCards() {
        return [...document.querySelectorAll("[data-stop-card]")];
    }

    function stopValue(card, field) {
        return card.querySelector(`[data-stop-field="${field}"]`)?.value.trim() ?? "";
    }

    function readStop(card) {
        const planned = stopValue(card, "planned");

        return {
            type: stopValue(card, "type") || "Other",
            name: stopValue(card, "name"),
            address: stopValue(card, "address"),
            city: stopValue(card, "city"),
            countryCode: stopValue(card, "country").toUpperCase() || null,
            latitude: Number(stopValue(card, "latitude")),
            longitude: Number(stopValue(card, "longitude")),
            plannedAt: planned ? new Date(planned).toISOString() : null,
            serviceMinutes: Number(stopValue(card, "service") || 0),
            notes: stopValue(card, "notes")
        };
    }

    function collectStops() {
        return getStopCards().map(readStop);
    }

    function validateStops(showToast = true) {
        const cards = getStopCards();

        if (cards.length < 2) {
            if (showToast) {
                setActiveTab("stops");
                toast("warning", "Nalog mora imati najmanje dve transportne tačke.");
            }
            return false;
        }

        const stops = collectStops();
        let valid = true;

        const hasLoading = stops.some(stop =>
            stop.type === "Loading" || stop.type === "LoadingAndUnloading"
        );
        const hasUnloading = stops.some(stop =>
            stop.type === "Unloading" || stop.type === "LoadingAndUnloading"
        );

        if (!hasLoading || !hasUnloading) {
            valid = false;
            toast("warning", "Ruta mora imati najmanje jedan utovar i jedan istovar.");
        }

        cards.forEach((card, index) => {
            const stop = stops[index];

            const fields = {
                name: card.querySelector('[data-stop-field="name"]'),
                country: card.querySelector('[data-stop-field="country"]'),
                latitude: card.querySelector('[data-stop-field="latitude"]'),
                longitude: card.querySelector('[data-stop-field="longitude"]'),
                service: card.querySelector('[data-stop-field="service"]')
            };

            if (!validation()?.required(stop.name)) {
                setFieldError(fields.name, "Naziv je obavezan.");
                valid = false;
            }

            if (!validation()?.lat(stop.latitude)) {
                setFieldError(fields.latitude, "Latitude mora biti između -90 i 90.");
                valid = false;
            }

            if (!validation()?.lon(stop.longitude)) {
                setFieldError(fields.longitude, "Longitude mora biti između -180 i 180.");
                valid = false;
            }

            if (!/^[A-Z]{2}$/.test(stop.countryCode || "")) {
                setFieldError(fields.country, "Država mora biti ISO kod od 2 slova.");
                valid = false;
            }

            if (!Number.isInteger(stop.serviceMinutes) ||
                stop.serviceMinutes < 0 ||
                stop.serviceMinutes > 1440) {
                setFieldError(fields.service, "Vreme servisa mora biti 0–1440 minuta.");
                valid = false;
            }
        });

        if (!valid && showToast) {
            setActiveTab("stops");
            toast("warning", "Proverite označena polja u transportnoj ruti.");
        }

        return valid;
    }

    function validateAll(showToast = true) {
        clearErrors();

        if (!validateBasic(showToast)) return false;
        if (!validateCargo(showToast)) return false;
        if (!validateStops(showToast)) return false;

        return true;
    }

    function createStop(data = {}) {
        const container = $("stops");
        if (!container) return null;

        const index = state.stopIndex++;
        const card = document.createElement("article");
        card.className = "pm-card";
        card.dataset.stopCard = "true";

        const header = document.createElement("header");
        header.className = "pm-card-header";

        const heading = document.createElement("div");

        const title = document.createElement("h3");
        title.className = "pm-card-title";
        title.textContent = "Transportna tačka ";

        const number = document.createElement("span");
        number.dataset.stopNumber = "true";
        title.appendChild(number);

        const subtitle = document.createElement("p");
        subtitle.className = "pm-card-subtitle";
        subtitle.textContent = "Lokacija, operacija i planirano vreme.";

        heading.append(title, subtitle);

        const actions = document.createElement("div");
        actions.className = "pm-card-actions";

        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "pm-btn pm-btn-danger pm-btn-sm";
        remove.textContent = "Ukloni";
        remove.dataset.removeStop = "true";
        actions.appendChild(remove);

        header.append(heading, actions);

        const body = document.createElement("div");
        body.className = "pm-card-body";

        const grid1 = document.createElement("div");
        grid1.className = "pm-form-grid pm-form-grid-3";

        const type = createField(index, "type", "Tip *", "select");
        state.stopTypes.forEach(([value, label]) => {
            const option = document.createElement("option");
            option.value = value;
            option.textContent = label;
            type.input.appendChild(option);
        });

        const name = createField(index, "name", "Naziv *");
        const city = createField(index, "city", "Grad");
        grid1.append(type.wrapper, name.wrapper, city.wrapper);

        const grid2 = document.createElement("div");
        grid2.className = "pm-form-grid pm-form-grid-3 pm-mt-4";
        const address = createField(index, "address", "Adresa");
        const country = createField(index, "country", "Država *");
        const service = createField(index, "service", "Servis (min) *", "number");
        service.input.min = "0";
        service.input.max = "1440";
        service.input.step = "1";
        service.input.value = "30";
        country.input.maxLength = 2;
        country.input.value = "RS";
        grid2.append(address.wrapper, country.wrapper, service.wrapper);

        const grid3 = document.createElement("div");
        grid3.className = "pm-form-grid pm-form-grid-3 pm-mt-4";
        const planned = createField(index, "planned", "Planirano vreme", "datetime-local");
        const latitude = createField(index, "latitude", "Latitude *", "number");
        const longitude = createField(index, "longitude", "Longitude *", "number");
        latitude.input.step = "any";
        longitude.input.step = "any";
        grid3.append(planned.wrapper, latitude.wrapper, longitude.wrapper);

        const notes = createField(index, "notes", "Napomena", "textarea");
        notes.input.rows = 3;

        body.append(grid1, grid2, grid3, notes.wrapper);
        card.append(header, body);

        setStopValues(card, data);

        remove.addEventListener("click", () => {
            if (getStopCards().length <= 2) {
                toast("warning", "Nalog mora zadržati najmanje dve transportne tačke.");
                return;
            }

            card.remove();
            renumberStops();
            updateReview();
        });

        container.appendChild(card);
        renumberStops();
        return card;
    }

    function createField(index, field, label, type = "text") {
        const wrapper = document.createElement("div");
        wrapper.className = "pm-field";

        const inputId = `stop-${index}-${field}`;

        const labelElement = document.createElement("label");
        labelElement.className = "pm-label";
        labelElement.htmlFor = inputId;
        labelElement.textContent = label;

        let input;
        if (type === "select") {
            input = document.createElement("select");
            input.className = "pm-select";
        } else if (type === "textarea") {
            input = document.createElement("textarea");
            input.className = "pm-textarea";
        } else {
            input = document.createElement("input");
            input.className = "pm-input";
            input.type = type;
        }

        input.id = inputId;
        input.dataset.stopField = field;

        const error = document.createElement("div");
        error.id = `${inputId}-error`;
        error.className = "pm-error";
        error.hidden = true;

        wrapper.append(labelElement, input, error);
        return { wrapper, input };
    }

    function setStopValues(card, data) {
        const set = (field, value) => {
            const element = card.querySelector(`[data-stop-field="${field}"]`);
            if (element && value !== undefined && value !== null) {
                element.value = value;
            }
        };

        set("type", data.type || "Other");
        set("name", data.name || "");
        set("address", data.address || "");
        set("city", data.city || "");
        set("country", data.countryCode || "RS");
        set("service", data.serviceMinutes ?? 30);
        set("latitude", data.latitude);
        set("longitude", data.longitude);
        set("notes", data.notes || "");

        if (data.plannedAt) {
            const date = new Date(data.plannedAt);
            if (!Number.isNaN(date.getTime())) {
                const offset = date.getTimezoneOffset();
                set("planned", new Date(date.getTime() - offset * 60000).toISOString().slice(0, 16));
            }
        }
    }

    function renumberStops() {
        getStopCards().forEach((card, index) => {
            const number = card.querySelector("[data-stop-number]");
            if (number) number.textContent = String(index + 1);
        });
    }

    function updateReview() {
        const priority = $("priority");
        const priorityText = priority?.options[priority.selectedIndex]?.text || "Normalan";

        $("reviewOrderNumber").value = $("orderNumber")?.value.trim() || "—";
        $("reviewCustomerName").value = $("customerName")?.value.trim() || "—";
        $("reviewPriority").value = priorityText;
        $("reviewWeight").value = $("cargoWeightTons")?.value ? `${$("cargoWeightTons").value} t` : "—";
        $("reviewVolume").value = $("cargoVolumeM3")?.value ? `${$("cargoVolumeM3").value} m³` : "—";
        $("reviewPallets").value = $("pallets")?.value || "—";
        $("reviewHazmat").value = $("hazmat")?.checked ? "Da" : "Ne";

        const reviewStops = $("reviewStops");
        if (!reviewStops) return;

        reviewStops.replaceChildren();

        collectStops().forEach((stop, index) => {
            const item = document.createElement("article");
            item.className = "pm-card";

            const body = document.createElement("div");
            body.className = "pm-card-body";

            const label = document.createElement("div");
            label.className = "pm-label";
            label.textContent = `Tačka ${index + 1} · ${stop.type}`;

            const name = document.createElement("strong");
            name.textContent = stop.name || "Bez naziva";

            const address = document.createElement("div");
            address.className = "pm-help";
            address.textContent = [stop.address, stop.city, stop.countryCode]
                .filter(Boolean)
                .join(", ") || "Lokacija nije navedena";

            body.append(label, name, address);
            item.appendChild(body);
            reviewStops.appendChild(item);
        });
    }

    function buildPayload() {
        return {
            orderNumber: valueOf("orderNumber"),
            customerName: valueOf("customerName"),
            priority: $("priority")?.value || "Normal",
            cargoDescription: valueOf("cargoDescription") || null,
            cargoWeightTons: nullableNumber("cargoWeightTons"),
            cargoVolumeM3: nullableNumber("cargoVolumeM3"),
            pallets: nullableInteger("pallets"),
            hazmat: $("hazmat")?.checked === true,
            stops: collectStops()
        };
    }

    function valueOf(id) {
        return $(id)?.value.trim() || "";
    }

    function nullableNumber(id) {
        const value = valueOf(id);
        return value === "" ? null : Number(value);
    }

    function nullableInteger(id) {
        const value = valueOf(id);
        return value === "" ? null : Number.parseInt(value, 10);
    }

    async function saveOrder(event) {
        event.preventDefault();

        if (!validateAll(true)) return;

        const button = $("saveOrder");
        if (!button) return;

        const original = button.innerHTML;
        button.disabled = true;
        button.textContent = "Čuvanje…";

        try {
            const response = await fetch("/api/business/orders", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                credentials: "same-origin",
                body: JSON.stringify(buildPayload())
            });

            const data = await response.json().catch(() => null);

            if (!response.ok) {
                throw new Error(
                    data?.message ||
                    data?.title ||
                    "Transportni nalog nije moguće sačuvati."
                );
            }

            toast("success", "Transportni nalog je uspešno kreiran.");

            window.setTimeout(() => {
                window.location.href = "/orders";
            }, 700);
        } catch (error) {
            console.error("[Orders New] Create order failed:", error);
            toast("error", error?.message || "Greška pri čuvanju transportnog naloga.");
        } finally {
            button.disabled = false;
            button.innerHTML = original;
            root.icons?.refresh?.(button);
        }
    }

    function nextTab(tabName) {
        if (tabName === "cargo" && !validateBasic(true)) return;
        if (tabName === "stops" && (!validateBasic(true) || !validateCargo(true))) return;
        if (tabName === "review" && !validateAll(true)) return;

        setActiveTab(tabName);
    }

    function bind() {
        const form = $("orderForm");
        if (!form || state.initialized) return;

        state.initialized = true;

        document.querySelectorAll("[data-order-tab]").forEach(tab => {
            tab.addEventListener("click", () => nextTab(tab.dataset.orderTab));
            tab.addEventListener("keydown", event => {
                if (event.key === "ArrowRight" || event.key === "ArrowDown") {
                    event.preventDefault();
                    const index = tabs.indexOf(tab.dataset.orderTab);
                    nextTab(tabs[(index + 1) % tabs.length]);
                    document.querySelector(`[data-order-tab="${tabs[(index + 1) % tabs.length]}"]`)?.focus();
                }

                if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
                    event.preventDefault();
                    const index = tabs.indexOf(tab.dataset.orderTab);
                    nextTab(tabs[(index - 1 + tabs.length) % tabs.length]);
                    document.querySelector(`[data-order-tab="${tabs[(index - 1 + tabs.length) % tabs.length]}"]`)?.focus();
                }
            });
        });

        document.querySelectorAll("[data-order-next]").forEach(button => {
            button.addEventListener("click", () => nextTab(button.dataset.orderNext));
        });

        document.querySelectorAll("[data-order-prev]").forEach(button => {
            button.addEventListener("click", () => setActiveTab(button.dataset.orderPrev));
        });

        $("addStop")?.addEventListener("click", () => {
            createStop({ type: "Other" });
            setActiveTab("stops");
        });

        form.addEventListener("submit", saveOrder);

        [
            "orderNumber",
            "customerName",
            "priority",
            "cargoDescription",
            "cargoWeightTons",
            "cargoVolumeM3",
            "pallets",
            "hazmat"
        ].forEach(id => {
            $(id)?.addEventListener("input", updateReview);
            $(id)?.addEventListener("change", updateReview);
        });

        form.addEventListener("input", event => {
            if (event.target.matches("input, select, textarea")) {
                setFieldError(event.target, "");
            }
        });

        createStop({ type: "Loading" });
        createStop({ type: "Unloading" });
        updateReview();
        setActiveTab("basic");
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", bind, { once: true });
    } else {
        bind();
    }

    window.ProMapOrdersNew = {
        reset() {
            $("orderForm")?.reset();
            $("stops")?.replaceChildren();
            state.stopIndex = 0;
            clearErrors();
            createStop({ type: "Loading" });
            createStop({ type: "Unloading" });
            setActiveTab("basic");
            updateReview();
        },
        addStop() {
            createStop({ type: "Other" });
            setActiveTab("stops");
        },
        collectStops,
        buildPayload,
        setActiveTab,
        validate: validateAll
    };
})();
