(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};

    function initializeTheme() {
        let theme = "emerald";
        try { theme = localStorage.getItem("pm-theme") || theme; } catch { }
        if (!new Set(["emerald", "white"]).has(theme)) theme = "emerald";
        document.documentElement.dataset.theme = theme;
    }

    root.theme = {
        get() { return document.documentElement.dataset.theme || "emerald"; },
        set(theme) {
            if (!new Set(["emerald", "white"]).has(theme)) return;
            document.documentElement.dataset.theme = theme;
            try { localStorage.setItem("pm-theme", theme); } catch { }
            root.events?.emit("theme:changed", { theme });
        },
        toggle() { this.set(this.get() === "emerald" ? "white" : "emerald"); }
    };

    function initializeGlobalSearch() {
        const input = document.querySelector(".pm-global-search input[type='search']");
        document.addEventListener("keydown", event => {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
                event.preventDefault(); input?.focus(); input?.select();
            }
        });
    }

    function initializeDoubleSubmitProtection() {
        document.addEventListener("submit", event => {
            const form = event.target;
            if (!(form instanceof HTMLFormElement) || form.dataset.allowDoubleSubmit === "true") return;
            form.querySelectorAll("button[type='submit'],input[type='submit']").forEach(button => {
                button.disabled = true;
                button.setAttribute("aria-busy", "true");
            });
        }, true);
    }

    function initializeReveal() {
        const elements = Array.from(document.querySelectorAll(".pm-reveal"));
        if (!elements.length) return;
        if (!("IntersectionObserver" in window)) { elements.forEach(el => el.classList.add("is-visible")); return; }
        const observer = new IntersectionObserver(entries => entries.forEach(entry => {
            if (entry.isIntersecting) { entry.target.classList.add("is-visible"); observer.unobserve(entry.target); }
        }), { threshold: 0.08 });
        elements.forEach(el => observer.observe(el));
    }

    function initializeSidebar() {
        const toggle = document.getElementById("pm-sidebar-toggle");
        if (!toggle) return;
        document.querySelectorAll(".pm-nav-item").forEach(link => link.addEventListener("click", () => {
            if (window.matchMedia("(max-width: 980px)").matches) toggle.checked = false;
        }));
    }

    function initializeUnhandledErrors() {
        window.addEventListener("unhandledrejection", event => {
            const error = event.reason;
            if (error?.name === "HttpError") root.toast?.error(error.message);
        });
    }

    document.addEventListener("DOMContentLoaded", async () => {
        initializeTheme();
        initializeGlobalSearch();
        initializeDoubleSubmitProtection();
        initializeReveal();
        initializeSidebar();
        initializeUnhandledErrors();
        await root.alerts?.refresh?.();
        root.events?.emit("app:ready");
    });
})();
