(() => {
    "use strict";

    const state = {
        initialized: false,
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

    function getElement(id) {
        return document.getElementById(id);
    }

    function getModal() {
        return getElement("ordersNewModal");
    }

    function getForm() {
        return getElement("orderForm");
    }

    function getStopsContainer() {
        return getElement("stops");
    }

    function getMessageElement() {
        return getElement("formMessage");
    }

    function getSaveButton() {
        return getElement("saveOrder");
    }

    function getAddStopButton() {
        return getElement("addStop");
    }

    function getText(root, selector) {
        const element = root.querySelector(selector);

        if (!element) {
            return null;
        }

        const value = element.value?.trim();

        return value || null;
    }

    function getNumber(id) {
        const element = getElement(id);

        if (!element) {
            return null;
        }

        const value = element.value.trim();

        if (value === "") {
            return null;
        }

        const number = Number(value);

        return Number.isFinite(number)
            ? number
            : null;
    }

    function showMessage(message, isError = true) {
        const element = getMessageElement();

        if (!element) {
            return;
        }

        element.hidden = false;
        element.textContent = message;

        element.classList.toggle(
            "error",
            isError
        );

        element.classList.toggle(
            "success",
            !isError
        );
    }

    function hideMessage() {
        const element = getMessageElement();

        if (!element) {
            return;
        }

        element.hidden = true;
        element.textContent = "";

        element.classList.remove("error");
        element.classList.remove("success");
    }

    function openModal() {
        const modal = getModal();

        if (!modal) {
            console.error(
                "[Orders New] Existing #ordersNewModal was not found."
            );

            return;
        }

        resetForm();

        modal.hidden = false;
        modal.setAttribute(
            "aria-hidden",
            "false"
        );

        document.body.classList.add(
            "has-modal"
        );

        window.setTimeout(() => {
            getElement("orderNumber")?.focus();
        }, 0);
    }

    function closeModal() {
        const modal = getModal();

        if (!modal) {
            return;
        }

        modal.hidden = true;

        modal.setAttribute(
            "aria-hidden",
            "true"
        );

        document.body.classList.remove(
            "has-modal"
        );
    }

    function resetForm() {
        const form = getForm();
        const stops = getStopsContainer();

        if (!form || !stops) {
            return;
        }

        form.reset();

        stops.replaceChildren();

        state.stopIndex = 0;

        hideMessage();

        createStop({
            type: "Loading"
        });

        createStop({
            type: "Unloading"
        });
    }

    function createStop(data = {}) {
        const stops = getStopsContainer();

        if (!stops) {
            return null;
        }

        const card = document.createElement("article");

        card.className =
            "pm-card pm-order-stop";

        card.dataset.index =
            String(state.stopIndex++);

        const options = state.stopTypes
            .map(([value, label]) => {
                const selected =
                    data.type === value
                        ? " selected"
                        : "";

                return `
                    <option value="${value}"${selected}>
                        ${label}
                    </option>
                `;
            })
            .join("");

        card.innerHTML = `
            <div class="pm-order-stop-head">

                <strong>
                    Tačka
                    <span class="stop-number"></span>
                </strong>

                <button
                    type="button"
                    class="pm-btn danger remove-stop">
                    Ukloni
                </button>

            </div>

            <div class="pm-grid pm-grid-3">

                <div class="pm-field">

                    <label class="pm-label">
                        Tip *
                    </label>

                    <select
                        class="pm-select stop-type">
                        ${options}
                    </select>

                </div>

                <div class="pm-field">

                    <label class="pm-label">
                        Naziv *
                    </label>

                    <input
                        class="pm-input stop-name"
                        maxlength="200"
                        autocomplete="off"
                        required />

                </div>

                <div class="pm-field">

                    <label class="pm-label">
                        Grad
                    </label>

                    <input
                        class="pm-input stop-city"
                        autocomplete="address-level2" />

                </div>

            </div>

            <div class="pm-grid pm-grid-3">

                <div class="pm-field">

                    <label class="pm-label">
                        Adresa
                    </label>

                    <input
                        class="pm-input stop-address"
                        autocomplete="street-address" />

                </div>

                <div class="pm-field">

                    <label class="pm-label">
                        Država
                    </label>

                    <input
                        class="pm-input stop-country"
                        maxlength="2"
                        value="RS"
                        autocomplete="country" />

                </div>

                <div class="pm-field">

                    <label class="pm-label">
                        Vreme servisa (min)
                    </label>

                    <input
                        class="pm-input stop-service"
                        type="number"
                        min="0"
                        max="1440"
                        step="1"
                        value="30" />

                </div>

            </div>

            <div class="pm-grid pm-grid-3">

                <div class="pm-field">

                    <label class="pm-label">
                        Planirano vreme
                    </label>

                    <input
                        class="pm-input stop-planned"
                        type="datetime-local" />

                </div>

                <div class="pm-field">

                    <label class="pm-label">
                        Latitude *
                    </label>

                    <input
                        class="pm-input stop-latitude"
                        type="number"
                        step="any"
                        min="-90"
                        max="90"
                        required />

                </div>

                <div class="pm-field">

                    <label class="pm-label">
                        Longitude *
                    </label>

                    <input
                        class="pm-input stop-longitude"
                        type="number"
                        step="any"
                        min="-180"
                        max="180"
                        required />

                </div>

            </div>

            <div class="pm-field">

                <label class="pm-label">
                    Napomena
                </label>

                <textarea
                    class="pm-textarea stop-notes"
                    rows="2"></textarea>

            </div>
        `;

        setStopValues(card, data);

        bindRemoveStop(card);

        stops.appendChild(card);

        renumberStops();

        return card;
    }

    function setStopValues(card, data) {
        const name =
            card.querySelector(".stop-name");

        const city =
            card.querySelector(".stop-city");

        const address =
            card.querySelector(".stop-address");

        const country =
            card.querySelector(".stop-country");

        const service =
            card.querySelector(".stop-service");

        const planned =
            card.querySelector(".stop-planned");

        const latitude =
            card.querySelector(".stop-latitude");

        const longitude =
            card.querySelector(".stop-longitude");

        const notes =
            card.querySelector(".stop-notes");

        if (name) {
            name.value =
                data.name ?? "";
        }

        if (city) {
            city.value =
                data.city ?? "";
        }

        if (address) {
            address.value =
                data.address ?? "";
        }

        if (country) {
            country.value =
                data.countryCode ?? "RS";
        }

        if (service) {
            service.value =
                data.serviceMinutes ?? 30;
        }

        if (planned && data.plannedAt) {
            planned.value =
                toDateTimeLocal(data.plannedAt);
        }

        if (latitude && data.latitude != null) {
            latitude.value =
                data.latitude;
        }

        if (longitude && data.longitude != null) {
            longitude.value =
                data.longitude;
        }

        if (notes) {
            notes.value =
                data.notes ?? "";
        }
    }

    function toDateTimeLocal(value) {
        const date = new Date(value);

        if (Number.isNaN(date.getTime())) {
            return "";
        }

        const offset =
            date.getTimezoneOffset();

        const localDate =
            new Date(
                date.getTime() -
                offset * 60 * 1000
            );

        return localDate
            .toISOString()
            .slice(0, 16);
    }

    function bindRemoveStop(card) {
        const button =
            card.querySelector(
                ".remove-stop"
            );

        if (!button) {
            return;
        }

        button.addEventListener(
            "click",
            () => {
                card.remove();
                renumberStops();
            }
        );
    }

    function renumberStops() {
        const stops =
            getStopsContainer();

        if (!stops) {
            return;
        }

        stops
            .querySelectorAll(
                ".pm-order-stop"
            )
            .forEach((card, index) => {

                const number =
                    card.querySelector(
                        ".stop-number"
                    );

                if (number) {
                    number.textContent =
                        String(index + 1);
                }
            });
    }

    function addStop() {
        createStop({
            type: "Other"
        });
    }

    function collectStops() {
        const stops =
            getStopsContainer();

        if (!stops) {
            return [];
        }

        return [
            ...stops.querySelectorAll(
                ".pm-order-stop"
            )
        ].map(card => {

            const planned =
                card.querySelector(
                    ".stop-planned"
                )?.value;

            return {
                type:
                    card.querySelector(
                        ".stop-type"
                    )?.value ?? "Other",

                name:
                    getText(
                        card,
                        ".stop-name"
                    ),

                address:
                    getText(
                        card,
                        ".stop-address"
                    ),

                city:
                    getText(
                        card,
                        ".stop-city"
                    ),

                countryCode:
                    getText(
                        card,
                        ".stop-country"
                    )?.toUpperCase() ?? null,

                latitude:
                    Number(
                        card.querySelector(
                            ".stop-latitude"
                        )?.value
                    ),

                longitude:
                    Number(
                        card.querySelector(
                            ".stop-longitude"
                        )?.value
                    ),

                plannedAt:
                    planned
                        ? new Date(
                            planned
                        ).toISOString()
                        : null,

                serviceMinutes:
                    Number(
                        card.querySelector(
                            ".stop-service"
                        )?.value || 0
                    ),

                notes:
                    getText(
                        card,
                        ".stop-notes"
                    )
            };
        });
    }

    function validateStops(stops) {
        if (stops.length < 2) {
            return (
                "Nalog mora imati najmanje " +
                "dve transportne tačke."
            );
        }

        const hasLoading =
            stops.some(
                stop =>
                    stop.type === "Loading" ||
                    stop.type ===
                    "LoadingAndUnloading"
            );

        if (!hasLoading) {
            return (
                "Dodaj najmanje jednu " +
                "utovarnu tačku."
            );
        }

        const hasUnloading =
            stops.some(
                stop =>
                    stop.type === "Unloading" ||
                    stop.type ===
                    "LoadingAndUnloading"
            );

        if (!hasUnloading) {
            return (
                "Dodaj najmanje jednu " +
                "istovarnu tačku."
            );
        }

        for (
            let index = 0;
            index < stops.length;
            index++
        ) {
            const stop =
                stops[index];

            const number =
                index + 1;

            if (!stop.name) {
                return (
                    "Transportna tačka " +
                    number +
                    ": naziv je obavezan."
                );
            }

            if (
                !Number.isFinite(
                    stop.latitude
                ) ||
                !Number.isFinite(
                    stop.longitude
                )
            ) {
                return (
                    "Transportna tačka " +
                    number +
                    ": potrebno je uneti " +
                    "validne koordinate."
                );
            }

            if (
                stop.latitude < -90 ||
                stop.latitude > 90
            ) {
                return (
                    "Transportna tačka " +
                    number +
                    ": latitude nije validan."
                );
            }

            if (
                stop.longitude < -180 ||
                stop.longitude > 180
            ) {
                return (
                    "Transportna tačka " +
                    number +
                    ": longitude nije validan."
                );
            }

            if (
                !Number.isInteger(
                    stop.serviceMinutes
                ) ||
                stop.serviceMinutes < 0 ||
                stop.serviceMinutes > 1440
            ) {
                return (
                    "Transportna tačka " +
                    number +
                    ": vreme servisa mora " +
                    "biti između 0 i 1440 minuta."
                );
            }
        }

        return null;
    }

    function buildPayload() {
        return {
            orderNumber:
                getElement(
                    "orderNumber"
                )?.value.trim() ?? "",

            customerName:
                getElement(
                    "customerName"
                )?.value.trim() ?? "",

            priority:
                getElement(
                    "priority"
                )?.value ?? "Normal",

            cargoDescription:
                getElement(
                    "cargoDescription"
                )?.value.trim() || null,

            cargoWeightTons:
                getNumber(
                    "cargoWeightTons"
                ),

            cargoVolumeM3:
                getNumber(
                    "cargoVolumeM3"
                ),

            pallets:
                getNumber(
                    "pallets"
                ),

            hazmat:
                getElement(
                    "hazmat"
                )?.checked ?? false,

            stops:
                collectStops()
        };
    }

    function validatePayload(payload) {
        if (!payload.orderNumber) {
            return "Broj naloga je obavezan.";
        }

        if (!payload.customerName) {
            return "Kupac je obavezan.";
        }

        return validateStops(
            payload.stops
        );
    }

    async function postOrder(payload) {
        const response =
            await fetch(
                "/api/business/orders",
                {
                    method: "POST",

                    headers: {
                        "Content-Type":
                            "application/json",

                        "Accept":
                            "application/json"
                    },

                    credentials:
                        "same-origin",

                    body:
                        JSON.stringify(
                            payload
                        )
                }
            );

        const data =
            await response
                .json()
                .catch(() => null);

        if (!response.ok) {
            throw new Error(
                data?.message ||
                data?.title ||
                "Nije moguće sačuvati " +
                "transportni nalog."
            );
        }

        return data;
    }

    async function submitForm(event) {
        event.preventDefault();

        hideMessage();

        const form =
            event.currentTarget;

        if (!form.reportValidity()) {
            return;
        }

        const payload =
            buildPayload();

        const validationError =
            validatePayload(
                payload
            );

        if (validationError) {
            showMessage(
                validationError,
                true
            );

            return;
        }

        const saveButton =
            getSaveButton();

        const addStopButton =
            getAddStopButton();

        if (!saveButton) {
            return;
        }

        const originalText =
            saveButton.textContent;

        saveButton.disabled = true;

        if (addStopButton) {
            addStopButton.disabled = true;
        }

        saveButton.textContent =
            "Čuvanje…";

        try {
            await postOrder(
                payload
            );

            showMessage(
                "Transportni nalog je " +
                "uspešno kreiran.",
                false
            );

            window.setTimeout(
                () => {
                    closeModal();

                    window.location.reload();
                },
                500
            );

        } catch (error) {
            console.error(
                "[Orders New] Create order failed:",
                error
            );

            showMessage(
                error?.message ||
                "Greška pri čuvanju " +
                "transportnog naloga.",
                true
            );

        } finally {
            saveButton.disabled =
                false;

            if (addStopButton) {
                addStopButton.disabled =
                    false;
            }

            saveButton.textContent =
                originalText;
        }
    }

    function bindForm() {
        const form =
            getForm();

        if (!form) {
            return;
        }

        if (
            form.dataset.ordersNewBound ===
            "true"
        ) {
            return;
        }

        form.dataset.ordersNewBound =
            "true";

        form.addEventListener(
            "submit",
            submitForm
        );
    }

    function bindAddStop() {
        const button =
            getAddStopButton();

        if (!button) {
            return;
        }

        if (
            button.dataset.ordersNewBound ===
            "true"
        ) {
            return;
        }

        button.dataset.ordersNewBound =
            "true";

        button.addEventListener(
            "click",
            addStop
        );
    }

    function bindCloseButtons() {
        const modal =
            getModal();

        if (!modal) {
            return;
        }

        modal
            .querySelectorAll(
                "[data-modal-close]"
            )
            .forEach(button => {

                if (
                    button.dataset
                        .ordersNewBound ===
                    "true"
                ) {
                    return;
                }

                button.dataset
                    .ordersNewBound =
                    "true";

                button.addEventListener(
                    "click",
                    event => {
                        event.preventDefault();
                        closeModal();
                    }
                );
            });
    }

    function bindEscape() {
        if (
            document.body.dataset
                .ordersNewEscapeBound ===
            "true"
        ) {
            return;
        }

        document.body.dataset
            .ordersNewEscapeBound =
            "true";

        document.addEventListener(
            "keydown",
            event => {

                if (
                    event.key !== "Escape"
                ) {
                    return;
                }

                const modal =
                    getModal();

                if (
                    modal &&
                    !modal.hidden
                ) {
                    closeModal();
                }
            }
        );
    }

    function bindOpenButton() {
        const button = getElement("newOrder");

        if (!button) {
            return;
        }

        if (button.dataset.ordersNewBound === "true") {
            return;
        }

        button.dataset.ordersNewBound = "true";

        button.addEventListener("click", event => {
                event.preventDefault();
                openModal();
            }
        );
    }

    function initialize() {
        if (state.initialized) {
            return;
        }

        state.initialized = true;

        bindOpenButton();
        bindForm();
        bindAddStop();
        bindCloseButtons();
        bindEscape();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, {
            once: true
        });
    } else {
        initialize();
    }

    window.ProMapOrdersNew = {
        open: openModal,
        close: closeModal,
        reset: resetForm,
        addStop,
        collectStops,
        buildPayload
    };
})();