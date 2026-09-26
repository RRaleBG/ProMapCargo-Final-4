(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};

    function ensureHost() {
        let host = document.getElementById("pm-toast-host");
        if (host) {
            return host;
        }

        host = document.createElement("div");
        host.id = "pm-toast-host";
        host.setAttribute("aria-live", "polite");
        host.setAttribute("aria-atomic", "false");
        document.body.appendChild(host);
        return host;
    }

    function iconFor(type) {
        return {
            success: "check-circle-2",
            warning: "triangle-alert",
            danger: "circle-alert",
            info: "info",
        }[type] || "info";
    }

    function show(message, type = "info", timeout = 4000) {
        const item = document.createElement("article");
        item.className = `pm-toast pm-toast--${type}`;
        item.setAttribute("role", type === "danger" ? "alert" : "status");

        const icon = document.createElement("i");
        icon.className = "pm-toast-icon";
        icon.dataset.lucide = iconFor(type);
        icon.setAttribute("aria-hidden", "true");

        const content = document.createElement("p");
        content.className = "pm-toast-message";
        content.textContent = String(message ?? "");

        const close = document.createElement("button");
        close.type = "button";
        close.className = "pm-toast-close";
        close.setAttribute("aria-label", "Zatvori obaveštenje");
        close.dataset.toastClose = "true";
        const closeText = document.createElement("span");
        closeText.setAttribute("aria-hidden", "true");
        closeText.textContent = "×";
        close.appendChild(closeText);

        item.append(icon, content, close);
        const host = ensureHost();
        host.appendChild(item);
        window.ProMapCargo.icons?.refresh?.(item);

        const remove = () => {
            item.classList.add("is-leaving");
            window.setTimeout(() => item.remove(), 180);
        };
        close.addEventListener("click", remove);
        if (timeout > 0) {
            window.setTimeout(remove, timeout);
        }
        return { close: remove, element: item };
    }

    root.toast = {
        show,
        success(message, timeout) { return show(message, "success", timeout); },
        warning(message, timeout) { return show(message, "warning", timeout); },
        error(message, timeout) { return show(message, "danger", timeout); },
        info(message, timeout) { return show(message, "info", timeout); },
    };
})();
