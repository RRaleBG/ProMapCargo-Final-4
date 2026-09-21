(function (window) {
    "use strict";

    window.ProMap = window.ProMap || {};

    window.ProMap.Pmtiles = {
        register: function (maplibregl) {
            if (window.__promapPmtilesProtocolRegistered) {
                return;
            }

            if (!window.pmtiles || typeof window.pmtiles.Protocol !== "function") {
                throw new Error("PMTiles library nije učitana.");
            }

            if (!maplibregl || typeof maplibregl.addProtocol !== "function") {
                throw new Error("MapLibre addProtocol() nije dostupan.");
            }

            const protocol = new window.pmtiles.Protocol();

            maplibregl.addProtocol("pmtiles", protocol.tile);

            window.__promapPmtilesProtocol = protocol;

            window.__promapPmtilesProtocolRegistered = true;
        },
    };
})(window);
