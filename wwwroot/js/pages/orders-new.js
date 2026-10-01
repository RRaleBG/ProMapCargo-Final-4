function setActiveTab(tabName) {
    const tabs = [
        "basic",
        "cargo",
        "stops",
        "review"
    ];

    if (!tabs.includes(tabName)) {
        tabName = "basic";
    }

    state.activeTab = tabName;

    document
        .querySelectorAll("[data-order-tab]")
        .forEach(button => {

    function getSaveButton() {
        return getElement("saveOrder");
    }

    function getAddStopButton() {
        return getElement("addStop");
    }

    function getMessageElement() {
        return getElement("formMessage");
    }

    function getNumber(id) {
        const value = Number(getElement(id)?.value);
        return Number.isFinite(value) ? value : null;
    }

    function showMessage(message, type = "danger") {
        const element = getMessageElement();

        if (!element) return;

        element.hidden = false;
        element.textContent = message;
        element.classList.remove(
            "pm-alert-danger",
            "pm-alert-success",
            "pm-alert-warning",
            "pm-alert-info"
        );
        element.classList.add(
            type === "success"
                ? "pm-alert-success"
                : "pm-alert-danger"
        );
    }

    function hideMessage() {
        const element = getMessageElement();

        if (!element) return;

        element.hidden = true;
        element.textContent = "";
        element.classList.remove(
            "pm-alert-danger",
            "pm-alert-success",
            "pm-alert-warning",
            "pm-alert-info"
        );
    }

    function setActiveTab(target) {
        document.querySelectorAll(".pm-tab").forEach(tab => {
            const active = tab.dataset.tabTarget === target;

            tab.classList.toggle("is-active", active);
            tab.setAttribute("aria-selected", String(active));
        });

        document.querySelectorAll(".pm-tab-panel").forEach(panel => {
            const active = panel.dataset.tabPanel === target;

            panel.classList.toggle("is-active", active);
            panel.hidden = !active;
        });
    }

    function bindTabs() {
        document.querySelectorAll(".pm-tab[data-tab-target]").forEach(tab => {
            if (tab.dataset.ordersNewTabBound === "true") return;

            tab.dataset.ordersNewTabBound = "true";
            tab.addEventListener("click", () => {
                setActiveTab(tab.dataset.tabTarget);
            });
        });
    }

    function resetForm() {
        const form = getForm();
        const stops = getStopsContainer();

        if (!form || !stops) return;

        form.reset();
        stops.replaceChildren();
        state.stopIndex = 0;

        hideMessage();
        setActiveTab("basic");

        createStop({ type: "Loading" });
        createStop({ type: "Unloading" });
    }

    function createStop(data = {}) {
        const stops = getStopsContainer();

        if (!stops) return null;

        const card = document.createElement("article");
        card.className = "pm-card";
        card.dataset.stopCard = "true";
        card.dataset.index = String(state.stopIndex++);

        const options = state.stopTypes.map(([value, label]) => {
            const selected = data.type === value ? " selected" : "";
            return "<option value=\"" + value + "\"" + selected + ">" +
                   label +
                   "</option>";
        }).join("");

        card.innerHTML = [
            "<header class=\"pm-card-header\">",
            "  <div>",
            "    <h3 class=\"pm-card-title\">Transportna tačka <span data-stop-number></span></h3>",
            "    <p class=\"pm-card-subtitle\">Podaci o lokaciji, vremenu i operaciji.</p>",
            "  </div>",
            "  <div class=\"pm-card-actions\">",
            "    <button type=\"button\" class=\"pm-btn pm-btn-danger pm-btn-sm\" data-remove-stop>Ukloni</button>",
            "  </div>",
            "</header>",
            "<div class=\"pm-card-body\">",
            "  <div class=\"pm-grid pm-grid-3\">",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Tip *</label>",
            "      <select class=\"pm-select\" data-stop-field=\"type\">" + options + "</select>",
            "    </div>",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Naziv *</label>",
            "      <input class=\"pm-input\" maxlength=\"200\" autocomplete=\"off\" data-stop-field=\"name\" />",
            "    </div>",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Grad</label>",
            "      <input class=\"pm-input\" autocomplete=\"address-level2\" data-stop-field=\"city\" />",
            "    </div>",
            "  </div>",
            "  <div class=\"pm-grid pm-grid-3\">",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Adresa</label>",
            "      <input class=\"pm-input\" autocomplete=\"street-address\" data-stop-field=\"address\" />",
            "    </div>",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Država</label>",
            "      <input class=\"pm-input\" maxlength=\"2\" value=\"RS\" autocomplete=\"country\" data-stop-field=\"country\" />",
            "    </div>",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Vreme servisa (min)</label>",
            "      <input class=\"pm-input\" type=\"number\" min=\"0\" max=\"1440\" step=\"1\" value=\"30\" data-stop-field=\"service\" />",
            "    </div>",
            "  </div>",
            "  <div class=\"pm-grid pm-grid-3\">",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Planirano vreme</label>",
            "      <input class=\"pm-input\" type=\"datetime-local\" data-stop-field=\"planned\" />",
            "    </div>",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Latitude *</label>",
            "      <input class=\"pm-input\" type=\"number\" step=\"any\" min=\"-90\" max=\"90\" data-stop-field=\"latitude\" />",
            "    </div>",
            "    <div class=\"pm-field\">",
            "      <label class=\"pm-label\">Longitude *</label>",
            "      <input class=\"pm-input\" type=\"number\" step=\"any\" min=\"-180\" max=\"180\" data-stop-field=\"longitude\" />",
            "    </div>",
            "  </div>",
            "  <div class=\"pm-field\">",
            "    <label class=\"pm-label\">Napomena</label>",
            "    <textarea class=\"pm-textarea\" rows=\"3\" data-stop-field=\"notes\"></textarea>",
            "  </div>",
            "</div>"
        ].join("");

        setStopValues(card, data);

        card.querySelector("[data-remove-stop]")?.addEventListener("click", () => {
            card.remove();
            renumberStops();
        });

        stops.appendChild(card);
        renumberStops();

        return card;
    }

    function setStopValues(card, data) {
        const set = (field, value) => {
            const element = card.querySelector(
                "[data-stop-field=\"" + field + "\"]"
            );

            if (element && value != null) {
                element.value = value;
            }
        };

        set("name", data.name ?? "");
        set("city", data.city ?? "");
        set("address", data.address ?? "");
        set("country", data.countryCode ?? "RS");
        set("service", data.serviceMinutes ?? 30);
        set("latitude", data.latitude);
        set("longitude", data.longitude);
        set("notes", data.notes ?? "");

        if (data.plannedAt) {
            set("planned", toDateTimeLocal(data.plannedAt));
        }
    }

    function toDateTimeLocal(value) {
        const date = new Date(value);

        if (Number.isNaN(date.getTime())) return "";

        const offset = date.getTimezoneOffset();

        return new Date(date.getTime() - offset * 60000)
            .toISOString()
            .slice(0, 16);
    }

    function renumberStops() {
        getStopsContainer()?.querySelectorAll("[data-stop-card]")
            .forEach((card, index) => {
                const number = card.querySelector("[data-stop-number]");

                if (number) {
                    number.textContent = String(index + 1);
                }
            });
    }

    function addStop() {
        createStop({ type: "Other" });
        setActiveTab("stops");
    }

    function collectStops() {
        return [
            ...(getStopsContainer()?.querySelectorAll("[data-stop-card]") ?? [])
        ].map(card => {
            const value = field => card.querySelector(
                "[data-stop-field=\"" + field + "\"]"
            )?.value.trim() ?? "";

            const planned = value("planned");

            return {
                type: value("type") || "Other",
                name: value("name"),
                address: value("address"),
                city: value("city"),
                countryCode: value("country").toUpperCase() || null,
                latitude: Number(value("latitude")),
                longitude: Number(value("longitude")),
                plannedAt: planned
                    ? new Date(planned).toISOString()
                    : null,
                serviceMinutes: Number(value("service") || 0),
                notes: value("notes")
            };
        });
    }

    function validateStops(stops) {
        if (stops.length < 2) {
            return "Nalog mora imati najmanje dve transportne tačke.";
        }

        const hasLoading = stops.some(stop =>
            stop.type === "Loading" ||
            stop.type === "LoadingAndUnloading"
        );

        if (!hasLoading) {
            return "Dodaj najmanje jednu utovarnu tačku.";
        }

        const hasUnloading = stops.some(stop =>
            stop.type === "Unloading" ||
            stop.type === "LoadingAndUnloading"
        );

        if (!hasUnloading) {
            return "Dodaj najmanje jednu istovarnu tačku.";
        }

        for (let index = 0; index < stops.length; index++) {
            const stop = stops[index];
            const number = index + 1;

            if (!stop.name) {
                setActiveTab("stops");
                return "Transportna tačka " + number + ": naziv je obavezan.";
            }

            if (!Number.isFinite(stop.latitude) ||
                !Number.isFinite(stop.longitude)) {
                setActiveTab("stops");
                return "Transportna tačka " + number +
                       ": potrebno je uneti validne koordinate.";
            }

            if (stop.latitude < -90 || stop.latitude > 90) {
                setActiveTab("stops");
                return "Transportna tačka " + number +
                       ": latitude nije validan.";
            }

            if (stop.longitude < -180 || stop.longitude > 180) {
                setActiveTab("stops");
                return "Transportna tačka " + number +
                       ": longitude nije validan.";
            }

            if (!Number.isInteger(stop.serviceMinutes) ||
                stop.serviceMinutes < 0 ||
                stop.serviceMinutes > 1440) {
                setActiveTab("stops");
                return "Transportna tačka " + number +
                       ": vreme servisa mora biti između 0 i 1440 minuta.";
            }
        }

        return null;
    }

    function buildPayload() {
        return {
            orderNumber: getElement("orderNumber")?.value.trim() ?? "",
            customerName: getElement("customerName")?.value.trim() ?? "",
            priority: getElement("priority")?.value ?? "Normal",
            cargoDescription:
                getElement("cargoDescription")?.value.trim() || null,
            cargoWeightTons: getNumber("cargoWeightTons"),
            cargoVolumeM3: getNumber("cargoVolumeM3"),
            pallets: getNumber("pallets"),
            hazmat: getElement("hazmat")?.checked ?? false,
            stops: collectStops()
        };
    }

    function validatePayload(payload) {
        if (!payload.orderNumber) {
            setActiveTab("basic");
            return "Broj naloga je obavezan.";
        }

        if (!payload.customerName) {
            setActiveTab("basic");
            return "Kupac je obavezan.";
        }

        return validateStops(payload.stops);
    }

    async function postOrder(payload) {
        const response = await fetch("/api/business/orders", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json"
            },
            credentials: "same-origin",
            body: JSON.stringify(payload)
        });

        const data = await response.json().catch(() => null);

        if (!response.ok) {
            throw new Error(
                data?.message ||
                data?.title ||
                "Nije moguće sačuvati transportni nalog."
            );
        }

        return data;
    }

    async function submitForm(event) {
        event.preventDefault();
        hideMessage();

        const payload = buildPayload();
        const validationError = validatePayload(payload);

        if (validationError) {
            showMessage(validationError);
            return;
        }

        const form = event.currentTarget;

        if (!form.reportValidity()) {
            setActiveTab("basic");
            return;
        }

        const saveButton = getSaveButton();
        const addStopButton = getAddStopButton();

        if (!saveButton) return;

        const originalText = saveButton.textContent;

        saveButton.disabled = true;

        if (addStopButton) {
            addStopButton.disabled = true;
        }

        saveButton.textContent = "Čuvanje…";

        try {
            await postOrder(payload);

            showMessage(
                "Transportni nalog je uspešno kreiran.",
                "success"
            );

            window.setTimeout(() => {
                window.location.href = "/orders";
            }, 500);
        } catch (error) {
            console.error(
                "[Orders New] Create order failed:",
                error
            );

            showMessage(
                error?.message ||
                "Greška pri čuvanju transportnog naloga."
            );
        } finally {
            saveButton.disabled = false;

            if (addStopButton) {
                addStopButton.disabled = false;
            }

            saveButton.textContent = originalText;
        }
    }

    function bindForm() {
        const form = getForm();

        if (!form || form.dataset.ordersNewBound === "true") return;

        form.dataset.ordersNewBound = "true";
        form.addEventListener("submit", submitForm);
    }

    function bindAddStop() {
        const button = getAddStopButton();

        if (!button || button.dataset.ordersNewBound === "true") return;

        button.dataset.ordersNewBound = "true";
        button.addEventListener("click", addStop);
    }

    function initialize() {
        if (state.initialized) return;

        state.initialized = true;

        bindTabs();
        resetForm();
        bindForm();
        bindAddStop();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }

    window.ProMapOrdersNew = {
        reset: resetForm,
        addStop,
        collectStops,
        buildPayload,
        setActiveTab
    };
})();