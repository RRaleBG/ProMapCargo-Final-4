(() => {
    "use strict";

    // Reports from the database: key figures (/api/reports/kpi) and the four report types
    // (/api/reports/{type}) as a table, CSV, or PDF through the browser's print dialog.

    const $ = id => document.getElementById(id);
    const form = $("reportForm");
    if (!form) return;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function isoDay(date) {
        return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    }

    function setKpi(id, value, suffix = "%") {
        $(id).textContent = value === null || value === undefined ? "—" : `${String(value).replace(".", ",")}${suffix}`;
        const bar = $(id + "Bar");
        if (bar && value !== null) {
            bar.setAttribute("aria-valuenow", String(value));
            bar.querySelector("span").style.setProperty("--pm-progress", `${Math.min(100, value)}%`);
        }
    }

    async function loadKpi() {
        try {
            const response = await ProMapCargo.initialData.fetch("/api/reports/kpi", { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            if (!response.ok) return;
            const kpi = await response.json();
            setKpi("kpiUtilization", kpi.fleetUtilization);
            setKpi("kpiOnTime", kpi.onTimeDeliveries);
            setKpi("kpiDeviation", kpi.routeDeviation);
        } catch { }
    }

    function params(format) {
        const query = new URLSearchParams();
        if ($("reportFrom").value) query.set("from", $("reportFrom").value);
        if ($("reportTo").value) query.set("to", $("reportTo").value);
        if (format) query.set("format", format);
        return query;
    }

    async function generate() {
        const result = $("reportResult");
        result.hidden = false;
        $("reportRows").innerHTML = '<tr><td>Učitavanje…</td></tr>';
        try {
            const response = await fetch(`/api/reports/${$("reportType").value}?${params()}`, { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            const data = await response.json().catch(() => null);
            if (!response.ok) throw new Error(data?.message || `Izveštaj nije generisan (${response.status}).`);

            $("reportTitle").textContent = data.title;
            $("reportPeriod").textContent = `${new Date(data.from).toLocaleDateString("sr-Latn-RS")} – ${new Date(data.to).toLocaleDateString("sr-Latn-RS")} · ${data.rows.length} redova`;
            $("reportHead").innerHTML = `<tr>${data.columns.map(column => `<th scope="col">${escapeHtml(column)}</th>`).join("")}</tr>`;
            $("reportRows").innerHTML = data.rows.map(row => `<tr>${row.map(cell => `<td>${escapeHtml(cell)}</td>`).join("")}</tr>`).join("") ||
                `<tr><td colspan="${data.columns.length}">Nema podataka za izabrani period.</td></tr>`;
        } catch (error) {
            $("reportHead").innerHTML = "";
            $("reportRows").innerHTML = `<tr><td>${escapeHtml(error.message)}</td></tr>`;
        }
    }

    function downloadCsv() {
        window.location.href = `/api/reports/${$("reportType").value}?${params("csv")}`;
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        generate();
    });

    $("reportCsv").addEventListener("click", downloadCsv);
    $("reportExportTop").addEventListener("click", downloadCsv);
    $("reportPdf").addEventListener("click", async () => {
        if ($("reportResult").hidden) await generate();
        window.print();
    });
    $("reportNew").addEventListener("click", () => {
        form.reset();
        setDefaultPeriod();
        $("reportResult").hidden = true;
        $("reportType").focus();
    });

    function setDefaultPeriod() {
        const today = new Date();
        $("reportTo").value = isoDay(today);
        $("reportFrom").value = isoDay(new Date(today.getFullYear(), today.getMonth(), 1));
    }

    setDefaultPeriod();
    loadKpi();
})();
