document.addEventListener("DOMContentLoaded", () => {
    "use strict";

    // Moderation queue from the database (/api/moderation). A decision (approve,
    // request changes, reject) is recorded with the moderator, time and reason; nothing
    // is applied to routes or roads.

    const root = document.querySelector('[data-module="moderation"]');
    if (!root) {
        return;
    }

    const $ = id => document.getElementById(id);

    const DOMAIN = { routing: "Routing", safety: "Safety", dispatch: "Dispatch", eta: "ETA" };
    const RISK = { high: "HIGH", medium: "MEDIUM", low: "LOW" };
    const DECIDED = {
        approved: ["status-approved", "APPROVED"],
        rejected: ["status-rejected", "REJECTED"],
        changes: ["status-rejected", "IZMENE"]
    };

    const state = {
        data: null,
        status: "all",
        selectedId: null
    };

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function ago(iso) {
        const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
        if (minutes < 60) return `pre ${Math.max(minutes, 0)} min`;
        if (minutes < 60 * 24) return `pre ${Math.round(minutes / 60)} h`;
        return new Date(iso).toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit", year: "numeric" });
    }

    function clock(iso) {
        return iso ? new Date(iso).toLocaleTimeString("sr-Latn-RS", { hour: "2-digit", minute: "2-digit" }) : "—";
    }

    function day(iso) {
        return iso ? new Date(iso).toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit", year: "numeric" }) : "—";
    }

    let toastTimer = null;

    function showToast(title, message) {
        const toast = $("moderationToast");
        if (!toast) return;
        $("toastTitle").textContent = title;
        $("toastText").textContent = message;
        toast.classList.add("show");
        if (toastTimer) clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toast.classList.remove("show"), 2400);
    }

    function selected() {
        return state.data?.requests.find(item => item.id === state.selectedId) || null;
    }

    // ---- Queue ----

    function cardHtml(item) {
        const badge = item.status === "pending"
            ? `<span class="risk-badge risk-${item.risk}">${RISK[item.risk] || item.risk}</span>`
            : `<span class="status-badge ${DECIDED[item.status]?.[0] || "status-rejected"}">${DECIDED[item.status]?.[1] || item.status}</span>`;
        const meta = [
            item.vehicle ? `<span>🚛 ${escapeHtml(item.vehicle)}</span>` : "",
            item.trip ? `<span>↗ ${escapeHtml(item.trip)}</span>` : "",
            `<span>${escapeHtml(DOMAIN[item.domain] || item.domain)}</span>`
        ].join("");

        return `
            <article class="moderation-request pm-card${item.id === state.selectedId ? " selected" : ""}" data-id="${item.id}" data-status="${item.status}">
                <div class="request-top">
                    <div>
                        <strong>${escapeHtml(item.number)}</strong>
                        <span>${ago(item.createdAt)} · ${escapeHtml(item.requestedByName || "—")}</span>
                    </div>
                    ${badge}
                </div>
                <h3>${escapeHtml(item.title)}</h3>
                <p>${escapeHtml(item.description || "")}</p>
                <div class="request-meta">${meta}</div>
            </article>`;
    }

    function visible() {
        const query = ($("moderationSearch").value || "").toLowerCase().trim();
        const risk = $("riskFilter").value;
        const domain = $("domainFilter").value;
        return state.data.requests.filter(item =>
            (state.status === "all" || item.status === state.status)
            && (risk === "all" || item.risk === risk)
            && (domain === "all" || item.domain === domain)
            && (!query || [item.number, item.title, item.description, item.vehicle, item.trip, item.requestedByName].join(" ").toLowerCase().includes(query)));
    }

    function renderQueue() {
        const items = visible();
        $("moderationQueue").innerHTML = items.map(cardHtml).join("") ||
            '<article class="moderation-request pm-card"><h3>Nema zahteva</h3><p>Promenite filter ili pretragu.</p></article>';
        $("queueCount").textContent = `${items.length} zahteva`;
    }

    function renderSummary() {
        const kpi = state.data.kpi;
        $("kpiPending").textContent = kpi.pending;
        $("kpiHighRisk").textContent = kpi.highRisk;
        $("kpiApproved").textContent = kpi.approvedMonth;
        $("kpiRejected").textContent = kpi.rejectedMonth;
        $("kpiToday").textContent = kpi.today;
        $("kpiAverage").textContent = kpi.averageMinutes < 60 ? `${kpi.averageMinutes}m` : `${Math.round(kpi.averageMinutes / 60)}h`;
        $("moderationBannerTitle").textContent = kpi.pending === 0 ? "Nema zahteva na čekanju" : `${kpi.pending} zahteva čekaju odluku`;

        const counts = { all: state.data.requests.length };
        state.data.requests.forEach(item => { counts[item.status] = (counts[item.status] || 0) + 1; });
        root.querySelectorAll("[data-count]").forEach(counter => { counter.textContent = counts[counter.dataset.count] || 0; });
    }

    // ---- Detail ----

    function renderDetail() {
        root.querySelectorAll(".moderation-request").forEach(card => card.classList.toggle("selected", card.dataset.id === state.selectedId));
        const item = selected();
        if (!item) return;

        $("detailTitle").textContent = `${item.number} · ${item.title}`;
        $("detailSubtitle").textContent = [item.vehicle, item.trip, DOMAIN[item.domain] || item.domain].filter(Boolean).join(" · ");
        const risk = $("detailRisk");
        risk.className = `risk-badge risk-${item.risk}`;
        risk.textContent = `${RISK[item.risk] || item.risk} RISK`;
        $("detailWarning").hidden = item.risk !== "high" || item.status !== "pending";
        $("detailWarningTitle").textContent = item.domain === "safety" ? "Promena utiče na bezbednost vozača" : "Zahtev visokog rizika";

        $("detailRequester").textContent = item.requestedByName || "—";
        $("detailDomain").textContent = DOMAIN[item.domain] || item.domain;
        $("detailReceivedTime").textContent = clock(item.createdAt);
        $("detailReceivedDate").textContent = day(item.createdAt);
        $("detailVehicle").textContent = item.vehicle || "—";
        $("detailTrip").textContent = item.trip || "—";
        $("detailChange").textContent = item.title;
        $("detailImpact").textContent = item.impactSummary || "—";
        $("detailReason").textContent = item.description || "—";

        const checks = [
            [true, "Podnosilac je evidentiran"],
            [Boolean(item.vehicle), item.vehicle ? "Zahtev je vezan za vozilo" : "Zahtev nije vezan za vozilo"],
            [true, `Zahtev ima audit ID ${item.number} i vreme prijema`],
            [item.risk !== "high", item.risk === "high" ? "Visok rizik: potrebna obrazložena odluka" : "Rizik ne zahteva dodatnu potvrdu"]
        ];
        $("policyChecks").innerHTML = checks.map(([ok, text]) => `<label><span class="${ok ? "check-ok" : "check-warn"}">${ok ? "✓" : "!"}</span>${escapeHtml(text)}</label>`).join("");

        const pending = item.status === "pending";
        $("decisionActions").querySelectorAll("button").forEach(button => { button.disabled = !pending; });
        const reason = $("decisionReason");
        reason.disabled = !pending;
        reason.value = pending ? "" : (item.decisionNote || "");

        const history = [
            ["", "Zahtev kreiran", `${item.requestedByName || "—"} · ${day(item.createdAt)} ${clock(item.createdAt)}`],
            ["", "Policy check pokrenut", `System · ${clock(item.createdAt)}`],
            pending
                ? ["audit-current", "Čeka odluku moderatora", `Queue · ${ago(item.createdAt)}`]
                : ["audit-current", `${DECIDED[item.status]?.[1] || item.status}${item.decisionNote ? ": " + item.decisionNote : ""}`, `${item.decidedByName || "—"} · ${day(item.decidedAt)} ${clock(item.decidedAt)}`]
        ];
        $("requestHistory").innerHTML = history.map(([cls, title, meta]) => `
            <div class="audit-item">
                <i class="${cls}"></i>
                <div><strong>${escapeHtml(title)}</strong><span>${escapeHtml(meta)}</span></div>
            </div>`).join("");
    }

    // ---- Data ----

    async function load() {
        try {
            const response = await ProMapCargo.initialData.fetch("/api/moderation", { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            if (response.status === 401) throw new Error("Prijavite se ponovo.");
            if (!response.ok) throw new Error(`Zahtevi nisu učitani (${response.status}).`);
            state.data = await response.json();
            if (!state.data.requests.some(item => item.id === state.selectedId)) {
                state.selectedId = (state.data.requests.find(item => item.status === "pending") || state.data.requests[0])?.id || null;
            }
            renderSummary();
            renderQueue();
            renderDetail();
        } catch (error) {
            showToast("Greška", error.message);
        }
    }

    async function decide(status) {
        const item = selected();
        if (!item) return;
        const note = $("decisionReason").value.trim();
        if (status !== "approved" && !note) {
            showToast("Obrazloženje je obavezno", "Unesite razlog odbijanja ili traženih izmena.");
            $("decisionReason").focus();
            return;
        }

        const response = await fetch(`/api/moderation/${item.id}/decision`, {
            method: "POST",
            headers: { "Content-Type": "application/json", "Accept": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify({ status, note: note || null })
        });
        const data = await response.json().catch(() => null);
        if (!response.ok) {
            showToast("Greška", data?.message || (response.status === 403 ? "Odluku donosi moderator, dispečer ili administrator." : "Odluka nije sačuvana."));
            return;
        }

        showToast("Odluka evidentirana", `${item.number}: ${DECIDED[status][1]}`);
        load();
    }

    // ---- Events ----

    root.querySelectorAll(".moderation-filter").forEach(button => {
        button.addEventListener("click", () => {
            root.querySelectorAll(".moderation-filter").forEach(item => item.classList.toggle("active", item === button));
            state.status = button.dataset.status;
            renderQueue();
        });
    });

    $("moderationSearch").addEventListener("input", renderQueue);
    $("riskFilter").addEventListener("change", renderQueue);
    $("domainFilter").addEventListener("change", renderQueue);

    $("moderationQueue").addEventListener("click", event => {
        const card = event.target.closest("[data-id]");
        if (!card) return;
        state.selectedId = card.dataset.id;
        renderDetail();
    });

    $("decisionActions").addEventListener("click", event => {
        const button = event.target.closest("[data-decision]");
        if (button && !button.disabled) decide(button.dataset.decision);
    });

    $("showPending")?.addEventListener("click", () => root.querySelector('.moderation-filter[data-status="pending"]')?.click());
    $("refreshModeration")?.addEventListener("click", () => load().then(() => showToast("Osveženo", "Zahtevi su ponovo učitani.")));

    load();
});
