(() => {
    "use strict";

    const root = document.getElementById("driverDetailsPage");
    if (!root) return;

    const pathParts = window.location.pathname.split("/").filter(Boolean);
    const driverId = pathParts[pathParts.length - 1];
    const http = window.ProMapCargo?.http;
    const detailUrl = `/api/business/drivers/${encodeURIComponent(driverId)}/details`;

    function escapeHtml(value) {
        return String(value == null ? "" : value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/\"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function formatDate(value) {
        if (!value) return "—";
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return "—";
        return new Intl.DateTimeFormat("sr-Latn-RS", { dateStyle: "short", timeStyle: "short" }).format(date);
    }

    function formatMoney(value, currency) {
        const amount = Number(value || 0);
        return `${new Intl.NumberFormat("sr-Latn-RS", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)} ${currency || "EUR"}`;
    }

    function getInitials(name) {
        const parts = String(name || "?").trim().split(/\s+/).filter(Boolean);
        if (!parts.length) return "?";
        if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
        return (parts[0].substring(0, 1) + parts[parts.length - 1].substring(0, 1)).toUpperCase();
    }

    function setText(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = value || "—";
    }

    function setLink(id, href, enabled) {
        const element = document.getElementById(id);
        if (!element) return;
        element.setAttribute("href", href || "#");
        element.setAttribute("aria-disabled", enabled ? "false" : "true");
        element.classList.toggle("pm-btn-disabled", !enabled);
    }

    function day(value) {
        if (!value) return "—";
        const date = new Date(`${value}T00:00:00`);
        const today = new Date(new Date().toDateString());
        const days = Math.round((date - today) / 86400000);
        return date.toLocaleDateString("sr-Latn-RS") + (days < 0 ? " · ISTEKLO" : days <= 30 ? ` · ističe za ${days} d` : "");
    }

    function renderTrips(details) {
        const container = document.getElementById("driverTripDetails");
        if (!container) return;
        const trips = details && Array.isArray(details.trips) ? details.trips : [];
        if (!trips.length) {
            container.innerHTML = '<div class="pm-driver-detail-value">Vozač nema evidentiranih tura.</div>';
            return;
        }

        container.innerHTML = trips.map(function (trip) {
            const costs = Array.isArray(trip.costs) ? trip.costs : [];
            const costHtml = costs.length
                ? '<ul class="pm-driver-trip-cost-list">' + costs.map(function (cost) {
                    const receiptLink = cost.receiptFileId
                        ? ` <a class="pm-btn pm-btn-sm" href="/api/files/${encodeURIComponent(cost.receiptFileId)}" target="_blank" rel="noopener">Račun</a>`
                        : "";
                    return '<li><strong>' + escapeHtml(cost.category || 'Trošak') + '</strong> · ' +
                        escapeHtml(formatMoney(cost.amount, cost.currency || trip.currency)) +
                        ' · ' + escapeHtml(cost.paymentMethod || '—') +
                        (cost.location ? ' · ' + escapeHtml(cost.location) : '') + receiptLink + '</li>';
                }).join('') + '</ul>'
                : '<div class="pm-driver-detail-value">Nema unetih troškova za ovu turu.</div>';

            return '<article class="pm-driver-trip-card">' +
                '<div class="pm-driver-trip-card-head">' +
                    '<div><strong>' + escapeHtml(trip.tripNumber || 'Tura') + '</strong><div class="pm-driver-detail-label">' + escapeHtml(trip.route || 'Ruta nije definisana') + '</div></div>' +
                    '<a class="pm-btn pm-btn-sm" href="/trips/details/' + encodeURIComponent(trip.id) + '">Detalji ture</a>' +
                '</div>' +
                '<div class="pm-driver-detail-grid">' +
                    '<div class="pm-driver-detail-item"><div class="pm-driver-detail-label">Nalog</div><div class="pm-driver-detail-value">' + escapeHtml(trip.orderNumber || '—') + '</div></div>' +
                    '<div class="pm-driver-detail-item"><div class="pm-driver-detail-label">Kupac</div><div class="pm-driver-detail-value">' + escapeHtml(trip.customerName || '—') + '</div></div>' +
                    '<div class="pm-driver-detail-item"><div class="pm-driver-detail-label">Status</div><div class="pm-driver-detail-value">' + escapeHtml(trip.status || '—') + ' · ' + escapeHtml(trip.executionState || '—') + '</div></div>' +
                    '<div class="pm-driver-detail-item"><div class="pm-driver-detail-label">Napredak</div><div class="pm-driver-detail-value">' + escapeHtml(Math.round(Number(trip.routeProgressPercent || 0)) + '%') + '</div></div>' +
                    '<div class="pm-driver-detail-item"><div class="pm-driver-detail-label">Polazak</div><div class="pm-driver-detail-value">' + escapeHtml(formatDate(trip.startedAt || trip.plannedStartAt)) + '</div></div>' +
                    '<div class="pm-driver-detail-item"><div class="pm-driver-detail-label">Trošak</div><div class="pm-driver-detail-value">' + escapeHtml(formatMoney(trip.spentAmount || 0, trip.currency)) + ' · Na čekanju ' + escapeHtml(formatMoney(trip.pendingAmount || 0, trip.currency)) + '</div></div>' +
                '</div>' +
                '<div class="pm-driver-detail-label">Troškovi ture</div>' + costHtml +
            '</article>';
        }).join('');
    }

    function fill(details) {
        document.getElementById("detailAvatar").textContent = getInitials(details.fullName);
        setText("detailName", details.fullName || "Bez imena");
        setText("detailLicense", details.licenseNumber ? `Vozačka licenca · ${details.licenseNumber}` : "Vozačka licenca nije uneta");
        setText("detailLicenseValue", details.licenseNumber || "Nije uneto");
        setText("detailPhone", details.phone || "Nije uneto");
        setText("detailCountry", details.currentCountryCode || "—");
        setText("detailGps", formatDate(details.lastGpsAt));
        setText("detailEmployee", details.employeeNumber);
        setText("detailEmail", details.email);
        setText("detailCategories", details.licenseCategories);
        setText("detailLicenseExpiry", day(details.licenseExpiry));
        setText("detailCode95", day(details.code95Expiry));
        setText("detailAdr", day(details.adrExpiry));
        setText("detailTachoCard", details.tachographCardNumber ? `${details.tachographCardNumber} · ${day(details.tachographCardExpiry)}` : "—");
        setText("detailMedical", day(details.medicalExpiry));
        setText("detailHired", details.hireDate ? new Date(`${details.hireDate}T00:00:00`).toLocaleDateString("sr-Latn-RS") : "—");
        setText("detailAddress", details.address);
        setText("detailNotes", details.notes);
        setText("detailVehicle", details.vehicle ? [details.vehicle.registration, details.vehicle.make, details.vehicle.model].filter(Boolean).join(" · ") : "Nije dodeljeno");
        setText("detailTrip", details.trips && details.trips.length ? (details.trips[0].tripNumber || "Tura") : "Nema aktivne ture");
        setText("detailGpsStatus", details.lastGpsAt ? "Online / istorija dostupna" : "Offline");
        const statusEl = document.getElementById("detailStatus");
        if (statusEl) {
            statusEl.innerHTML = `<span class="pm-driver-status"><span class="pm-driver-status-dot"></span>${escapeHtml(details.status || "Nepoznato")}</span>`;
        }

        setLink("detailVehicleLink", details.vehicle?.id ? `/vehicles/details/${encodeURIComponent(details.vehicle.id)}` : "/vehicles", Boolean(details.vehicle?.id));
        setLink("detailTripLink", details.trips && details.trips.length ? `/trips/details/${encodeURIComponent(details.trips[0].id)}` : "/trips", Boolean(details.trips && details.trips.length));
        renderTrips(details);
    }

    async function load() {
        if (!driverId || !http?.get) {
            throw new Error("Detalji vozača trenutno nisu dostupni.");
        }

        const seeded = window.ProMapCargo?.initialData?.take?.(detailUrl);
        const details = seeded !== undefined ? seeded : await http.get(detailUrl);
        if (!details) {
            throw new Error("Detalji vozača trenutno nisu dostupni.");
        }

        fill(details);
    }

    load().catch(function (loadError) {
        const container = document.getElementById("driverTripDetails");
        if (container) {
            container.innerHTML = `<div class="pm-driver-detail-value">${escapeHtml(loadError?.message || "Detalji vozača trenutno nisu dostupni.")}</div>`;
        }
    });
})();
