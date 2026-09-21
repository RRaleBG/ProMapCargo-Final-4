(() => {
    "use strict";
    const root = (window.ProMapCargo = window.ProMapCargo || {});
    const selectors = [
        "[data-alert-count]",
        ".pm-notification-badge",
        ".pm-nav-count.danger",
    ];
    function updateBadges(count) {
        const value = Math.max(0, Number(count || 0));
        selectors.forEach((selector) =>
            document.querySelectorAll(selector).forEach((el) => {
                el.textContent = String(value);
                el.hidden = value === 0;
                el.setAttribute("aria-label", `${value} aktivnih upozorenja`);
            }),
        );
    }
    async function refresh() {
        if (!root.http) return null;
        const endpoints = ["/api/alerts/count", "/api/alerts?summary=true"];
        for (const endpoint of endpoints) {
            try {
                const data = await root.http.get(endpoint, { timeoutMs: 5000 });
                const count =
                    typeof data === "number"
                        ? data
                        : Number(data?.count ?? data?.total ?? data?.active ?? NaN);
                if (Number.isFinite(count)) {
                    updateBadges(count);
                    return count;
                }
            } catch { }
        }
        return null;
    }
    root.alerts = { updateBadges, refresh };
})();
