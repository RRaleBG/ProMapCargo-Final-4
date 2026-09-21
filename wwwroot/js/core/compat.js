(() => {
    "use strict";
    const CARD_SELECTORS = [
        ".pm-dashboard-card", ".pm-dispatch-card", ".pm-finance-card", ".pm-compliance-card",
        ".pm-alert-card", ".pm-monitoring-card", ".pm-audit-card", ".pm-report-card",
        ".pm-settings-card", ".pm-profile-card", ".pm-vehicle-card", ".pm-driver-card",
        ".pm-order-card", ".pm-trip-card", ".nav-control-card", ".nav-status-card",
        ".navigation-control-panel", ".navigation-map-section"
    ].join(",");

    const KPI_SELECTORS = [".pm-dashboard-kpi", ".pm-dispatch-kpi"].join(",");

    function apply() {
        document.querySelectorAll(CARD_SELECTORS).forEach(el => el.classList.add("pm-card"));
        document.querySelectorAll(KPI_SELECTORS).forEach(el => el.classList.add("pm-kpi"));

        document.querySelectorAll("button[class*='btn'],a[class*='btn']").forEach(el => {
            if (el.closest(".pm-content-inner")) el.classList.add("pm-btn");
        });
    }

    document.addEventListener("DOMContentLoaded", apply);
    window.ProMapCargo = window.ProMapCargo || {};
    window.ProMapCargo.compat = { apply };
})();
