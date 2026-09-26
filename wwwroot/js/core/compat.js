(() => {
    "use strict";
    const CARD_SELECTORS = [
        ".pm-dashboard-card", ".pm-dispatch-card", ".pm-finance-card", ".pm-compliance-card",
        ".pm-alert-card", ".pm-alerts-card", ".pm-monitoring-card", ".monitoring-side-card",
        ".pm-audit-card", ".pm-report-card", ".pm-settings-card", ".pm-profile-card",
        ".pm-vehicle-card", ".pm-vehicles-card", ".pm-driver-card", ".pm-drivers-card",
        ".pm-order-card", ".pm-orders-filter-card", ".pm-orders-detail-card", ".pm-trip-card",
        ".finance-card", ".finance-physical-card", ".finance-balance-card", ".finance-assigned-card",
        ".nav-control-card", ".nav-status-card"
    ].join(",");

    const KPI_SELECTORS = [
        ".pm-dashboard-kpi", ".pm-dispatch-kpi", ".pm-alert-kpi",
        ".pm-driver-kpi", ".pm-orders-kpi", ".pm-vehicle-kpi",
        ".finance-kpi", ".compliance-kpi", ".monitoring-kpi", ".nav-kpi-item", ".nav-kpi-card"
    ].join(",");

    const PAGE_HEADER_SELECTORS = [
        ".pm-dispatch-header", ".pm-alerts-header", ".compliance-header",
        ".finance-page-header", ".monitoring-header", ".nav-page-header",
        ".pm-profile-header", ".pm-header", ".pm-page-header"
    ].join(",");

    function apply() {
        document.querySelectorAll(".pm-content-inner [data-module]").forEach(page => {
            page.classList.add("pm-page");
            page.classList.add("pm-page--" + (page.dataset.module || "workspace"));
            page.querySelectorAll(PAGE_HEADER_SELECTORS).forEach(header => {
                header.classList.add("pm-page-header", "pm-header");
            });
            page.querySelectorAll(":scope > header").forEach(header => {
                header.classList.add("pm-page-header", "pm-header");
            });

            page.querySelectorAll("[class]").forEach(element => {
                const names = Array.from(element.classList);
                if (names.some(name => /(^|-)kpi$/.test(name) && !name.endsWith("-icon"))) {
                    element.classList.add("pm-kpi");
                }
            });
        });

        document.querySelectorAll(CARD_SELECTORS).forEach(el => el.classList.add("pm-card"));
        document.querySelectorAll(KPI_SELECTORS).forEach(el => el.classList.add("pm-kpi"));
        document.querySelectorAll(".finance-reveal").forEach(el => el.classList.add("pm-reveal"));

        document.querySelectorAll("button[class*='btn'],a[class*='btn']").forEach(el => {
            if (el.closest(".pm-content-inner")) el.classList.add("pm-btn");
        });
    }

    document.addEventListener("DOMContentLoaded", apply);
    window.ProMapCargo = window.ProMapCargo || {};
    window.ProMapCargo.compat = { apply };
})();
