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

    const $ = id => document.getElementById(id);

    function toast(type, message) {
        const service = root.toast;

        if (service?.[type]) {
            service[type](message);
            return;
        }

        console[type === "error" ? "error" : "log"](message);
    }

    function validation() {
        return root.validation;
    }

    function setFieldError(input, message) {
        const service = validation();

        if (service?.setFieldError) {
            service.setFieldError(input, message);
            return;
        }

        if (!input) return;

        input.setAttribute("aria-invalid", message ? "true" : "false");
        const error = $(input.id + "-error");

        if (error) {
            error.textContent = message || "";
            error.hidden = !message;
        }
    }

    function clearErrors() {
        document.querySelectorAll("#orderForm input, #orderForm select, #orderForm textarea")
            .forEach(input => setFieldError(input, ""));
    }

    function setActiveTab(tabName) {
        const validTabs = ["basic", "cargo", "stops", "review"];

        if (!validTabs.includes(tabName)) {
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

        if (tabName === "review") {
            updateReview();
        }
    }

    function validateBasic() {
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

        if (!valid) {
            setActiveTab("basic");
            toast("warning", "Popunite obavezna polja u osnovnim podacima.");
        }

        return valid;
    }

    function validateCargo() {
        let valid = true;

        const weight = $("cargoWeightTons");
        const volume = $("cargoVolumeM3");
        const pallets = $("pallets");

        const numericFields = [
            [weight, "Težina ne može biti negativna."],
            [volume, "Zapremina ne može biti negativna."],
            [pallets, "Broj paleta ne može biti negativan."]
        ];

        numericFields.forEach(([input, message]) => {
            if (!input?.value) {
                setFieldError(input, "");
                return;
            }

            const value = Number(input.value);

            if (!Number.isFinite(value) || value < 0) {
                setFieldError(input, message);
                valid = false;
            }
        });

        if (!valid) {
            setActiveTab("cargo");
            toast("warning", "Proverite podatke o teretu.");
        }

        return valid;
    }

    function getStopCards() {
        return [...document.querySelectorAll("[data-stop-card]")];
    }

    function readStop(card) {
        const value = field =>
            card.querySelector("[data-stop-field="" + field + ""]")?.value.trim() ?? "";

        const planned = value("planned");

        return {
            type: value("type") || "Other",
            name: value("name"),
            address: value("address"),
            city: value("city"),
            countryCode: value("country").toUpperCase() || null,
            latitude: Number(value("latitude")),
            longitude: Number(value("longitude")),
            plannedAt: planned ? new Date(planned).toISOString() : null,
            serviceMinutes: Number(value("service") || 0),
            notes: value("notes")
        };
    }

    function collectStops() {
        return getStopCards().map(readStop);
    }

    function validateStops() {
        const cards = getStopCards();

        if (cards.length < 2) {
            setActiveTab("stops");
            toast("warning", "Nalog mora imati najmanje dve transportne tačke.");
            return false;
        }

        const stops = collectStops();
        let valid = true;

        const hasLoading = stops.some(stop =>
            stop.type === "Loading" ||
            stop.type === "LoadingAndUnloading"
        );

        const hasUnloading = stops.some(stop =>
            stop.type === "Unloading" ||
            stop.type === "LoadingAndUnloading"
        );

        if (!hasLoading || !hasUnloading) {
            toast("warning", "Ruta mora imati najmanje jedan utovar i jedan istovar.");
            valid = false;
        }

        cards.forEach((card, index) => {
            const stop = stops[index];
            const number = index + 1;

            const name = card.querySelector("[data-stop-field="name"]");
            const country = card.querySelector("[data-stop-field="country"]");
            const latitude = card.querySelector("[data-stop-field="latitude"]");
            const longitude = card.querySelector("[data-stop-field="longitude"]");
            const service = card.querySelector("[data-stop-field="service"]");

            if (!validation()?.required(stop.name)) {
                setFieldError(name, "Naziv je obavezan.");
                valid = false;
            }

            if (!validation()?.lat(stop.latitude)) {
                setFieldError(latitude, "Latitude mora biti između -90 i 90.");
                valid = false;
            }

            if (!validation()?.lon(stop.longitude)) {
                setFieldError(longitude, "Longitude mora biti između -180 i 180.");
                valid = false;
            }

            if (!/^[A-Z]{2}$/.test(stop.countryCode || "")) {
                setFieldError(country, "Država mora biti ISO kod od 2 slova.");
                valid = false;
            }

            if (!Number.isInteger(stop.serviceMinutes) ||
                stop.serviceMinutes < 0 ||
                stop.serviceMinutes > 1440) {
                setFieldError(service, "Vreme servisa mora biti 0–1440 minuta.");
                valid = false;
            }

            if (!valid && index === 0) {
                // Validation summary is handled by the toast; field errors remain visible.
            }
        });

        if (!valid) {
            setActiveTab("stops");
            toast("warning", "Proverite označena polja u transportnim tačkama.");
        }

        return valid;
    }

    function validateAll() {
        clearErrors();

        if (!validateBasic()) return false;
        if (!validateCargo()) return false;
        if (!validateStops()) return false;

        return true;
    }

    function createStop(data = {}) {
        const container = $("stops");

        if (!container) return null;

        state.stopIndex += 1;

        const card = document.createElement("article");
        card.className = "pm-card";
        card.dataset.stopCard = "true";

        const options = state.stopTypes.map(([value, label]) => {
            const selected = value === (data.type || "Other") ? " selected" : "";
            return "<option value="" + value + """ + selected + ">" + label + "</option>";
        }).join("");

        card.innerHTML = [
            "<header class="pm-card-header">",
            "  <div>",
            "    <h3 class="pm-card-title">Transportna tačka <span data-stop-number></span></h3>",
            "    <p class="pm-card-subtitle">Lokacija, vreme i operacija.</p>",
            "  </div>",
            "  <div class="pm-card-actions">",
            "    <button type="button" class="pm-btn pm-btn-danger pm-btn-sm" data-remove-stop>",
            "      Ukloni",
            "    </button>",
            "  </div>",
            "</header>",
            "<div class="pm-card-body">",
            "  <div class="pm-form-grid pm-form-grid-3">",
            "    <div class="pm-field">",
            "      <label class="pm-label">Tip *</label>",
            "      <select class="pm-select" data-stop-field="type">" + options + "</select>",
            "    </div>",
            "    <div class="pm-field">",
            "      <label class="pm-label">Naziv *</label>",
            "      <input class="pm-input" maxlength="200" data-stop-field="name" required />",
            "    </div>",
            "    <div class="pm-field">",
            "      <label class="pm-label">Grad</label>",
            "      <input class="pm-input" data-stop-field="city" />",
            "    </div>",
            "  </div>",
            "  <div class="pm-form-grid pm-form-grid-3 pm-mt-4">",
            "    <div class="pm-field">",
            "      <label class="pm-label">Adresa</label>",
            "      <input class="pm-input" data-stop-field="address" />",
            "    </div>",
            "    <div class="pm-field">",
            "      <label class="pm-label">Država *</label>",
            "      <input class="pm-input" maxlength="2" value="RS" data-stop-field="country" />",
            "    </div>",
            "    <div class="pm-field">",
            "      <label class="pm-label">Servis (min) *</label>",
            "      <input class="pm-input" type="number" min="0" max="1440" step="1" value="30" data-stop-field="service" />",
            "    </div>",
            "  </div>",
            "  <div class="pm-form-grid pm-form-grid-3 pm-mt-4">",
            "    <div class="pm-field">",
            "      <label class="pm-label">Planirano vreme</label>",
            "      <input class="pm-input" type="datetime-local" data-stop-field="planned" />",
            "    </div>",
            "    <div class="pm-field">",
            "      <label class="pm-label">Latitude *</label>",
            "      <input class="pm-input" type="number" step="any" data-stop-field="latitude" />",
            "    </div>",
            "    <div class="pm-field">",
            "      <label class="pm-label">Longitude *</label>",
            "      <input class="pm-input" type="number" step="any" data-stop-field="longitude" />",
            "    </div>",
            "  </div>",
            "  <div class="pm-field pm-mt-4">",
            "    <label class="pm-label">Napomena</label>",
            "    <textarea class="pm-textarea" rows="3" data-stop-field="notes"></textarea>",
            "  </div>",
            "</div>"
        ].join("");

        setStopValues(card, data);

        card.querySelector("[data-remove-stop]").addEventListener("click", () => {
            const cards = getStopCards();

            if (cards.length <= 2) {
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

    function setStopValues(card, data) {
        const set = (field, value) => {
            const element = card.querySelector(
                "[data-stop-field="" + field + ""]"
            );

            if (element && value !== undefined && value !== null) {
                element.value = value;
            }
        };

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
                set(
                    "planned",
                    new Date(date.getTime() - offset * 60000)
                        .toISOString()
                        .slice(0, 16)
                );
            }
        }
    }

    function renumberStops() {
        getStopCards().forEach((card, index) => {
            const number = card.querySelector("[data-stop-number]");

            if (number) {
                number.textContent = String(index + 1);
            }
        });
    }

    function updateReview() {
        const priority = $("priority");
        const priorityText =
            priority?.options[priority.selectedIndex]?.text || "Normalan";

        $("reviewOrderNumber").value = $("orderNumber")?.value.trim() || "—";
        $("reviewCustomerName").value = $("customerName")?.value.trim() || "—";
        $("reviewPriority").value = priorityText;
        $("reviewWeight").value =
            $("cargoWeightTons")?.value ? $("cargoWeightTons").value + " t" : "—";
        $("reviewVolume").value =
            $("cargoVolumeM3")?.value ? $("cargoVolumeM3").value + " m³" : "—";
        $("reviewPallets").value =
            $("pallets")?.value ? $("pallets").value : "—";
        $("reviewHazmat").value = $("hazmat")?.checked ? "Da" : "Ne";

        const reviewStops = $("reviewStops");

        if (!reviewStops) return;

        reviewStops.replaceChildren();

        collectStops().forEach((stop, index) => {
            const item = document.createElement("div");
            item.className = "pm-card";
            item.innerHTML =
                "<div class="pm-card-body">" +
                "<div class="pm-label">Tačka " + (index + 1) + " · " +
                escapeHtml(stop.type) + "</div>" +
                "<strong>" + escapeHtml(stop.name || "Bez naziva") + "</strong>" +
                "<div class="pm-help">" +
                escapeHtml([stop.address, stop.city, stop.countryCode].filter(Boolean).join(", ")) +
                "</div>" +
                "</div>";

            reviewStops.appendChild(item);
        });
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll(""", "&quot;")
            .replaceAll("'", "&#039;");
    }

    function buildPayload() {
        const number = valueOf("orderNumber");
        const customer = valueOf("customerName");

        return {
            orderNumber: number,
            customerName: customer,
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

        if (!validateAll()) {
            return;
        }

        const button = $("saveOrder");

        if (!button) return;

        const original = button.innerHTML;
        button.disabled = true;
        button.innerHTML = "Čuvanje…";

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
            window.ProMapCargo.icons?.refresh?.(button);
        }
    }

    function nextTab(tabName) {
        if (tabName === "cargo" && !validateBasic()) return;
        if (tabName === "stops" && (!validateBasic() || !validateCargo())) return;
        if (tabName === "review" && !validateAll()) return;

        setActiveTab(tabName);
    }

    function bind() {
        const form = $("orderForm");

        if (!form || state.initialized) return;

        state.initialized = true;

        document.querySelectorAll("[data-order-tab]").forEach(tab => {
            tab.addEventListener("click", () => nextTab(tab.dataset.orderTab));
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