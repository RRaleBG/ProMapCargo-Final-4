(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};
    function ensureHost() {
        let host = document.getElementById("pm-toast-host");
        if (host) return host;
        host = document.createElement("div");
        host.id = "pm-toast-host";
        host.setAttribute("aria-live", "polite");
        host.style.cssText = "position:fixed;right:18px;bottom:18px;z-index:9999;display:grid;gap:10px;max-width:min(420px,calc(100vw - 36px));";
        document.body.appendChild(host);
        return host;
    }
    root.toast = {
        show(message, type = "info", timeout = 4000) {
            const item = document.createElement("div");
            item.className = `pm-card pm-toast pm-toast--${type}`;
            item.style.cssText = "padding:12px 14px;box-shadow:var(--pm-shadow-md);";
            item.textContent = String(message ?? "");
            ensureHost().appendChild(item);
            const close = () => item.remove();
            if (timeout > 0) window.setTimeout(close, timeout);
            return { close, element: item };
        },
        success(message, timeout) { return this.show(message, "success", timeout); },
        warning(message, timeout) { return this.show(message, "warning", timeout); },
        error(message, timeout) { return this.show(message, "danger", timeout); },
        info(message, timeout) { return this.show(message, "info", timeout); }
    };
})();
