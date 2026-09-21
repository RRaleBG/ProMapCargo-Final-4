"use strict";

window.ProMap = window.ProMap || {};

window.ProMap.Gps = (() => {
    let watchId = null;

    const state = {
        enabled: false,
        position: null,
        error: null
    };

    function isSupported() {
        return (
            "geolocation" in navigator &&
            typeof navigator.geolocation.watchPosition === "function"
        );
    }

    function normalizePosition(position) {
        if (!position) {
            return null;
        }

        const coords = position.coords || position;

        const latitude = Number(coords.latitude);
        const longitude = Number(coords.longitude);

        if (
            !Number.isFinite(latitude) ||
            !Number.isFinite(longitude)
        ) {
            return null;
        }

        const accuracyValue = Number(coords.accuracy);
        const headingValue = Number(coords.heading);
        const speedValue = Number(coords.speed);

        const accuracy =
            Number.isFinite(accuracyValue)
                ? accuracyValue
                : null;

        const heading =
            Number.isFinite(headingValue)
                ? headingValue
                : null;

        const speed =
            Number.isFinite(speedValue)
                ? speedValue
                : null;

        const timestampValue =
            Number(position.timestamp);

        const timestamp =
            Number.isFinite(timestampValue)
                ? timestampValue
                : Date.now();

        return {
            latitude,
            longitude,
            accuracy,
            heading,
            speed,
            timestamp,

            coords: {
                latitude,
                longitude,
                accuracy,
                heading,
                speed
            }
        };
    }

    function start(options = {}) {
        if (!isSupported()) {
            state.enabled = false;
            state.error = new Error(
                "Browser ne podržava GPS/geolocation."
            );

            if (typeof options.onError === "function") {
                options.onError(state.error);
            }

            return false;
        }

        stop();

        state.enabled = true;
        state.error = null;

        const enableHighAccuracy =
            options.enableHighAccuracy ?? true;

        const maximumAge =
            options.maximumAge ?? 10000;

        const timeout =
            options.timeout ?? 30000;

        watchId =
            navigator.geolocation.watchPosition(
                position => {
                    const normalized =
                        normalizePosition(position);

                    if (!normalized) {
                        state.error =
                            new Error(
                                "GPS je vratio nevalidne geografske koordinate."
                            );

                        if (
                            typeof options.onError ===
                            "function"
                        ) {
                            options.onError(
                                state.error
                            );
                        }

                        return;
                    }

                    state.position =
                        normalized;

                    state.error = null;

                    if (
                        typeof options.onPosition ===
                        "function"
                    ) {
                        options.onPosition(
                            normalized
                        );
                    }
                },

                error => {
                    state.error = error;

                    /*
                     * TIMEOUT nije fatalan.
                     *
                     * watchPosition treba da ostane aktivan
                     * i browser može kasnije dobiti GPS fix.
                     */
                    if (
                        error &&
                        error.code ===
                        GeolocationPositionError.TIMEOUT
                    ) {
                        console.debug(
                            "[ProMap GPS] GPS timeout - čekam novi fix."
                        );

                        return;
                    }

                    if (
                        typeof options.onError ===
                        "function"
                    ) {
                        options.onError(error);
                    }
                },

                {
                    enableHighAccuracy,
                    maximumAge,
                    timeout
                }
            );

        return true;
    }

    function stop() {
        if (
            watchId !== null &&
            "geolocation" in navigator &&
            typeof navigator.geolocation.clearWatch ===
            "function"
        ) {
            navigator.geolocation.clearWatch(
                watchId
            );
        }

        watchId = null;
        state.enabled = false;
    }

    function getPosition() {
        return state.position;
    }

    function getState() {
        return {
            enabled: state.enabled,
            position: state.position,
            error: state.error
        };
    }

    return {
        start,
        stop,
        getPosition,
        getState,
        isSupported
    };
})();