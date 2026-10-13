(function () {
    "use strict";

    // Alert center data from the database (/api/alerts/feed). The page reads the cached
    // list synchronously (get, getOpen ...); load() refreshes it and status changes are
    // saved through the API, then "promap:alerts-changed" tells the page to re-render.

    window.ProMap = window.ProMap || {};

    const CATEGORY_LABELS = {
        navigation: "Navigacija",
        gps: "GPS",
        compliance: "Compliance",
        border: "Granica",
        maintenance: "Servis",
        tachograph: "Tachograph",
        fuel: "Gorivo / Fuel card",
        orders: "Transportni nalozi",
        trips: "Ture",
        driver: "Vozači",
        vehicle: "Vozila",
        finance: "Finansije",
        system: "Sistem",
    };

    const SEVERITY_LABELS = {
        critical: "Critical",
        high: "High",
        medium: "Medium",
        low: "Low",
        info: "Info",
    };

    // OperationalAlert.Type → category, title, suggested action.
    const TYPES = {
        Delay: ["orders", "Kašnjenje ture", "Proverite turu i obavestite kupca o novom ETA.", "/trips"],
        OffRoute: ["navigation", "Vozilo je van planirane rute", "Otvorite navigaciju i proverite poziciju vozila.", "/navigation"],
        GpsLost: ["gps", "GPS signal izgubljen", "Proverite poziciju vozila u monitoringu.", "/monitoring"],
        Maintenance: ["maintenance", "Vozilo na servisu", "Proverite servis i raspoloživost vozila.", "/compliance"],
        Compliance: ["compliance", "Problem sa dokumentacijom", "Otvorite Compliance i unesite važeći dokument.", "/compliance"],
        Incident: ["driver", "Incident prijavljen od vozača", "Kontaktirajte vozača i evidentirajte rešenje.", "/dispatch"],
        Border: ["border", "Granični prelaz", "Pratite čekanje na granici u dispečerskom centru.", "/dispatch"],
        Fuel: ["fuel", "Gorivo / kartica", "Proverite transakcije u finansijama.", "/finance"],
        Finance: ["finance", "Finansijsko upozorenje", "Proverite troškove u finansijama.", "/finance"],
    };

    const SEVERITY = { Critical: "critical", Warning: "high", Info: "info" };
    const STATUS_TO_UI = { Open: "open", Acknowledged: "acknowledged", Snoozed: "snoozed", Resolved: "resolved" };
    const STATUS_TO_API = { open: "Open", acknowledged: "Acknowledged", snoozed: "Snoozed", resolved: "Resolved" };

    let cache = [];

    function toUi(item) {
        const type = TYPES[item.type] || ["system", item.type || "Upozorenje", "Proverite detalje upozorenja.", null];
        return {
            id: item.id,
            severity: SEVERITY[item.severity] || "medium",
            category: type[0],
            type: item.type,
            status: STATUS_TO_UI[item.status] || "open",
            title: type[1],
            message: item.message,
            vehicle: item.vehicle,
            driver: item.driver,
            trip: item.trip,
            location: item.latitude != null ? `${Number(item.latitude).toFixed(4)}, ${Number(item.longitude).toFixed(4)}` : null,
            source: item.source,
            createdAt: item.createdAt,
            resolvedAt: item.resolvedAt,
            action: type[2],
            actionUrl: type[3],
        };
    }

    function notify() {
        updateBadges(cache);
        window.dispatchEvent(new CustomEvent("promap:alerts-changed", { detail: { alerts: cache } }));
    }

    async function load() {
        const response = await ProMapCargo.initialData.fetch("/api/alerts/feed", { headers: { "Accept": "application/json" }, credentials: "same-origin" });
        if (!response.ok) {
            throw new Error(response.status === 401 ? "Prijavite se da biste videli upozorenja." : `Upozorenja nisu učitana (${response.status}).`);
        }
        cache = (await response.json()).map(toUi);
        notify();
        return cache;
    }

    async function setStatus(id, status) {
        const response = await fetch(`/api/alerts/${encodeURIComponent(id)}/status`, {
            method: "POST",
            headers: { "Content-Type": "application/json", "Accept": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify({ status: STATUS_TO_API[status] || status }),
        });
        if (!response.ok) {
            console.warn("ProMap Alerts: status nije sačuvan", response.status);
        }
        return load();
    }

    function getAlerts() {
        return cache;
    }

    function getOpenAlerts() {
        return cache.filter(function (alert) {
            return alert.status === "open";
        });
    }

    function getTodayCount() {
        const today = new Date();
        return cache.filter(function (alert) {
            const date = new Date(alert.createdAt);
            return date.getFullYear() === today.getFullYear() && date.getMonth() === today.getMonth() && date.getDate() === today.getDate();
        }).length;
    }

    function updateBadges(alerts) {
        const count = (alerts || cache).filter(function (alert) {
            return alert.status === "open";
        }).length;

        document.querySelectorAll(".pm-nav-count[data-alert-count], .pm-notification-badge[data-alert-count]").forEach(function (element) {
            element.textContent = count;
            element.hidden = count <= 0;
        });
    }

    window.ProMap.Alerts = {
        load: load,
        get: getAlerts,
        getOpen: getOpenAlerts,
        getOpenCount: function () { return getOpenAlerts().length; },
        getCriticalCount: function () {
            return getOpenAlerts().filter(function (alert) { return alert.severity === "critical"; }).length;
        },
        getTodayCount: getTodayCount,
        acknowledge: function (id) { return setStatus(id, "acknowledged"); },
        resolve: function (id) { return setStatus(id, "resolved"); },
        reopen: function (id) { return setStatus(id, "open"); },
        snooze: function (id) { return setStatus(id, "snoozed"); },
        setStatus: setStatus,
        categoryLabel: function (category) { return CATEGORY_LABELS[category] || category || "Sistem"; },
        severityLabel: function (severity) { return SEVERITY_LABELS[severity] || severity || "Info"; },
        refreshBadges: function () { updateBadges(cache); },
    };
})();
