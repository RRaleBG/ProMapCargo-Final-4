/* ============================================================
 * ProMap Cargo
 * Runtime Map Icons v4
 *
 * File:
 *   wwwroot/js/promap-map-icons.js
 *
 * IMPORTANT:
 * This module ONLY registers runtime images.
 * It does NOT create MapLibre layers.
 * Layers are defined by promap-dark.json.
 * ============================================================ */

(() => {
    "use strict";

    const VERSION = "4.0.0";

    const ICON_IDS = [
        // ----------------------------------------------------
        // ROAD SHIELDS
        // ----------------------------------------------------
        "promap-shield-motorway",
        "promap-shield-trunk",
        "promap-shield-primary",
        "promap-shield-secondary",
        "promap-shield-tertiary",

        // ----------------------------------------------------
        // EXISTING PRO MAP CARGO ICONS
        // ----------------------------------------------------
        "promap-fuel",
        "promap-charging",
        "promap-parking",
        "promap-truck-service",
        "promap-hospital",
        "promap-police",
        "promap-airport",

        // ----------------------------------------------------
        // POI v4
        // ----------------------------------------------------
        "promap-railway",
        "promap-station",
        "promap-mall",
        "promap-university",
        "promap-attraction",
        "promap-sports",
        "promap-park",
        "promap-castle",
        "promap-cemetery",
        "promap-exhibition",
        "promap-events"
    ];

    const state = {
        map: null,
        initialized: false,
        debug: false,
        loaded: new Set(),
        pending: new Map()
    };

    function log(...args) {
        if (state.debug) {
            console.info("[ProMap Map Icons]", ...args);
        }
    }

    function warn(...args) {
        console.warn("[ProMap Map Icons]", ...args);
    }

    // --------------------------------------------------------
    // SVG helpers
    // --------------------------------------------------------

    function esc(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&apos;");
    }

    function svgWrap(content, width = 64, height = 64) {
        return `
<svg xmlns="http://www.w3.org/2000/svg"
     width="${width}"
     height="${height}"
     viewBox="0 0 ${width} ${height}">
${content}
</svg>`;
    }

    function shieldSvg(fill, textColor) {
        return svgWrap(`
<path d="M8 7
         Q32 2 56 7
         L53 39
         Q32 58 11 39
         Z"
      fill="${fill}"
      stroke="#061b17"
      stroke-width="3"/>

<path d="M13 11
         Q32 7 51 11
         L49 36
         Q32 50 15 36
         Z"
      fill="none"
      stroke="#ffffff"
      stroke-opacity=".35"
      stroke-width="1.5"/>

<text x="32"
      y="34"
      text-anchor="middle"
      font-family="Arial, sans-serif"
      font-size="18"
      font-weight="700"
      fill="${textColor}">
    {TEXT}
</text>`.replace("{TEXT}", ""));
    }

    function shieldSvgWithText(fill, text) {
        return svgWrap(`
<path d="M8 7
         Q32 2 56 7
         L53 39
         Q32 58 11 39
         Z"
      fill="${fill}"
      stroke="#061b17"
      stroke-width="3"/>

<path d="M13 11
         Q32 7 51 11
         L49 36
         Q32 50 15 36
         Z"
      fill="none"
      stroke="#ffffff"
      stroke-opacity=".35"
      stroke-width="1.5"/>

<text x="32"
      y="35"
      text-anchor="middle"
      font-family="Arial, sans-serif"
      font-size="17"
      font-weight="700"
      fill="#ffffff">${esc(text)}</text>
`);
    }

    function pinSvg(symbol, background = "#10b981") {
        return svgWrap(`
<path d="M32 5
         C18 5 9 15 9 28
         C9 45 32 59 32 59
         C32 59 55 45 55 28
         C55 15 46 5 32 5 Z"
      fill="${background}"
      stroke="#061b17"
      stroke-width="3"/>

<circle cx="32" cy="28" r="16"
        fill="#061b17"
        fill-opacity=".92"/>

<text x="32"
      y="34"
      text-anchor="middle"
      font-family="Arial, sans-serif"
      font-size="18"
      font-weight="700"
      fill="#ffffff">${esc(symbol)}</text>
`);
    }

    function circleSvg(symbol, background = "#10b981") {
        return svgWrap(`
<circle cx="32" cy="32" r="25"
        fill="${background}"
        stroke="#061b17"
        stroke-width="3"/>

<text x="32"
      y="39"
      text-anchor="middle"
      font-family="Arial, sans-serif"
      font-size="22"
      font-weight="700"
      fill="#ffffff">${esc(symbol)}</text>
`);
    }

    // --------------------------------------------------------
    // Icon SVG definitions
    // --------------------------------------------------------

    function createSvg(id) {
        switch (id) {

            // ------------------------------------------------
            // ROAD SHIELDS
            // ------------------------------------------------

            case "promap-shield-motorway":
                return shieldSvgWithText("#166534", "M");

            case "promap-shield-trunk":
                return shieldSvgWithText("#0f766e", "T");

            case "promap-shield-primary":
                return shieldSvgWithText("#b45309", "P");

            case "promap-shield-secondary":
                return shieldSvgWithText("#64748b", "S");

            case "promap-shield-tertiary":
                return shieldSvgWithText("#475569", "3");

            // ------------------------------------------------
            // EXISTING ICONS
            // ------------------------------------------------

            case "promap-fuel":
                return pinSvg("F", "#059669");

            case "promap-charging":
                return pinSvg("⚡", "#0d9488");

            case "promap-parking":
                return pinSvg("P", "#2563eb");

            case "promap-truck-service":
                return pinSvg("T", "#d97706");

            case "promap-hospital":
                return pinSvg("+", "#dc2626");

            case "promap-police":
                return pinSvg("★", "#1d4ed8");

            case "promap-airport":
                return pinSvg("✈", "#7c3aed");

            // ------------------------------------------------
            // POI v4
            // ------------------------------------------------

            case "promap-railway":
                return pinSvg("R", "#475569");

            case "promap-station":
                return pinSvg("S", "#334155");

            case "promap-mall":
                return pinSvg("M", "#be185d");

            case "promap-university":
                return pinSvg("U", "#7c3aed");

            case "promap-attraction":
                return pinSvg("★", "#d97706");

            case "promap-sports":
                return pinSvg("⚽", "#059669");

            case "promap-park":
                return pinSvg("◆", "#15803d");

            case "promap-castle":
                return pinSvg("C", "#92400e");

            case "promap-cemetery":
                return pinSvg("†", "#64748b");

            case "promap-exhibition":
                return pinSvg("E", "#0891b2");

            case "promap-events":
                return pinSvg("★", "#c026d3");

            default:
                return null;
        }
    }

    // --------------------------------------------------------
    // SVG -> ImageData
    // --------------------------------------------------------

    async function svgToImageData(svg) {
        const blob = new Blob([svg], {
            type: "image/svg+xml;charset=utf-8"
        });

        const url = URL.createObjectURL(blob);

        try {
            const image = await new Promise((resolve, reject) => {
                const img = new Image();

                img.onload = () => resolve(img);
                img.onerror = reject;

                img.src = url;
            });

            const canvas = document.createElement("canvas");

            canvas.width = 128;
            canvas.height = 128;

            const ctx = canvas.getContext("2d", {
                alpha: true,
                willReadFrequently: false
            });

            if (!ctx) {
                throw new Error("Canvas 2D context unavailable.");
            }

            ctx.clearRect(0, 0, 128, 128);
            ctx.drawImage(image, 0, 0, 128, 128);

            return ctx.getImageData(0, 0, 128, 128);

        } finally {
            URL.revokeObjectURL(url);
        }
    }

    // --------------------------------------------------------
    // Register one icon
    // --------------------------------------------------------

    async function registerIcon(map, id) {
        if (!map) {
            throw new Error("MapLibre map instance is missing.");
        }

        if (map.hasImage(id)) {
            state.loaded.add(id);
            return true;
        }

        if (state.pending.has(id)) {
            return state.pending.get(id);
        }

        const promise = (async () => {
            const svg = createSvg(id);

            if (!svg) {
                throw new Error(`No SVG definition for ${id}`);
            }

            const imageData = await svgToImageData(svg);

            if (!map.hasImage(id)) {
                map.addImage(
                    id,
                    imageData,
                    {
                        pixelRatio: 2,
                        sdf: false
                    }
                );
            }

            state.loaded.add(id);

            log("Registered:", id);

            return true;
        })();

        state.pending.set(id, promise);

        try {
            return await promise;
        } finally {
            state.pending.delete(id);
        }
    }

    // --------------------------------------------------------
    // Register all icons
    // --------------------------------------------------------

    async function registerAll(map) {
        for (const id of ICON_IDS) {
            try {
                await registerIcon(map, id);
            } catch (error) {
                warn(`Failed to register ${id}:`, error);
            }
        }

        return verifyIcons();
    }

    // --------------------------------------------------------
    // Verify
    // --------------------------------------------------------

    function verifyIcons() {
        if (!state.map) {
            return {
                ok: false,
                total: ICON_IDS.length,
                loaded: 0,
                missing: [...ICON_IDS]
            };
        }

        const missing = ICON_IDS.filter(
            id => !state.map.hasImage(id)
        );

        const loaded = ICON_IDS.length - missing.length;

        return {
            ok: missing.length === 0,
            total: ICON_IDS.length,
            loaded,
            missing
        };
    }

    // --------------------------------------------------------
    // styleimagemissing
    // --------------------------------------------------------

    function installMissingImageHandler(map) {
        if (map.__promapMapIconsMissingHandlerInstalled) {
            return;
        }

        map.__promapMapIconsMissingHandlerInstalled = true;

        map.on("styleimagemissing", async event => {
            const id = event.id;

            if (!ICON_IDS.includes(id)) {
                return;
            }

            try {
                await registerIcon(map, id);
                log("Recovered missing style image:", id);
            } catch (error) {
                warn(`Could not recover missing image ${id}:`, error);
            }
        });
    }

    // --------------------------------------------------------
    // styledata handler
    // --------------------------------------------------------

    function installStyleHandler(map) {
        if (map.__promapMapIconsStyleHandlerInstalled) {
            return;
        }

        map.__promapMapIconsStyleHandlerInstalled = true;

        map.on("styledata", () => {
            if (!map.isStyleLoaded()) {
                return;
            }

            registerAll(map).catch(error => {
                warn("styledata registration failed:", error);
            });
        });
    }

    // --------------------------------------------------------
    // INIT
    // --------------------------------------------------------

    async function init(map, options = {}) {
        if (!map) {
            warn("init() called without MapLibre map.");
            return {
                ok: false,
                error: "MapLibre map instance missing."
            };
        }

        state.map = map;
        state.debug = options.debug === true;

        installMissingImageHandler(map);
        installStyleHandler(map);

        if (map.isStyleLoaded()) {
            await registerAll(map);
        } else {
            await new Promise(resolve => {
                const handler = () => {
                    if (!map.isStyleLoaded()) {
                        return;
                    }

                    map.off("styledata", handler);
                    resolve();
                };

                map.on("styledata", handler);
            });

            await registerAll(map);
        }

        state.initialized = true;

        const result = verifyIcons();

        log(
            `Initialized v${VERSION}:`,
            `${result.loaded}/${result.total} icons`
        );

        if (!result.ok) {
            warn("Missing icons:", result.missing);
        }

        return result;
    }

    // --------------------------------------------------------
    // Public API
    // --------------------------------------------------------

    window.ProMapMapIcons = {
        version: VERSION,

        ids: [...ICON_IDS],

        init,

        verifyIcons,

        register: async id => {
            if (!state.map) {
                throw new Error("Map icons are not initialized.");
            }

            if (!ICON_IDS.includes(id)) {
                throw new Error(`Unknown ProMap icon: ${id}`);
            }

            return registerIcon(state.map, id);
        },

        registerAll: async () => {
            if (!state.map) {
                throw new Error("Map icons are not initialized.");
            }

            return registerAll(state.map);
        }
    };

    // Legacy compatibility
    window.initProMapMapIcons = init;

    console.info(
        `[ProMap Map Icons] Runtime registry v${VERSION} loaded — ${ICON_IDS.length} icons.`
    );
})();