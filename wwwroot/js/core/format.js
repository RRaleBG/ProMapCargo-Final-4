(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};
    const locale = "sr-Latn-RS";
    root.format = {
        number(value, digits = 0) { return new Intl.NumberFormat(locale, { maximumFractionDigits: digits }).format(Number(value || 0)); },
        currency(value, currency = "EUR") { return new Intl.NumberFormat(locale, { style: "currency", currency }).format(Number(value || 0)); },
        date(value) { const d = value instanceof Date ? value : new Date(value); return Number.isNaN(d.getTime()) ? "—" : new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(d); },
        dateTime(value) { const d = value instanceof Date ? value : new Date(value); return Number.isNaN(d.getTime()) ? "—" : new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(d); },
        distanceMeters(value) { const m = Number(value || 0); return m >= 1000 ? `${(m / 1000).toFixed(m >= 10000 ? 0 : 1)} km` : `${Math.round(m)} m`; },
        durationSeconds(value) { const total = Math.max(0, Math.round(Number(value || 0))); const h = Math.floor(total / 3600); const min = Math.round((total % 3600) / 60); return h ? `${h} h ${min} min` : `${min} min`; }
    };
})();
