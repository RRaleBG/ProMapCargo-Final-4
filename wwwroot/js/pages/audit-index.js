(() => {
    "use strict";

    // Audit trail from the database (/api/audit) with filters and CSV export.

    const $ = id => document.getElementById(id);
    const rows = $("auditRows");
    if (!rows) return;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function query() {
        const params = new URLSearchParams();
        [["user", "auditUser"], ["action", "auditAction"], ["entity", "auditEntity"], ["from", "auditFrom"]].forEach(([key, id]) => {
            const value = $(id).value.trim();
            if (value) params.set(key, value);
        });
        return params;
    }

    // "Driver.Incident" → warning badge; status changes and creations → success.
    function badge(action) {
        const warning = /Incident|Reject|Fault|Pause/i.test(action);
        return `<span class="pm-badge ${warning ? "pm-badge--warning" : "pm-badge--success"}">${warning ? "Upozorenje" : "Uspešno"}</span>`;
    }

    function details(json) {
        if (!json) return "";
        try {
            return Object.entries(JSON.parse(json)).map(([key, value]) => `${key}: ${value}`).join(", ");
        } catch {
            return json;
        }
    }

    async function load() {
        rows.innerHTML = '<tr><td colspan="5">Učitavanje…</td></tr>';
        try {
            const response = await ProMapCargo.initialData.fetch(`/api/audit?${query()}`, { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            if (response.status === 401) throw new Error("Prijavite se da biste videli audit.");
            if (!response.ok) throw new Error(`Audit nije učitan (${response.status}).`);
            const items = await response.json();

            $("auditSummary").textContent = `${items.length} događaja${items.length === 1000 ? " (prikazano najnovijih 1000)" : ""}`;
            rows.innerHTML = items.map(item => `
                <tr title="${escapeHtml(details(item.newValueJson))}">
                    <td>${escapeHtml(new Date(item.createdAt).toLocaleString("sr-Latn-RS"))}</td>
                    <td>${escapeHtml(item.user)}</td>
                    <td>${escapeHtml(item.action)}</td>
                    <td>${escapeHtml(item.entityType)} · ${escapeHtml(item.entityId.slice(0, 8))}</td>
                    <td>${badge(item.action)} <small>${escapeHtml(details(item.newValueJson))}</small></td>
                </tr>`).join("") || '<tr><td colspan="5">Nema događaja za izabrane filtere.</td></tr>';
        } catch (error) {
            rows.innerHTML = `<tr><td colspan="5">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    $("auditFilters").addEventListener("submit", event => {
        event.preventDefault();
        load();
    });

    $("auditClear").addEventListener("click", () => {
        $("auditFilters").reset();
        load();
    });

    $("auditExport").addEventListener("click", () => {
        const params = query();
        params.set("format", "csv");
        window.location.href = `/api/audit?${params}`;
    });

    load();
})();
