(() => {
    "use strict";

    // Driver portal from the database (/api/driver/portal): active trip and its stops.
    // Actions: arrival at / completion of the next stop, break start/end, incident report
    // (an alert for dispatch) and a call to the dispatch phone.

    const $ = id => document.getElementById(id);
    if (!$("driverStops")) return;

    const TYPE = {
        Loading: ["Utovar", ""], Unloading: ["Istovar", "pm-badge--success"], LoadingAndUnloading: ["Utovar i istovar", ""],
        Border: ["Granica", "pm-badge--warning"], Rest: ["Odmor", ""], Fuel: ["Gorivo", ""], Other: ["Stanica", ""]
    };
    const STATUS = { Planned: "planirano", Arrived: "na stanici", InService: "u toku", Completed: "završeno", Skipped: "preskočeno" };
    const TRIP_STATE = { Active: "Na dužnosti", Paused: "Pauza", Planned: "Pre polaska", Completed: "Tura završena" };

    let portal = null;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;");
    }

    function when(iso) {
        if (!iso) return "";
        const d = new Date(iso);
        return `${d.toLocaleDateString("sr-Latn-RS", { day: "2-digit", month: "2-digit" })} ${d.toLocaleTimeString("sr-Latn-RS", { hour: "2-digit", minute: "2-digit" })}`;
    }

    function message(text, isError) {
        const box = $("driverMessage");
        box.hidden = !text;
        box.textContent = text || "";
        box.classList.toggle("pm-text-danger", !!isError);
    }

    function render() {
        const trip = portal.trip;
        const stops = portal.stops;
        const next = stops.find(stop => stop.id === portal.nextStopId);

        $("driverSubtitle").textContent = `${portal.driver.fullName}${portal.driver.employeeNumber ? " · " + portal.driver.employeeNumber : ""}`;
        $("driverTrip").textContent = trip?.tripNumber || "Nema ture";
        $("driverRoute").textContent = trip ? `${trip.route}${trip.vehicle ? " · " + trip.vehicle : ""}` : "Dispečer još nije dodelio turu.";
        $("driverNextStop").textContent = next ? (next.city || next.name) : "—";
        $("driverNextStopInfo").textContent = next ? `${TYPE[next.type]?.[0] || next.type} · ${when(next.plannedAt) || "bez termina"}` : (trip ? "Sve stanice su završene" : "—");
        $("driverState").textContent = trip ? (TRIP_STATE[trip.status] || trip.status) : "Slobodan";
        $("driverStateInfo").textContent = trip ? `Napredak ${Math.round(trip.routeProgressPercent || 0)}%${trip.plannedArrivalAt ? " · dolazak " + when(trip.plannedArrivalAt) : ""}` : "—";

        $("driverStops").innerHTML = stops.map(stop => {
            const [label, cls] = TYPE[stop.type] || TYPE.Other;
            const done = stop.status === "Completed";
            return `
                <div class="pm-list-row">
                    <div><strong>${stop.sequence} · ${escapeHtml(stop.city || stop.name)}</strong>
                        <div class="pm-text-muted">${escapeHtml(stop.name)} · ${STATUS[stop.status] || stop.status}${stop.plannedAt ? " · " + when(stop.plannedAt) : ""}${stop.notes ? " · " + escapeHtml(stop.notes) : ""}</div>
                    </div><span class="pm-badge ${done ? "pm-badge--success" : cls}">${done ? "Završeno" : label}</span>
                </div>`;
        }).join("") || '<div class="pm-list-row"><div><strong>Nema stanica</strong><div class="pm-text-muted">Nema aktivne ture.</div></div></div>';

        const stopAction = $("driverStopAction");
        stopAction.disabled = !next;
        stopAction.textContent = next && next.status !== "Planned" ? "Završi stanicu" : "Stigao na stanicu";

        const pause = $("driverPause");
        pause.disabled = !trip || !["Active", "Paused"].includes(trip.status);
        pause.textContent = trip?.status === "Paused" ? "Završi pauzu" : "Započni pauzu";

        $("driverIncident").disabled = false;

        const call = $("driverCall");
        if (portal.dispatchPhone) {
            call.href = "tel:" + portal.dispatchPhone.replace(/\s+/g, "");
            call.removeAttribute("aria-disabled");
            call.title = portal.dispatchPhone;
        }
    }

    async function load() {
        try {
            const response = await ProMapCargo.initialData.fetch("/api/driver/portal", { headers: { "Accept": "application/json" }, credentials: "same-origin" });
            const data = await response.json().catch(() => null);
            if (response.status === 401) throw new Error("Prijavite se nalogom vozača.");
            if (response.status === 403) throw new Error("Portal je dostupan vozačima i administratoru.");
            if (!response.ok) throw new Error(data?.message || `Portal nije učitan (${response.status}).`);
            portal = data;
            message("");
            render();
        } catch (error) {
            message(error.message, true);
            $("driverStops").innerHTML = '<div class="pm-list-row"><div><strong>Nema podataka</strong></div></div>';
            // Without a portal (e.g. an account not linked to a driver) there is nothing to act on.
            ["driverStopAction", "driverPause", "driverIncident"].forEach(id => { if ($(id)) $(id).disabled = true; });
        }
    }

    async function post(url, body) {
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json", "Accept": "application/json" },
            credentials: "same-origin",
            body: body ? JSON.stringify(body) : null
        });
        const data = await response.json().catch(() => null);
        if (!response.ok) throw new Error(data?.message || `Akcija nije uspela (${response.status}).`);
        return data;
    }

    async function run(action, success) {
        try {
            await action();
            message(success);
            await load();
            message(success);
        } catch (error) {
            message(error.message, true);
        }
    }

    $("driverStopAction").addEventListener("click", () => {
        const next = portal?.stops.find(stop => stop.id === portal.nextStopId);
        if (!next) return;
        const arriving = next.status === "Planned";
        run(() => post(`/api/driver/stops/${next.id}/${arriving ? "arrive" : "complete"}`),
            arriving ? `Dolazak na ${next.city || next.name} je evidentiran.` : `Stanica ${next.city || next.name} je završena.`);
    });

    $("driverPause").addEventListener("click", () => {
        const pausing = portal?.trip?.status === "Active";
        run(() => post("/api/driver/pause"), pausing ? "Pauza je započeta." : "Pauza je završena, vožnja se nastavlja.");
    });

    $("driverIncident").addEventListener("click", () => {
        $("incidentForm").hidden = false;
        $("incidentMessage").focus();
    });

    $("incidentCancel").addEventListener("click", () => {
        $("incidentForm").reset();
        $("incidentForm").hidden = true;
    });

    $("incidentForm").addEventListener("submit", event => {
        event.preventDefault();
        const text = $("incidentMessage").value.trim();
        if (!text) return;
        run(() => post("/api/driver/incident", { message: text, severity: $("incidentSeverity").value }), "Incident je prijavljen dispečeru.")
            .then(() => {
                $("incidentForm").reset();
                $("incidentForm").hidden = true;
            });
    });

    $("driverCall").addEventListener("click", event => {
        if ($("driverCall").getAttribute("aria-disabled") === "true") {
            event.preventDefault();
            message("Broj dispečera nije unet za firmu.", true);
        }
    });

    load();
})();
