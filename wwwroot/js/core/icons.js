(() => {
    "use strict";

    const glyphNames = new Map([
        ["↻", "refresh-cw"],
        ["⟳", "refresh-cw"],
        ["＋", "plus"],
        ["+", "plus"],
        ["⌕", "search"],
        ["↗", "route"],
        ["▤", "clipboard-list"],
        ["▣", "truck"],
        ["◎", "locate-fixed"],
        ["△", "triangle-alert"],
        ["◌", "circle"],
        ["◇", "scan-line"],
        ["₿", "wallet"],
        ["✓", "check"],
        ["!", "triangle-alert"],
        ["×", "x"],
        ["↰", "corner-up-left"],
        ["⇅", "arrow-up-down"],
        ["⛶", "maximize"],
        ["◈", "layout-dashboard"],
        ["♙", "user-round"],
        ["◉", "activity"],
        ["◍", "wallet"],
        ["→", "arrow-right"],
        ["←", "arrow-left"],
        ["⌃", "chevron-up"],
        ["⌄", "chevron-down"],
        ["⌂", "home"],
        ["✉", "mail"],
        ["☎", "phone"],
    ]);

    const iconClassNames = [
        "pm-nav-icon", "pm-search-icon", "pm-notification-icon", "pm-user-chevron",
        "pm-kpi-icon", "pm-dispatch-kpi-icon", "pm-alert-kpi-icon", "pm-driver-kpi-icon",
        "pm-vehicle-kpi-icon", "compliance-kpi-icon", "finance-kpi-icon",
        "monitoring-kpi-icon", "policy-icon", "moderation-banner-icon",
        "critical-banner-icon", "toast-icon", "check-ok", "check-warn",
        "circle-check", "chart-no-axes-column-increasing", "settings", "scroll-text",
        "map", "phone", "calendar-days", "clock-3", "menu", "arrow-right",
        "refresh-cw", "plus", "truck", "locate-fixed", "triangle-alert", "wallet",
        "user-round", "layout-dashboard"
    ];

    function nearestContext(element) {
        const scopes = [
            element.closest("button"),
            element.closest("a"),
            element.closest("[class*='kpi']"),
            element.closest("[class*='card']"),
            element.parentElement,
        ].filter(Boolean);
        return scopes.map((scope) => scope.textContent || "").join(" ").toLowerCase();
    }

    function inferLucideName(svg) {
        const context = nearestContext(svg);
        const className = String(svg.getAttribute("class") || "").toLowerCase();
        if (/loading|spinner|spin/.test(context + className)) return "loader-circle";
        if (/refresh|osveži|reload/.test(context + className)) return "refresh-cw";
        if (/add|new|novo|plus|create/.test(context + className)) return "plus";
        if (/search|pretraži|filter/.test(context + className)) return "search";
        if (/driver|vozač|profile|user|operator/.test(context + className)) return "user-round";
        if (/vehicle|truck|vozil|fleet|kamion/.test(context + className)) return "truck";
        if (/gps|location|lokacij|position|signal/.test(context + className)) return "locate-fixed";
        if (/route|routing|tura|navigation|navigacij/.test(context + className)) return "route";
        if (/alert|warning|incident|risk|error|opasnost/.test(context + className)) return "triangle-alert";
        if (/check|active|approved|online|success|potvr/.test(context + className)) return "circle-check";
        if (/finance|money|cost|payment|saldo|euro|currency/.test(context + className)) return "wallet";
        if (/report|chart|analytics|statistic|izveštaj/.test(context + className)) return "chart-no-axes-column-increasing";
        if (/setting|config|podešav/.test(context + className)) return "settings";
        if (/audit|history|log|timeline/.test(context + className)) return "scroll-text";
        if (/map|karta/.test(context + className)) return "map";
        if (/phone|call|pozovi|kontakt/.test(context + className)) return "phone";
        if (/calendar|date|datum/.test(context + className)) return "calendar-days";
        if (/time|clock|vreme|eta/.test(context + className)) return "clock-3";
        if (/menu|navigation-toggle/.test(context + className)) return "menu";
        if (/arrow|next|forward|open|navigate|slede/.test(context + className)) return "arrow-right";
        return null;
    }

    function replaceIconElement(element, name) {
        if (element.hasAttribute("data-lucide")) return;
        element.setAttribute("data-lucide", name);
        element.setAttribute("aria-hidden", "true");
    }

    function markGlyphs(root) {
        const selectors = iconClassNames.map((name) => `.${name}`).join(",");
        root.querySelectorAll?.(selectors).forEach((element) => {
            if (element.children.length) return;
            const name = glyphNames.get((element.textContent || "").trim());
            if (name) replaceIconElement(element, name);
        });

        root.querySelectorAll?.("button > span, a > span").forEach((element) => {
            if (element.children.length || element.hasAttribute("data-lucide")) return;
            const name = glyphNames.get((element.textContent || "").trim());
            if (name) replaceIconElement(element, name);
        });
    }

    function markInlineSvgs(root) {
        root.querySelectorAll?.("svg:not([data-lucide])").forEach((svg) => {
            if (svg.classList.contains("lucide")) return;
            if (svg.closest(".pm-map, .leaflet-container, .maplibregl-map, .pm-profile-avatar, .pm-public-brand-mark")) return;
            const viewBox = (svg.getAttribute("viewBox") || "").trim();
            if (viewBox !== "0 0 24 24") return;

            const parent = svg.parentElement;
            const isIcon = svg.hasAttribute("aria-hidden") ||
                parent?.matches("button, a, [class*='icon'], [class*='kpi']");
            if (!isIcon) return;

            const name = inferLucideName(svg);
            if (!name) return;

            const placeholder = document.createElement("i");
            placeholder.setAttribute("data-lucide", name);
            placeholder.setAttribute("aria-hidden", "true");
            const className = svg.getAttribute("class");
            if (className) placeholder.className = className;
            for (const attribute of ["style", "width", "height", "title", "focusable"]) {
                const value = svg.getAttribute(attribute);
                if (value !== null) placeholder.setAttribute(attribute, value);
            }
            svg.replaceWith(placeholder);
        });
    }

    let queued = false;
    function renderIcons(root = document) {
        if (!window.lucide?.createIcons || queued) return;
        queued = true;
        requestAnimationFrame(() => {
            queued = false;
            markGlyphs(root);
            markInlineSvgs(root);
            window.lucide.createIcons();
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        renderIcons();
        if (!("MutationObserver" in window) || !document.body) return;
        const observer = new MutationObserver((records) => {
            if (records.some((record) => record.addedNodes.length)) renderIcons();
        });
        observer.observe(document.body, { childList: true, subtree: true });
    }, { once: true });

    window.ProMapCargo = window.ProMapCargo || {};
    window.ProMapCargo.icons = { refresh: renderIcons };
})();
