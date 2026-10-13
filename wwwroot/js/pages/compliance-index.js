(() => {
    "use strict";

    // Compliance from the database (/api/compliance): vehicles and trailers with their
    // documents and validity, service, repairs, faults, notes, trailer and history.
    // "Novi dokument" uploads the document; service, repair, fault and note entries and
    // the trailer change are saved from the two small modals.

    const root = document.querySelector('[data-module="compliance"]');
    if (!root) {
        return;
    }

    const $ = id => document.getElementById(id);

    const STATUS_BADGE = {
        blocked: ["status-danger", "BLOCKED"],
        expiring: ["status-warning", "EXPIRING"],
        compliant: ["status-success", "COMPLIANT"]
    };

    const STATE_TEXT = {
        valid: ["status-good-text", "Validno"],
        expiring: ["status-warning-text", "Ističe"],
        expired: ["status-danger-text", "Isteklo"],
        missing: ["status-danger-text", "Nedostaje"],
        none: ["status-good-text", "—"]
    };

    const DOC_ICON = {
        registration: ["document-icon-green", "▣"],
        "euro-truck": ["document-icon-green", "E6"],
        "euro-trailer": ["document-icon-green", "EU"],
        technical: ["document-icon-purple", "TP"],
        tir: ["document-icon-red", "TIR"],
        license: ["document-icon-blue", "LIC"],
        tachograph: ["document-icon-cyan", "TH"],
        fire: ["document-icon-orange", "PP"]
    };

    const LOG_FORMS = {
        service: { kicker: "MAINTENANCE", title: "Novi servis", kinds: true, severity: false, cost: true, odometer: true, workshop: true, completed: false },
        repair: { kicker: "REPAIRS", title: "Nova popravka", kind: "Repair", severity: false, cost: true, odometer: true, workshop: true, completed: true },
        fault: { kicker: "FAULT MONITORING", title: "Prijava greške", kind: "Fault", severity: true, cost: false, odometer: true, workshop: false, completed: true },
        note: { kicker: "NOTES", title: "Nova napomena", kind: "Note", severity: false, cost: false, odometer: false, workshop: false, completed: false }
    };

    const state = {
        data: null,
        status: "all",
        selectedId: null,
        logForm: null
    };

    // ---- Helpers ----

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function date(iso) {
        if (!iso) return "—";
        const d = new Date(iso.length === 10 ? iso + "T00:00:00" : iso);
        return d.toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit", year: "numeric" });
    }

    function time(iso) {
        return iso ? new Date(iso).toLocaleTimeString("sr-Latn-RS", { hour: "2-digit", minute: "2-digit" }) : "";
    }

    function daysLeft(iso) {
        if (!iso) return null;
        const today = new Date(new Date().toDateString());
        return Math.round((new Date(iso + "T00:00:00") - today) / 86400000);
    }

    function daysText(days) {
        if (days === null) return "—";
        if (days < 0) return `isteklo pre ${-days} d`;
        return days === 1 ? "1 dan" : `${days} dana`;
    }

    function money(value) {
        return value == null ? "—" : `${Number(value).toLocaleString("sr-RS", { maximumFractionDigits: 2 })} €`;
    }

    function selected() {
        return state.data?.vehicles.find(vehicle => vehicle.id === state.selectedId) || null;
    }

    function showToast(title, message) {
        $("toastTitle").textContent = title || "Sačuvano";
        $("toastText").textContent = message || "Promena je uspešno evidentirana.";
        const toast = $("complianceToast");
        toast.classList.add("show");
        window.clearTimeout(showToast.timeout);
        showToast.timeout = window.setTimeout(() => toast.classList.remove("show"), 3200);
    }

    async function request(url, options = {}) {
        const response = await ProMapCargo.initialData.fetch(url, { credentials: "same-origin", headers: { "Accept": "application/json", ...(options.headers || {}) }, ...options });
        const data = await response.json().catch(() => null);
        if (!response.ok) {
            throw new Error(data?.message || data?.title || (response.status === 401 ? "Prijavite se ponovo." : `Greška ${response.status}.`));
        }
        return data;
    }

    // ---- Vehicle list ----

    function cardHtml(vehicle) {
        const [badgeClass, badgeText] = STATUS_BADGE[vehicle.status];
        const cardClass = vehicle.status === "blocked" ? " vehicle-card-blocked" : vehicle.status === "expiring" ? " vehicle-card-expiring" : "";
        const registration = vehicle.registrationDoc;
        const registrationDays = daysLeft(registration.expiry);
        const detail = (label, item, text) => {
            const [cls, word] = STATE_TEXT[item.state] || STATE_TEXT.none;
            return `<div><span>${label}</span><strong class="${cls}">${escapeHtml(text ?? word)}</strong></div>`;
        };
        const search = [vehicle.registration, vehicle.make, vehicle.model, vehicle.vin, vehicle.name, vehicle.trailer?.registration].join(" ");
        const warning = vehicle.criticalCount > 0
            ? `<div class="vehicle-card-alert"><span>!</span>${vehicle.criticalCount} kritičn${vehicle.criticalCount === 1 ? "i dokument" : "a dokumenta"}</div>`
            : vehicle.expiringCount > 0
                ? `<div class="vehicle-card-warning"><span>!</span>${vehicle.expiringCount} dokument${vehicle.expiringCount === 1 ? "" : "a"} ističe u 30 dana</div>`
                : "";

        return `
            <article class="vehicle-card${cardClass}${vehicle.id === state.selectedId ? " selected" : ""}"
                     data-vehicle-id="${vehicle.id}" data-status="${vehicle.status}" data-type="${vehicle.type}" data-search="${escapeHtml(search)}">
                <div class="vehicle-card-top">
                    <div class="vehicle-identity">
                        <div class="vehicle-avatar vehicle-avatar-truck">${vehicle.type === "trailer" ? "🚚" : "🚛"}</div>
                        <div>
                            <strong>${escapeHtml(vehicle.registration)}</strong>
                            <span>${escapeHtml([vehicle.make, vehicle.model].filter(Boolean).join(" ") || vehicle.name || "")}</span>
                        </div>
                    </div>
                    <span class="status-badge ${badgeClass}">${badgeText}</span>
                </div>
                <div class="vehicle-card-details">
                    ${detail("Registracija", registration, registrationDays === null ? null : daysText(registrationDays))}
                    ${detail(vehicle.emissionClass ? vehicle.emissionClass.replace("euro", "Euro ") : "Euro", vehicle.euro)}
                    ${vehicle.type === "trailer" ? "" : detail("TIR", vehicle.tir, vehicle.tir.state === "none" ? "—" : null)}
                    ${vehicle.type === "trailer" ? "" : detail("Tahograf", vehicle.tachograph)}
                </div>
                ${vehicle.trailer ? `<div class="vehicle-trailer-line"><span>Prikolica</span><strong>${escapeHtml(vehicle.trailer.registration)} · ${escapeHtml([vehicle.trailer.make, vehicle.trailer.model].filter(Boolean).join(" "))}</strong></div>` : ""}
                ${warning}
            </article>`;
    }

    function renderList() {
        const query = ($("vehicleSearch").value || "").trim().toLowerCase();
        const type = $("vehicleTypeFilter").value;
        const vehicles = state.data.vehicles.filter(vehicle =>
            (state.status === "all" || vehicle.status === state.status)
            && (type === "all" || vehicle.type === type)
            && (!query || [vehicle.registration, vehicle.make, vehicle.model, vehicle.vin, vehicle.name, vehicle.trailer?.registration].join(" ").toLowerCase().includes(query)));

        $("vehicleList").innerHTML = vehicles.map(cardHtml).join("") ||
            '<article class="vehicle-card"><div class="vehicle-card-top"><div class="vehicle-identity"><div><strong>Nema vozila</strong><span>Promenite filter ili pretragu.</span></div></div></div></article>';
        $("visibleVehicleCount").textContent = `${vehicles.length} vozila`;
    }

    function renderSummary() {
        const kpi = state.data.kpi;
        $("totalVehicles").textContent = kpi.total;
        $("compliantVehicles").textContent = kpi.compliant;
        $("expiringVehicles").textContent = kpi.expiring;
        $("blockedVehicles").textContent = kpi.blocked;
        $("trailerCount").textContent = kpi.trailers;
        $("documentCount").textContent = kpi.documents;

        const counts = { all: state.data.vehicles.length };
        state.data.vehicles.forEach(vehicle => { counts[vehicle.status] = (counts[vehicle.status] || 0) + 1; });
        root.querySelectorAll("[data-status-filter]").forEach(button => {
            const counter = button.querySelector("span");
            if (counter) counter.textContent = counts[button.dataset.statusFilter] || 0;
        });

        const blocked = state.data.vehicles.filter(vehicle => vehicle.status === "blocked").length;
        $("criticalBanner").hidden = blocked === 0;
        $("criticalBannerTitle").textContent = `${blocked} vozila zahtevaju intervenciju`;

        const trailerSelect = $("trailerSelect");
        trailerSelect.innerHTML = '<option value="">Bez prikolice</option>';
        state.data.trailers.forEach(trailer => trailerSelect.add(new Option(`${trailer.registration} · ${[trailer.make, trailer.model].filter(Boolean).join(" ")}`, trailer.id)));
    }

    // ---- Detail panel ----

    function renderDetail() {
        const vehicle = selected();
        root.querySelectorAll(".vehicle-card").forEach(card => card.classList.toggle("selected", card.dataset.vehicleId === state.selectedId));
        if (!vehicle) return;

        $("detailVehicleIcon").textContent = vehicle.type === "trailer" ? "🚚" : "🚛";
        $("detailVehicleTitle").textContent = vehicle.registration;
        $("detailVehicleSubtitle").textContent = [[vehicle.make, vehicle.model].filter(Boolean).join(" "), vehicle.vin ? "VIN " + vehicle.vin : null].filter(Boolean).join(" · ");
        const [badgeClass, badgeText] = STATUS_BADGE[vehicle.status];
        $("detailStatus").innerHTML = `<span class="status-badge ${badgeClass}">${badgeText}</span>`;

        const critical = vehicle.documents.filter(doc => doc.state === "expired");
        const lock = $("complianceLock");
        lock.hidden = vehicle.status !== "blocked";
        $("complianceLockText").textContent = critical.length
            ? critical.map(doc => `${doc.title} je istekao ${date(doc.expiryDate)}.`).join(" ")
            : vehicle.vehicleStatus === "OutOfService" ? "Vozilo je označeno kao blokirano." : "Nedostaje obavezan dokument.";

        const counts = {
            documents: vehicle.documents.length,
            trailer: vehicle.trailer ? 1 : 0,
            service: vehicle.service.length,
            repairs: vehicle.repairs.length,
            faults: vehicle.faults.filter(item => item.status === "Open").length,
            notes: vehicle.notes.length
        };
        root.querySelectorAll("[data-tab-count]").forEach(counter => { counter.textContent = counts[counter.dataset.tabCount] ?? 0; });

        renderDocuments(vehicle);
        renderTrailer(vehicle);
        renderService(vehicle);
        renderRepairs(vehicle);
        renderFaults(vehicle);
        renderNotes(vehicle);
        renderHistory(vehicle);
        $("changeTrailer").hidden = vehicle.type === "trailer";
    }

    function renderDocuments(vehicle) {
        $("documentList").innerHTML = vehicle.documents.map(doc => {
            const [iconClass, icon] = DOC_ICON[doc.documentType] || ["document-icon-blue", "DOC"];
            const expired = doc.state === "expired";
            const rowClass = expired ? "document-expired" : "document-valid";
            const status = expired ? ["status-danger", "ISTEKLO"] : doc.state === "expiring" ? ["status-warning", "ISTIČE"] : ["status-success", "VALIDNO"];
            const file = doc.fileId ? `<a class="icon-action" href="/api/files/${doc.fileId}" target="_blank" rel="noopener" title="Pregled">◉</a>` : "";
            const action = expired || doc.state === "expiring"
                ? `<button type="button" class="document-fix-button" data-document="${doc.documentType}">OBRADI</button>`
                : `${file}<button type="button" class="icon-action" data-document="${doc.documentType}" title="Novi dokument ovog tipa">✎</button>`;
            return `
                <article class="document-row ${rowClass}">
                    <div class="document-icon ${expired ? "document-icon-red" : iconClass}">${icon}</div>
                    <div class="document-main">
                        <div class="document-title-row">
                            <strong>${escapeHtml(doc.title)}</strong>
                            <span class="document-status ${status[0]}">${status[1]}</span>
                        </div>
                        <span class="document-meta">${escapeHtml([doc.number, doc.issuer].filter(Boolean).join(" · "))}</span>
                        <span class="document-expiry${expired ? " document-expired-text" : ""}">
                            ${expired ? "Istekao" : "Važi do"} <strong>${date(doc.expiryDate)}</strong>${expired ? "" : " · " + daysText(doc.daysLeft)}
                        </span>
                    </div>
                    <div class="document-actions">${action}</div>
                </article>`;
        }).join("") || '<article class="document-row"><div class="document-main"><strong>Nema unetih dokumenata</strong><span class="document-meta">Dodajte dokument dugmetom iznad.</span></div></article>';
    }

    function renderTrailer(vehicle) {
        if (vehicle.type === "trailer") {
            const tractor = state.data.vehicles.find(item => item.trailer?.id === vehicle.id);
            $("trailerContent").innerHTML = `<div class="trailer-history"><div class="subsection-heading"><h4>${tractor ? "Vuče je " + escapeHtml(tractor.registration) : "Prikolica trenutno nije povezana"}</h4></div></div>`;
            return;
        }

        const trailer = vehicle.trailer ? state.data.vehicles.find(item => item.id === vehicle.trailer.id) : null;
        const docState = trailer ? `${trailer.documents.filter(doc => doc.state === "valid" || doc.state === "expiring").length} / ${trailer.documents.length}` : "—";
        const [euroClass, euroText] = trailer ? STATE_TEXT[trailer.euro.state] : STATE_TEXT.none;
        const [regClass, regText] = trailer ? STATE_TEXT[trailer.registrationDoc.state] : STATE_TEXT.none;

        $("trailerContent").innerHTML = `
            <div class="combination-card">
                <div class="combination-vehicle">
                    <div class="combination-icon">🚛</div>
                    <span>${escapeHtml(vehicle.vehicleType === "Tractor" ? "TEGLJAČ" : "VOZILO")}</span>
                    <strong>${escapeHtml(vehicle.registration)}</strong>
                    <small>${escapeHtml([vehicle.make, vehicle.model].filter(Boolean).join(" "))}</small>
                </div>
                <div class="combination-connector">⇄</div>
                <div class="combination-vehicle">
                    <div class="combination-icon trailer-icon">🚚</div>
                    <span>POLUPRIKOLICA</span>
                    <strong>${escapeHtml(trailer?.registration || "—")}</strong>
                    <small>${escapeHtml(trailer ? [trailer.make, trailer.model].filter(Boolean).join(" ") : "bez prikolice")}</small>
                </div>
            </div>
            <div class="trailer-compliance-grid">
                <article class="trailer-info-card"><span>Euro potvrda</span><strong class="${euroClass}">${euroText.toUpperCase()}</strong><small>${trailer ? date(trailer.euro.expiry) : "—"}</small></article>
                <article class="trailer-info-card"><span>Registracija</span><strong class="${regClass}">${regText.toUpperCase()}</strong><small>${trailer ? daysText(daysLeft(trailer.registrationDoc.expiry)) : "—"}</small></article>
                <article class="trailer-info-card"><span>Dokumentacija</span><strong class="status-good-text">${docState}</strong><small>${trailer ? "važećih dokumenata" : "—"}</small></article>
            </div>
            <div class="trailer-history">
                <div class="subsection-heading"><h4>Istorija povezivanja</h4></div>
                <div class="timeline">
                    ${vehicle.trailerHistory.map(item => `
                        <div class="timeline-item">
                            <span class="timeline-dot"></span>
                            <div><strong>${date(item.assignedAt)}</strong><span>${escapeHtml(vehicle.registration)} → ${escapeHtml(item.trailer || "bez prikolice")}</span></div>
                        </div>`).join("") || '<div class="timeline-item"><span class="timeline-dot"></span><div><strong>—</strong><span>Nema promena prikolice</span></div></div>'}
                </div>
            </div>`;
    }

    function renderService(vehicle) {
        const minor = vehicle.service.find(item => item.kind === "ServiceMinor");
        const major = vehicle.service.find(item => item.kind === "ServiceMajor");
        const box = (label, item) => `<div><span>${label}</span><strong>${item ? date(item.occurredAt) : "—"}</strong><small>${item?.odometerKm ? item.odometerKm.toLocaleString("sr-RS") + " km" : "nema zapisa"}</small></div>`;
        $("serviceSummary").innerHTML = box("Mali servis", minor) + box("Veliki servis", major) +
            `<div><span>Trenutna km</span><strong>${vehicle.odometerKm ? vehicle.odometerKm.toLocaleString("sr-RS") + " km" : "—"}</strong><small>poslednje očitavanje</small></div>`;

        $("serviceList").innerHTML = vehicle.service.map(item => `
            <article class="service-row">
                <div class="service-type ${item.kind === "ServiceMajor" ? "service-large" : "service-small"}">${item.kind === "ServiceMajor" ? "V" : "M"}</div>
                <div>
                    <strong>${escapeHtml(item.title)}</strong>
                    <span>${date(item.occurredAt)}${item.odometerKm ? " · " + item.odometerKm.toLocaleString("sr-RS") + " km" : ""}${item.workshop ? " · " + escapeHtml(item.workshop) : ""}</span>
                </div>
                <div class="service-cost"><span>Trošak</span><strong>${money(item.cost)}</strong></div>
            </article>`).join("") || '<article class="service-row"><div><strong>Nema servisa</strong><span>Dodajte servis dugmetom iznad.</span></div></article>';
    }

    function renderRepairs(vehicle) {
        $("repairList").innerHTML = vehicle.repairs.map(item => {
            const open = item.status === "Open";
            return `
                <article class="repair-row ${open ? "repair-open" : "repair-completed"}">
                    <div class="repair-status-dot"></div>
                    <div class="repair-main">
                        <strong>${escapeHtml(item.title)}</strong>
                        <span>${date(item.occurredAt)} · ${open ? "otvoreno" : "završeno"}${item.workshop ? " · " + escapeHtml(item.workshop) : ""}</span>
                        <small>${escapeHtml(item.description || "")}</small>
                    </div>
                    <div class="repair-cost">
                        <span>${open ? "Status" : "Trošak"}</span>
                        <strong>${open ? "OTVORENO" : money(item.cost)}</strong>
                        ${open ? `<button type="button" class="icon-action" data-resolve="${item.id}" title="Označi kao završeno">✓</button>` : ""}
                    </div>
                </article>`;
        }).join("") || '<article class="repair-row"><div class="repair-main"><strong>Nema popravki</strong></div></article>';
    }

    function renderFaults(vehicle) {
        $("faultList").innerHTML = vehicle.faults.map(item => {
            const open = item.status === "Open";
            const level = open ? (item.severity === "Critical" ? "critical" : "warning") : "resolved";
            return `
                <article class="fault-row fault-${level}">
                    <div class="fault-level">${open ? item.severity.toUpperCase() : "RESOLVED"}</div>
                    <div class="fault-main">
                        <strong>${escapeHtml(item.title)}</strong>
                        <span>${date(item.occurredAt)} · ${time(item.occurredAt)}${!open && item.resolvedAt ? " · rešeno " + date(item.resolvedAt) : ""}</span>
                        <small>${escapeHtml(item.description || "")}</small>
                    </div>
                    ${open
                        ? `<button type="button" class="fault-open-status" data-resolve="${item.id}" title="Označi kao rešeno">OPEN</button>`
                        : '<span class="fault-resolved-status">RESOLVED</span>'}
                </article>`;
        }).join("") || '<article class="fault-row fault-resolved"><div class="fault-main"><strong>Nema prijavljenih grešaka</strong></div></article>';
    }

    function renderNotes(vehicle) {
        $("notesList").innerHTML = vehicle.notes.map(item => `
            <article class="note-card">
                <div class="note-date">${date(item.occurredAt)}</div>
                <strong>${escapeHtml(item.title)}</strong>
                <span>Unos: ${escapeHtml(item.createdByName || "—")}${item.description ? " · " + escapeHtml(item.description) : ""}</span>
            </article>`).join("") || '<article class="note-card"><strong>Nema napomena</strong></article>';
    }

    function renderHistory(vehicle) {
        const dot = action => action.includes("Document") || action.includes("Create") ? "audit-green"
            : action.includes("Fault") || action.includes("Resolve") ? "audit-red" : "audit-blue";
        $("auditTimeline").innerHTML = vehicle.history.map(item => {
            let details = "";
            try {
                const value = item.newValueJson ? JSON.parse(item.newValueJson) : null;
                details = value ? Object.values(value).filter(v => v !== null && v !== "").join(" · ") : "";
            } catch { details = ""; }
            return `
                <article>
                    <div class="audit-dot ${dot(item.action)}"></div>
                    <div>
                        <strong>${escapeHtml(item.action)}</strong>
                        <span>${date(item.createdAt)} · ${escapeHtml(item.user)}</span>
                        <small>${escapeHtml(details)}</small>
                    </div>
                </article>`;
        }).join("") || '<article><div class="audit-dot audit-blue"></div><div><strong>Nema zabeleženih promena</strong></div></article>';
    }

    // ---- Data ----

    async function load() {
        try {
            state.data = await request("/api/compliance");
            if (!state.data.vehicles.some(vehicle => vehicle.id === state.selectedId)) {
                const first = state.data.vehicles.find(vehicle => vehicle.status === "blocked") || state.data.vehicles[0];
                state.selectedId = first?.id || null;
            }
            renderSummary();
            renderList();
            renderDetail();
        } catch (error) {
            showToast("Greška", error.message);
        }
    }

    // ---- Modals ----

    function openModal(modal) {
        modal.classList.add("open");
        modal.setAttribute("aria-hidden", "false");
        document.body.classList.add("pm-modal-open");
    }

    function closeModals() {
        root.ownerDocument.querySelectorAll(".compliance-modal.open").forEach(modal => {
            modal.classList.remove("open");
            modal.setAttribute("aria-hidden", "true");
        });
        document.body.classList.remove("pm-modal-open");
    }

    function openDocument(type) {
        if (!selected()) return;
        const form = $("documentForm");
        form.reset();
        $("documentType").value = type || "";
        $("documentModalTitle").textContent = type ? "Ažuriranje dokumenta" : "Novi dokument";
        const content = root.ownerDocument.querySelector(".file-upload-content");
        if (content && content.dataset.original) content.innerHTML = content.dataset.original;
        openModal($("documentModal"));
        window.setTimeout(() => $("documentType").focus(), 50);
    }

    function localNow() {
        const now = new Date();
        return new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    }

    function openLog(kind) {
        if (!selected()) return;
        const config = LOG_FORMS[kind];
        state.logForm = kind;
        $("logForm").reset();
        $("logModalKicker").textContent = config.kicker;
        $("logModalTitle").textContent = config.title;
        $("logKindField").hidden = !config.kinds;
        $("logSeverityField").hidden = !config.severity;
        $("logCostField").hidden = !config.cost;
        $("logOdometerField").hidden = !config.odometer;
        $("logWorkshopField").hidden = !config.workshop;
        $("logCompletedField").hidden = !config.completed;
        $("logDate").value = localNow();
        if (config.odometer && selected().odometerKm) $("logOdometer").value = selected().odometerKm;
        openModal($("logModal"));
        window.setTimeout(() => $("logTitle").focus(), 50);
    }

    async function saveDocument(event) {
        event.preventDefault();
        const vehicle = selected();
        if (!vehicle) return;
        if (!$("documentType").value) { showToast("Nedostaje tip", "Izaberite tip dokumenta."); return; }
        if (!$("documentExpiryDate").value) { showToast("Nedostaje datum", "Unesite datum važenja dokumenta."); return; }

        const body = new FormData();
        body.append("documentType", $("documentType").value);
        body.append("expiryDate", $("documentExpiryDate").value);
        [["number", "documentNumber"], ["issueDate", "documentIssueDate"], ["issuer", "documentIssuer"], ["note", "documentNote"]].forEach(([key, id]) => {
            const value = $(id).value.trim();
            if (value) body.append(key, value);
        });
        const file = $("documentFile").files[0];
        if (file) body.append("file", file);

        try {
            await request(`/api/compliance/vehicles/${vehicle.id}/documents`, { method: "POST", body });
            closeModals();
            showToast("Dokument sačuvan", `${vehicle.registration}: dokument je evidentiran.`);
            load();
        } catch (error) {
            showToast("Greška", error.message);
        }
    }

    async function saveLog(event) {
        event.preventDefault();
        const vehicle = selected();
        const config = LOG_FORMS[state.logForm];
        if (!vehicle || !config) return;
        const title = $("logTitle").value.trim();
        if (!title) { showToast("Nedostaje naslov", "Unesite naslov."); return; }

        const number = id => $(id).value === "" ? null : Number($(id).value);
        const payload = {
            kind: config.kinds ? $("logKind").value : config.kind,
            title,
            description: $("logDescription").value.trim() || null,
            occurredAt: $("logDate").value ? new Date($("logDate").value).toISOString() : null,
            odometerKm: config.odometer ? number("logOdometer") : null,
            cost: config.cost ? number("logCost") : null,
            severity: config.severity ? $("logSeverity").value : null,
            workshop: config.workshop ? ($("logWorkshop").value.trim() || null) : null,
            completed: config.completed ? $("logCompleted").value === "true" : true
        };

        try {
            await request(`/api/compliance/vehicles/${vehicle.id}/log`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) });
            closeModals();
            showToast("Sačuvano", `${config.title}: ${vehicle.registration}.`);
            load();
        } catch (error) {
            showToast("Greška", error.message);
        }
    }

    async function saveTrailer(event) {
        event.preventDefault();
        const vehicle = selected();
        if (!vehicle) return;
        try {
            await request(`/api/compliance/vehicles/${vehicle.id}/trailer`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ trailerId: $("trailerSelect").value || null })
            });
            closeModals();
            showToast("Prikolica promenjena", vehicle.registration);
            load();
        } catch (error) {
            showToast("Greška", error.message);
        }
    }

    async function resolve(id) {
        try {
            await request(`/api/compliance/log/${id}/resolve`, { method: "POST" });
            showToast("Rešeno", "Zapis je označen kao završen.");
            load();
        } catch (error) {
            showToast("Greška", error.message);
        }
    }

    // ---- Events ----

    root.querySelectorAll("[data-status-filter]").forEach(button => {
        button.addEventListener("click", () => {
            root.querySelectorAll("[data-status-filter]").forEach(item => item.classList.toggle("active", item === button));
            state.status = button.dataset.statusFilter;
            renderList();
        });
    });

    $("vehicleSearch").addEventListener("input", renderList);
    $("vehicleTypeFilter").addEventListener("change", renderList);

    $("vehicleList").addEventListener("click", event => {
        const card = event.target.closest("[data-vehicle-id]");
        if (!card) return;
        state.selectedId = card.dataset.vehicleId;
        renderDetail();
        if (window.matchMedia("(max-width: 1100px)").matches) $("vehicleDetailPanel").scrollIntoView({ behavior: "smooth", block: "start" });
    });

    root.querySelectorAll(".detail-tab").forEach(tab => {
        tab.addEventListener("click", () => {
            root.querySelectorAll(".detail-tab").forEach(item => item.classList.toggle("active", item === tab));
            root.querySelectorAll(".detail-tab-content").forEach(content => content.classList.toggle("active", content.dataset.content === tab.dataset.tab));
        });
    });

    $("showBlockedVehicles")?.addEventListener("click", () => root.querySelector('[data-status-filter="blocked"]')?.click());
    $("refreshCompliance")?.addEventListener("click", () => load().then(() => showToast("Osveženo", "Podaci su ponovo učitani.")));
    $("addDocumentTop")?.addEventListener("click", () => openDocument());
    $("addDocument")?.addEventListener("click", () => openDocument());
    $("repairCompliance")?.addEventListener("click", () => {
        const expired = selected()?.documents.find(doc => doc.state === "expired");
        openDocument(expired?.documentType);
    });
    $("documentList").addEventListener("click", event => {
        const button = event.target.closest("[data-document]");
        if (button) openDocument(button.dataset.document);
    });
    $("addService")?.addEventListener("click", () => openLog("service"));
    $("addRepair")?.addEventListener("click", () => openLog("repair"));
    $("addFault")?.addEventListener("click", () => openLog("fault"));
    $("addNote")?.addEventListener("click", () => openLog("note"));
    $("changeTrailer")?.addEventListener("click", () => {
        $("trailerSelect").value = selected()?.trailer?.id || "";
        openModal($("trailerModal"));
    });
    root.ownerDocument.addEventListener("click", event => {
        const button = event.target.closest("[data-resolve]");
        if (button) resolve(button.dataset.resolve);
        if (event.target.closest("[data-close-modal]")) closeModals();
    });
    document.addEventListener("keydown", event => { if (event.key === "Escape") closeModals(); });

    $("documentForm").addEventListener("submit", saveDocument);
    $("logForm").addEventListener("submit", saveLog);
    $("trailerForm").addEventListener("submit", saveTrailer);

    const fileInput = $("documentFile");
    const fileContent = root.ownerDocument.querySelector(".file-upload-content");
    if (fileContent) fileContent.dataset.original = fileContent.innerHTML;
    fileInput?.addEventListener("change", () => {
        const file = fileInput.files && fileInput.files[0];
        if (file && fileContent) {
            fileContent.innerHTML = `<strong>${escapeHtml(file.name)}</strong><span>${Math.round(file.size / 1024)} KB · spremno za upload</span>`;
        }
    });

    load();
})();
