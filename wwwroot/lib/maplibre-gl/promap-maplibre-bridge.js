import * as maplibregl
    from "./maplibre-gl.js";

window.maplibregl =
    maplibregl;

console.info(
    "[ProMap] Local MapLibre bridge loaded:",
    {
        version:
            maplibregl.version ??
            "unknown",

        hasMap:
            typeof maplibregl.Map === "function",

        hasAddProtocol:
            typeof maplibregl.addProtocol === "function"
    }
);

export default maplibregl;