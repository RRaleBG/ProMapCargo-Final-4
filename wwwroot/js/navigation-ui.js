/* ProMap Cargo — Navigation UI layer.
   Adds UX behaviour around navigation.js without touching its internals:
   plan/drive modes, recent locations, loading + fallback + retry states,
   debug-only diagnostics and ARIA for the custom tabs. */
(() => {
    "use strict";

    const page = document.getElementById("navigationPage");
    if (!page) return;

    const $ = (id) => document.getElementById(id);
    const RECENT_KEY = "pm-nav-recent";
    const RECENT_MAX = 5;

    /* ---------- 1. Plan / drive mode (driven by the live-navigation buttons) ---------- */
    function syncMode() {
        const stop = $("stopLiveNavigation");
        const driving = Boolean(stop && !stop.hidden);
        page.dataset.navMode = driving ? "drive" : "plan";
    }

    const stopButton = $("stopLiveNavigation");
    if (stopButton) {
        new MutationObserver(syncMode).observe(stopButton, {
            attributes: true,
            attributeFilter: ["hidden"],
        });
    }
    syncMode();

    /* ---------- 2. Diagnostics: only for administrators or ?debug=1 ---------- */
    const debug = new URLSearchParams(window.location.search).get("debug") === "1";
    const diagnostics = $("navDiagnosticsCard");
    if (diagnostics && (debug || page.dataset.isAdmin === "1")) {
        diagnostics.hidden = false;
    }

    /* ---------- 3. Map tabs: ARIA ---------- */
    const tabs = ["mapRouteTab", "mapRestrictionsTab", "mapGpsTab"]
        .map($)
        .filter(Boolean);
    if (tabs.length) {
        tabs[0].parentElement?.setAttribute("role", "tablist");
        const syncTabs = () =>
            tabs.forEach((tab) =>
                tab.setAttribute("aria-selected", String(tab.classList.contains("active"))),
            );
        tabs.forEach((tab) => {
            tab.setAttribute("role", "tab");
            new MutationObserver(syncTabs).observe(tab, {
                attributes: true,
                attributeFilter: ["class"],
            });
        });
        syncTabs();
    }

    /* ---------- 4. Suggestions / inputs: ARIA ---------- */
    [
        ["navStart", "navStartSuggestions"],
        ["navEnd", "navEndSuggestions"],
    ].forEach(([inputId, listId]) => {
        const input = $(inputId);
        const list = $(listId);
        if (!input || !list) return;
        input.setAttribute("role", "combobox");
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-controls", listId);
        list.setAttribute("role", "listbox");
        const sync = () =>
            input.setAttribute("aria-expanded", String(list.childElementCount > 0));
        new MutationObserver(sync).observe(list, { childList: true });
        sync();
    });

    /* ---------- 5. Recent locations ---------- */
    function readRecent() {
        try {
            const raw = JSON.parse(localStorage.getItem(RECENT_KEY) || "[]");
            return Array.isArray(raw) ? raw.filter((v) => typeof v === "string") : [];
        } catch {
            return [];
        }
    }

    function writeRecent(list) {
        try {
            localStorage.setItem(RECENT_KEY, JSON.stringify(list.slice(0, RECENT_MAX)));
        } catch { /* storage unavailable */ }
    }

    function remember(value) {
        const text = String(value || "").trim();
        if (text.length < 3) return;
        const next = [text, ...readRecent().filter((v) => v.toLowerCase() !== text.toLowerCase())];
        writeRecent(next);
        renderRecent();
    }

    function ensureRecentHost(inputId) {
        const input = $(inputId);
        if (!input) return null;
        const id = `${inputId}Recent`;
        let host = $(id);
        if (!host) {
            host = document.createElement("div");
            host.id = id;
            host.className = "nav-recent";
            host.setAttribute("aria-label", "Nedavne lokacije");
            input.closest(".nav-input-wrapper")?.appendChild(host);
        }
        return host;
    }

    function renderRecent() {
        const items = readRecent();
        ["navStart", "navEnd"].forEach((inputId) => {
            const host = ensureRecentHost(inputId);
            if (!host) return;
            host.innerHTML = "";
            host.hidden = items.length === 0;
            items.forEach((value) => {
                const chip = document.createElement("button");
                chip.type = "button";
                chip.className = "nav-recent-chip";
                chip.textContent = value;
                chip.title = value;
                chip.addEventListener("click", () => {
                    const input = $(inputId);
                    if (!input) return;
                    input.value = value;
                    input.dispatchEvent(new Event("input", { bubbles: true }));
                    input.focus();
                });
                host.appendChild(chip);
            });
        });
    }

    renderRecent();

    // Save both fields when the user successfully requests a route.
    const calc = $("calcRoute");
    if (calc) {
        calc.addEventListener("click", () => {
            const errorBefore = $("navError");
            window.setTimeout(() => {
                if (errorBefore && !errorBefore.hidden) return; // validation failed
                remember($("navStart")?.value);
                remember($("navEnd")?.value);
            }, 1500);
        });
    }

    /* ---------- 6. Loading skeleton while routing ---------- */
    if (calc) {
        const syncRouting = () => {
            page.classList.toggle("is-routing", calc.disabled);
            calc.setAttribute("aria-busy", String(calc.disabled));
        };
        new MutationObserver(syncRouting).observe(calc, {
            attributes: true,
            attributeFilter: ["disabled"],
        });
        syncRouting();
    }

    /* ---------- 7. Error: retry button ---------- */
    const error = $("navError");
    if (error && calc) {
        const retry = document.createElement("button");
        retry.type = "button";
        retry.className = "nav-retry";
        retry.textContent = "Pokušaj ponovo";
        retry.hidden = true;
        retry.addEventListener("click", () => calc.click());
        error.insertAdjacentElement("afterend", retry);
        new MutationObserver(() => {
            retry.hidden = error.hidden;
        }).observe(error, { attributes: true, attributeFilter: ["hidden"] });
    }

    /* ---------- 8. Fallback notice (PostGIS unavailable -> OSRM) ---------- */
    const engine = $("summaryEngine");
    const summaryCard = page.querySelector(".nav-summary-card");
    if (engine && summaryCard) {
        const notice = document.createElement("div");
        notice.className = "nav-fallback-notice";
        notice.setAttribute("role", "status");
        notice.hidden = true;
        notice.textContent =
            "Lokalni PostGIS graf nije dao rutu, prikazana je ruta sa OSRM rezervnog servisa. Provera restrikcija za kamion može biti ograničena.";
        summaryCard.insertAdjacentElement("afterbegin", notice);
        const syncEngine = () => {
            notice.hidden = !/fallback|osrm/i.test(engine.textContent || "");
        };
        new MutationObserver(syncEngine).observe(engine, {
            childList: true,
            characterData: true,
            subtree: true,
        });
        syncEngine();
    }
})();
