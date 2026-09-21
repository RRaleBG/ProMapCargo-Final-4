"use strict";

window.ProMap = window.ProMap || {};

window.ProMap.Routing = (() => {
    const DEFAULT_ENDPOINT = "/api/routing/route";

    function numberOrNull(value) {
        const number = Number(value);

        return Number.isFinite(number) ? number : null;
    }

    function normalizeCoordinate(point) {
        if (!point) {
            return null;
        }

        const latitude = Number(point.latitude ?? point.lat ?? point.Lat);

        const longitude = Number(point.longitude ?? point.lon ?? point.Lon);

        return {
            latitude,
            longitude,
        };
    }

    function validateCoordinate(point) {
        if (!point) {
            return false;
        }

        return (
            Number.isFinite(point.latitude) &&
            Number.isFinite(point.longitude) &&
            point.latitude >= -90 &&
            point.latitude <= 90 &&
            point.longitude >= -180 &&
            point.longitude <= 180
        );
    }

    function buildRequest(state = {}) {
        const start = normalizeCoordinate(state.start);

        const destination = normalizeCoordinate(state.destination ?? state.end);

        if (!validateCoordinate(start)) {
            throw new Error(
                "Start i destination moraju sadržati validne geografske koordinate.",
            );
        }

        if (!validateCoordinate(destination)) {
            throw new Error(
                "Start i destination moraju sadržati validne geografske koordinate.",
            );
        }

        const profile = state.profile === "car" ? "car" : "truck";

        const request = {
            start,
            destination,
            profile,

            avoidRestricted: state.avoidRestricted !== false,

            departureAt: state.departureAt || new Date().toISOString(),
        };

        if (profile === "truck") {
            const truck = state.truck || state.vehicle || {};

            request.truck = {
                grossWeightT: numberOrNull(
                    truck.grossWeightT ??
                    truck.grossWeightTons ??
                    truck.weightTons ??
                    truck.weight,
                ),

                heightM: numberOrNull(
                    truck.heightM ?? truck.heightMeters ?? truck.height,
                ),

                widthM: numberOrNull(truck.widthM ?? truck.widthMeters ?? truck.width),

                lengthM: numberOrNull(
                    truck.lengthM ?? truck.lengthMeters ?? truck.length,
                ),

                axleLoadT: numberOrNull(truck.axleLoadT ?? truck.axleLoadTons),

                axles: Number.isFinite(Number(truck.axles))
                    ? Math.round(Number(truck.axles))
                    : 5,

                isHgv: truck.isHgv !== false,

                commercial: truck.commercial !== false,

                hazmat: Boolean(truck.hazmat),

                goods: truck.goods || null,

                adrClass: truck.adrClass || null,

                vehicleClass: truck.vehicleClass || "HeavyGoods",

                maxSpeedKmh: numberOrNull(truck.maxSpeedKmh ?? truck.maxSpeed),
            };
        }

        return request;
    }

    async function calculate(state = {}, options = {}) {
        const endpoint = options.endpoint || DEFAULT_ENDPOINT;

        const request = buildRequest(state);

        const response = await fetch(endpoint, {
            method: "POST",

            headers: {
                "Content-Type": "application/json",

                Accept: "application/json",
            },

            body: JSON.stringify(request),

            signal: options.signal,
        });

        let payload = null;

        const contentType = response.headers.get("content-type") || "";

        if (contentType.includes("application/json")) {
            try {
                payload = await response.json();
            } catch {
                payload = null;
            }
        } else {
            const text = await response.text();

            if (text) {
                try {
                    payload = JSON.parse(text);
                } catch {
                    payload = null;
                }
            }
        }

        if (!response.ok) {
            const error = new Error(
                payload?.message ||
                payload?.detail ||
                `Routing API HTTP ${response.status}`,
            );

            error.code = payload?.code || `HTTP_${response.status}`;

            error.status = response.status;

            error.payload = payload;

            throw error;
        }

        if (!payload) {
            throw new Error("Routing API je vratio prazan odgovor.");
        }

        if (payload.success === false) {
            const error = new Error(
                payload.message || "Routing engine nije uspeo da izračuna rutu.",
            );

            error.code = payload.code || "ROUTING_FAILED";

            error.payload = payload;

            throw error;
        }

        if (payload.code && String(payload.code).toLowerCase() !== "ok") {
            const error = new Error(
                payload.message || "Routing engine nije pronašao rutu.",
            );

            error.code = payload.code;

            error.payload = payload;

            throw error;
        }

        if (!Array.isArray(payload.routes) || payload.routes.length === 0) {
            throw new Error(
                payload.message || "Routing engine nije vratio nijednu rutu.",
            );
        }

        return payload;
    }

    return {
        calculate,
        buildRequest,
        normalizeCoordinate,
        validateCoordinate,
    };
})();
