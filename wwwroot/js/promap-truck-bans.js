/*
 * Truck driving bans on the navigation map (web and the app's WebView).
 *
 * Countries with an approved ban in force right now are painted dark red: solid for a
 * nationwide ban (impassable for the truck), lighter with a dashed border when the ban
 * covers only some roads. Bans come from /api/truck-bans (approved on /truck-bans), their
 * local times are evaluated in each country's time zone, and a ban only counts for a truck
 * heavier than the country's limit (#navWeight). Re-evaluated every minute, reloaded
 * every 30 minutes. Clicking a painted country shows the time window and affected roads.
 */
(function () {
    "use strict";

    const COUNTRIES_URL = "/data/truck-ban-countries.geojson";
    const BANS_URL = "/api/truck-bans";
    const SOURCE = "promap-truck-bans";
    const FILL = "promap-truck-bans-fill";
    const LINE = "promap-truck-bans-line";
    const LINE_REGIONAL = "promap-truck-bans-line-regional";
    const RED = "#7f1d1d";
    const RED_LINE = "#dc2626";

    const state = { map: null, countries: null, calendar: null, popup: null };

    function truckWeightT() {
        const value = parseFloat(document.getElementById("navWeight")?.value ?? "");
        return Number.isFinite(value) && value > 0 ? value : 40;
    }

    // Date ("YYYY-MM-DD") and minute of the day of a moment in a time zone.
    function localParts(moment, timeZone) {
        const parts = Object.fromEntries(
            new Intl.DateTimeFormat("en-CA", {
                timeZone,
                year: "numeric",
                month: "2-digit",
                day: "2-digit",
                hour: "2-digit",
                minute: "2-digit",
                hourCycle: "h23",
            })
                .formatToParts(moment)
                .map((p) => [p.type, p.value]),
        );
        return { date: `${parts.year}-${parts.month}-${parts.day}`, minute: Number(parts.hour) * 60 + Number(parts.minute) };
    }

    function minutes(clock) {
        const [h, m] = clock.split(":").map(Number);
        return h * 60 + m;
    }

    // Bans in force now for this truck, grouped by country code.
    function activeBans(now = new Date()) {
        const result = new Map();
        if (!state.calendar) return result;
        const weight = truckWeightT();
        const countries = new Map(state.calendar.countries.map((c) => [c.countryCode, c]));
        for (const ban of state.calendar.bans) {
            const country = countries.get(ban.countryCode);
            if (!country) continue;
            if (country.minWeightT != null && weight <= country.minWeightT) continue;
            const local = localParts(now, country.timeZone);
            if (local.date !== ban.date) continue;
            if (local.minute < minutes(ban.start) || local.minute >= minutes(ban.end)) continue;
            const list = result.get(ban.countryCode) ?? [];
            list.push(ban);
            result.set(ban.countryCode, list);
        }
        return result;
    }

    function render() {
        const map = state.map;
        const source = map?.getSource(SOURCE);
        if (!source || !state.countries) return;
        const active = activeBans();
        const features = state.countries.features
            .filter((f) => active.has(f.properties.code))
            .map((f) => {
                const bans = active.get(f.properties.code);
                return {
                    ...f,
                    properties: {
                        ...f.properties,
                        // Nationwide when any ban in force covers the whole country.
                        nationwide: bans.some((b) => !b.regional),
                        summary: bans.map((b) => `${b.start}–${b.end}${b.regional ? " (deo puteva)" : ""}`).join(", "),
                        info: bans.map((b) => b.info).filter(Boolean).join("\n\n"),
                    },
                };
            });
        source.setData({ type: "FeatureCollection", features });
    }

    // Below the labels and the route, above the base map.
    function firstOverlayLayerId(map) {
        const layer = map.getStyle().layers.find((l) => l.type === "symbol" || l.id.startsWith("route"));
        return layer?.id;
    }

    function addLayers(map) {
        if (map.getSource(SOURCE)) return;
        map.addSource(SOURCE, { type: "geojson", data: { type: "FeatureCollection", features: [] } });
        const before = firstOverlayLayerId(map);
        map.addLayer({
            id: FILL,
            type: "fill",
            source: SOURCE,
            paint: { "fill-color": RED, "fill-opacity": ["case", ["get", "nationwide"], 0.45, 0.18] },
        }, before);
        // line-dasharray cannot depend on feature data, so the solid border (nationwide ban)
        // and the dashed one (some roads only) are two layers split by a filter.
        map.addLayer({
            id: LINE,
            type: "line",
            source: SOURCE,
            filter: ["==", ["get", "nationwide"], true],
            paint: { "line-color": RED_LINE, "line-width": 1.6 },
        }, before);
        map.addLayer({
            id: LINE_REGIONAL,
            type: "line",
            source: SOURCE,
            filter: ["!=", ["get", "nationwide"], true],
            paint: { "line-color": RED_LINE, "line-width": 1.6, "line-dasharray": [3, 2] },
        }, before);

        map.on("click", FILL, (event) => {
            const p = event.features?.[0]?.properties;
            if (!p || !window.maplibregl) return;
            state.popup?.remove();
            const title = p.nationwide === true || p.nationwide === "true" ? "Zabrana vožnje kamiona" : "Delimična zabrana vožnje";
            const box = document.createElement("div");
            box.style.maxWidth = "260px";
            box.innerHTML = `<strong>${title}: ${p.name}</strong><div>${p.summary} (lokalno vreme)</div>`;
            if (p.info) {
                const info = document.createElement("div");
                info.style.cssText = "margin-top:6px;white-space:pre-line;font-size:12px;opacity:.85";
                info.textContent = p.info;
                box.appendChild(info);
            }
            state.popup = new window.maplibregl.Popup({ closeButton: true }).setLngLat(event.lngLat).setDOMContent(box).addTo(map);
        });
    }

    async function loadCalendar() {
        try {
            const response = await fetch(BANS_URL, { cache: "no-store" });
            if (response.ok) state.calendar = await response.json();
        } catch {
            // Offline: keep the last calendar (the app serves its cached copy).
        }
        render();
    }

    async function attach(map) {
        state.map = map;
        try {
            const response = await fetch(COUNTRIES_URL);
            state.countries = await response.json();
        } catch (error) {
            console.warn("[ProMap TruckBans] country borders not loaded", error);
            return;
        }

        const ready = () => {
            addLayers(map);
            loadCalendar();
        };
        if (map.isStyleLoaded()) ready();
        else map.once("load", ready);

        // The style can be reloaded (theme, retry): put the layers back.
        map.on("styledata", () => {
            if (!map.getSource(SOURCE) && map.isStyleLoaded()) {
                addLayers(map);
                render();
            }
        });

        setInterval(render, 60 * 1000);
        setInterval(loadCalendar, 30 * 60 * 1000);
        document.getElementById("navWeight")?.addEventListener("change", render);
    }

    window.addEventListener("promap:navigation-map-ready", (event) => {
        const map = event.detail?.map;
        if (map) attach(map);
    });

    window.ProMapTruckBans = { activeBans, render };
})();
