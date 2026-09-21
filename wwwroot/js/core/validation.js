(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};
    root.validation = {
        required(value) { return String(value ?? "").trim().length > 0; },
        email(value) { return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(value ?? "").trim()); },
        positiveNumber(value) { return Number.isFinite(Number(value)) && Number(value) > 0; },
        lat(value) { const n = Number(value); return Number.isFinite(n) && n >= -90 && n <= 90; },
        lon(value) { const n = Number(value); return Number.isFinite(n) && n >= -180 && n <= 180; },
        setFieldError(input, message) {
            if (!input) return;
            input.setAttribute("aria-invalid", message ? "true" : "false");
            const id = input.id ? `${input.id}-error` : null;
            let error = id ? document.getElementById(id) : null;
            if (!error && message) {
                error = document.createElement("div");
                if (id) error.id = id;
                error.className = "pm-error";
                input.insertAdjacentElement("afterend", error);
            }
            if (error) {
                error.textContent = message || "";
                error.hidden = !message;
                if (id) input.setAttribute("aria-describedby", id);
            }
        }
    };
})();
