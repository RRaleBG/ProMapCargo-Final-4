(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};

    class HttpError extends Error {
        constructor(message, status, payload, response) {
            super(message);
            this.name = "HttpError";
            this.status = status;
            this.payload = payload;
            this.response = response;
        }
    }

    function problemMessage(payload, fallback) {
        if (!payload) return fallback;
        if (typeof payload === "string") return payload;
        return payload.detail || payload.title || payload.message || fallback;
    }

    async function request(url, options = {}) {
        const controller = new AbortController();
        const timeoutMs = Number(options.timeoutMs ?? 15000);
        const timeout = setTimeout(() => controller.abort(), timeoutMs);
        const externalSignal = options.signal;
        const onAbort = () => controller.abort();
        externalSignal?.addEventListener("abort", onAbort, { once: true });

        const headers = new Headers(options.headers || {});
        const hasBody = options.body !== undefined && options.body !== null;
        let body = options.body;
        if (hasBody && !(body instanceof FormData) && typeof body !== "string") {
            headers.set("Content-Type", "application/json");
            body = JSON.stringify(body);
        }
        headers.set("Accept", "application/json");

        try {
            const response = await fetch(url, {
                credentials: "same-origin",
                ...options,
                headers,
                body,
                signal: controller.signal
            });

            const contentType = response.headers.get("content-type") || "";
            let payload = null;
            if (response.status !== 204) {
                payload = contentType.includes("application/json") ? await response.json().catch(() => null) : await response.text().catch(() => null);
            }

            if (!response.ok) {
                throw new HttpError(problemMessage(payload, `HTTP ${response.status}`), response.status, payload, response);
            }
            return payload;
        } catch (error) {
            if (error?.name === "AbortError") throw new HttpError("Zahtev je istekao ili je otkazan.", 0, null, null);
            if (error instanceof HttpError) throw error;
            throw new HttpError("Mrežna greška. Proverite konekciju i pokušajte ponovo.", 0, null, null);
        } finally {
            clearTimeout(timeout);
            externalSignal?.removeEventListener("abort", onAbort);
        }
    }

    root.http = {
        HttpError,
        request,
        get(url, options) { return request(url, { ...options, method: "GET" }); },
        post(url, body, options) { return request(url, { ...options, method: "POST", body }); },
        put(url, body, options) { return request(url, { ...options, method: "PUT", body }); },
        patch(url, body, options) { return request(url, { ...options, method: "PATCH", body }); },
        delete(url, options) { return request(url, { ...options, method: "DELETE" }); }
    };
})();
