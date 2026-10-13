(() => {
    "use strict";
    // First load of a page: the server embeds the JSON of the page's GET endpoints in
    // <script type="application/json" data-initial-url="...">, so the page renders without
    // waiting for a request. Each entry is used once; refresh, search and filters go to the API.
    const root = window.ProMapCargo = window.ProMapCargo || {};
    let store = null;

    const key = url => String(url).replace(/\?$/, "");

    function entries() {
        if (store) return store;
        store = new Map();
        document.querySelectorAll('script[type="application/json"][data-initial-url]').forEach(element => {
            store.set(key(element.dataset.initialUrl), element.textContent);
            element.remove();
        });
        return store;
    }

    function takeText(url) {
        const all = entries();
        const k = key(url);
        if (!all.has(k)) return undefined;
        const text = all.get(k);
        all.delete(k);
        return text;
    }

    root.initialData = {
        // Parsed data embedded for url, or undefined.
        take(url) {
            const text = takeText(url);
            return text === undefined ? undefined : JSON.parse(text);
        },
        // Drop-in for fetch(): a GET answered from the embedded data, otherwise a real request.
        fetch(url, options) {
            const method = String(options?.method || "GET").toUpperCase();
            const text = method === "GET" ? takeText(url) : undefined;
            if (text === undefined) return window.fetch(url, options);
            return Promise.resolve(new Response(text, { status: 200, headers: { "Content-Type": "application/json" } }));
        }
    };
})();
