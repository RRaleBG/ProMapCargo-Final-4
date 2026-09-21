(function () {
    "use strict";

    window.ProMap = window.ProMap || {};

    const manager =
        window.ProMap.MapLayers = {};

    const maps =
        new WeakMap();

    const tile =
        layer =>
            `/api/map/tiles/${layer}/{z}/{x}/{y}.png`;

    async function getConfig() {
        const response =
            await fetch(
                "/api/map/config",
                {
                    credentials: "same-origin",
                    headers: {
                        Accept:
                            "application/json"
                    }
                });

        if (!response.ok) {
            throw new Error(
                "Map configuration nije dostupna."
            );
        }

        return response.json();
    }

    function createBaseLayers(
        config,
        map) {
        const enabled =
            new Set(
                (config.layers || [])
                    .filter(layer => layer.enabled)
                    .map(layer => layer.id)
            );

        const baseLayers = {};

        if (enabled.has("dark")) {
            baseLayers["TomTom Dark"] =
                L.tileLayer(
                    tile("dark"),
                    {
                        maxZoom: 20,
                        attribution:
                            "© TomTom"
                    }
                );
        }

        baseLayers["OSM Light"] =
            L.tileLayer(
                "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
                {
                    maxZoom: 19,
                    attribution:
                        "© OpenStreetMap contributors"
                }
            );

        baseLayers["Satellite"] =
            L.tileLayer(
                tile("satellite"),
                {
                    maxZoom: 20,
                    attribution:
                        "Tiles © Esri"
                }
            );

        const overlays = {};

        if (enabled.has("flow")) {
            overlays["Traffic Flow"] =
                L.tileLayer(
                    tile("flow"),
                    {
                        maxZoom: 20,
                        opacity: 0.75,
                        attribution:
                            "© TomTom"
                    }
                );
        }

        if (enabled.has("incidents")) {
            overlays["Traffic Incidents"] =
                L.tileLayer(
                    tile("incidents"),
                    {
                        maxZoom: 20,
                        opacity: 0.9,
                        attribution:
                            "© TomTom"
                    }
                );
        }

        let defaultLayer =
            config.defaultBase === "dark"
                ? baseLayers["TomTom Dark"]
                : baseLayers["OSM Light"];

        if (!defaultLayer) {
            defaultLayer =
                baseLayers["OSM Light"];
        }

        if (defaultLayer) {
            defaultLayer.addTo(map);
        }

        const control =
            L.control.layers(
                baseLayers,
                overlays,
                {
                    collapsed: true,
                    position: "topright"
                }
            );

        control.addTo(map);

        return {
            baseLayers,
            overlays,
            control
        };
    }

    async function attach(map) {
        if (!map) {
            return null;
        }

        if (maps.has(map)) {
            return maps.get(map);
        }

        const config =
            await getConfig();

        const result =
            createBaseLayers(
                config,
                map);

        maps.set(
            map,
            result);

        return result;
    }

    manager.attach = attach;

    window.addEventListener(
        "load",
        () => {
            document
                .querySelectorAll(
                    ".pm-map[data-map-proxy], .pm-map[data-map-config]"
                )
                .forEach(element => {
                    if (element._leaflet_id) {
                        return;
                    }

                    const map =
                        L.map(element);

                    attach(map)
                        .catch(console.error);
                });
        });

})();