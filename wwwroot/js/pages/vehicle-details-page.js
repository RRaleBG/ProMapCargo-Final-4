(() => {
    "use strict";

    const root = document.getElementById("vehicleDetailsPage");
    if (!root) return;

    const pathParts = window.location.pathname.split("/").filter(Boolean);
    const vehicleId = pathParts[pathParts.length - 1];
    const http = window.ProMapCargo?.http;
    const detailUrl = `/api/business/vehicles/${encodeURIComponent(vehicleId)}/details`;

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
        return value ? new Date(`${value}T00:00:00`).toLocaleDateString("sr-Latn-RS") : "—";
    }

    function renderTimeline(id, items, map, emptyText) {
        const element = document.getElementById(id);
        if (!element) return;
        if (!Array.isArray(items) || !items.length) {
            element.innerHTML = `<div class="pm-trip-empty">${escapeHtml(emptyText)}</div>`;
            return;
        }
        element.innerHTML = items.map(map).join("");
    }

    function fill(details) {
        setText("detailRegistration", details.registration || "Bez registracije");
        setText("detailVehicleName", [details.make, details.model, details.name].filter(Boolean).join(" · ") || details.type || "Vozilo");
        const statusEl = document.getElementById("detailStatus");
        if (statusEl) {
            statusEl.innerHTML = `<span class="pm-vehicle-status"><span class="pm-vehicle-status-dot"></span>${escapeHtml(details.status || "Nepoznato")}</span>`;
        }

        setText("detailRegistrationValue", details.registration);
        setText("detailVin", details.vin);
        setText("detailMake", details.make);
        setText("detailModel", details.model);
        setText("detailDriver", details.driver?.fullName || "Nije dodeljen");
        setText("detailTrip", details.trip?.tripNumber || "Nema aktivne ture");
        setText("detailGpsStatus", details.lastGpsAt ? "Online / istorija dostupna" : "Offline");
        setText("detailYear", details.year);
        setText("detailType", details.type);
        setText("detailWeight", details.grossWeightTons ? `${details.grossWeightTons} t` : "—");
        setText("detailGpsTime", formatDate(details.lastGpsAt));
        setText("detailFleet", details.fleetNumber);
        setText("detailEuro", details.emissionClass);
        setText("detailFuelCard", details.fuelCard);
        setText("detailTrailer", details.trailerRegistration);
        setText("detailOdometer", details.odometerKm ? `${Number(details.odometerKm).toLocaleString("sr-Latn-RS")} km` : "—");
        setText("detailDimensions", `${details.lengthMeters} × ${details.widthMeters} × ${details.heightMeters} m`);
        setText("detailAxles", `${details.axles} / ${details.axleLoadTons} t`);
        setText("detailNotes", details.notes);

        setLink("detailDriverLink", details.driver?.id ? `/drivers/details/${encodeURIComponent(details.driver.id)}` : "/drivers", Boolean(details.driver?.id));
        setLink("detailTripLink", details.trip?.id ? `/trips/details/${encodeURIComponent(details.trip.id)}` : "/trips", Boolean(details.trip?.id));

        renderTimeline("vehicleDocuments", details.documents, document => `
            <div class="timeline-item">
                <span class="timeline-dot"></span>
                <div>
                    <strong>${escapeHtml(document.documentType)}</strong>
                    <span>${escapeHtml(document.number || "Bez broja")}</span>
                    <span>Važi do ${escapeHtml(day(document.expiryDate))}${document.fileId ? ` · <a href="/api/files/${encodeURIComponent(document.fileId)}" target="_blank" rel="noopener">Dokument</a>` : ""}</span>
                </div>
            </div>`, "Nema dokumenata za vozilo.");

        renderTimeline("vehicleLogs", details.logEntries, entry => `
            <div class="timeline-item">
                <span class="timeline-dot"></span>
                <div>
                    <strong>${escapeHtml(entry.kind)} · ${escapeHtml(entry.title)}</strong>
                    <span>${escapeHtml(entry.description || "Bez opisa")}</span>
                    <span>${escapeHtml(formatDate(entry.occurredAt))}${entry.cost != null ? ` · ${escapeHtml(String(entry.cost))} ${escapeHtml(entry.currency || "EUR")}` : ""}</span>
                </div>
            </div>`, "Nema servisnih ili repair stavki.");

        renderTimeline("vehicleTripHistory", details.tripHistory, trip => `
            <div class="timeline-item">
                <span class="timeline-dot"></span>
                <div>
                    <strong>${escapeHtml(trip.tripNumber || "Tura")}</strong>
                    <span>${escapeHtml(trip.route || "Ruta nije dostupna")}</span>
                    <span>${escapeHtml(formatDate(trip.startedAt))} → ${escapeHtml(formatDate(trip.completedAt))}</span>
                </div>
            </div>`, "Nema istorije tura za ovo vozilo.");
    }

    async function load() {
        if (!vehicleId || !http?.get) {
            throw new Error("Detalji vozila trenutno nisu dostupni.");
        }

        const seeded = window.ProMapCargo?.initialData?.take?.(detailUrl);
        const details = seeded !== undefined ? seeded : await http.get(detailUrl);
        if (!details) {
            throw new Error("Detalji vozila trenutno nisu dostupni.");
        }

        fill(details);
    }

    load().catch(function (loadError) {
        renderTimeline("vehicleDocuments", [], function () { return ""; }, loadError?.message || "Detalji vozila trenutno nisu dostupni.");
        renderTimeline("vehicleLogs", [], function () { return ""; }, "");
        renderTimeline("vehicleTripHistory", [], function () { return ""; }, "");
    });
})();
