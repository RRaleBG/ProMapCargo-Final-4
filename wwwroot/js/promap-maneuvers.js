"use strict";

window.ProMap = window.ProMap || {};

window.ProMap.Maneuvers = (() => {
    function getNumber(...values) {
        for (const value of values) {
            const number = Number(value);

            if (Number.isFinite(number)) {
                return number;
            }
        }

        return null;
    }

    function normalize(maneuver) {
        if (!maneuver) {
            return {
                type: "continue",
                modifier: null,
                icon: "↑",
                instruction: "Nastavi pravo",
                distanceMeters: null,
                durationSeconds: null,
            };
        }

        const type = maneuver.type || maneuver.Type || "continue";

        const modifier = maneuver.modifier || maneuver.Modifier || null;

        const distanceMeters = getNumber(
            maneuver.distanceMeters,
            maneuver.DistanceMeters,
            maneuver.distanceFromPreviousMeters,
            maneuver.DistanceFromPreviousMeters,
            maneuver.distance,
            maneuver.Distance,
        );

        const durationSeconds = getNumber(
            maneuver.durationSeconds,
            maneuver.DurationSeconds,
            maneuver.duration,
            maneuver.Duration,
        );

        const instruction =
            maneuver.instruction ||
            maneuver.Instruction ||
            buildInstruction(type, modifier);

        return {
            type,
            modifier,

            icon: maneuver.icon || maneuver.Icon || getIcon(type, modifier),

            instruction,

            distanceMeters,

            durationSeconds,
        };
    }

    function getIcon(type, modifier) {
        switch (String(type || "").toLowerCase()) {
            case "turn":
                if (modifier === "left") {
                    return "↰";
                }

                if (modifier === "right") {
                    return "↱";
                }

                return "↪";

            case "roundabout":
                return "⟳";

            case "uturn":
            case "u-turn":
                return "↶";

            case "merge":
                return "⇢";

            case "fork":
                return "⑂";

            case "arrive":
            case "arrival":
                return "●";

            case "depart":
            case "departure":
                return "↑";

            case "continue":
                return "↑";

            default:
                return "↑";
        }
    }

    function buildInstruction(type, modifier) {
        const normalized = String(type || "").toLowerCase();

        if (normalized === "turn") {
            if (modifier === "left") {
                return "Skreni levo";
            }

            if (modifier === "right") {
                return "Skreni desno";
            }

            return "Skretanje";
        }

        if (normalized === "roundabout") {
            return "Uđi u kružni tok";
        }

        if (normalized === "uturn" || normalized === "u-turn") {
            return "Polukružno okretanje";
        }

        if (normalized === "merge") {
            return "Uključi se";
        }

        if (normalized === "fork") {
            if (modifier === "left") {
                return "Drži levo";
            }

            if (modifier === "right") {
                return "Drži desno";
            }

            return "Račvanje puta";
        }

        if (normalized === "arrive" || normalized === "arrival") {
            return "Stigli ste na odredište";
        }

        if (normalized === "depart" || normalized === "departure") {
            return "Polazak";
        }

        return "Nastavi pravo";
    }

    function formatDistance(meters) {
        const value = Number(meters);

        if (!Number.isFinite(value)) {
            return "—";
        }

        if (value < 1000) {
            return `${Math.round(value)} m`;
        }

        return `${(value / 1000).toFixed(1)} km`;
    }

    function formatDuration(seconds) {
        const value = Number(seconds);

        if (!Number.isFinite(value)) {
            return "—";
        }

        const minutes = Math.max(0, Math.round(value / 60));

        if (minutes < 60) {
            return `${minutes} min`;
        }

        const hours = Math.floor(minutes / 60);

        const remainder = minutes % 60;

        return `${hours} h ${remainder} min`;
    }

    return {
        normalize,
        getIcon,
        buildInstruction,
        formatDistance,
        formatDuration,
    };
})();
