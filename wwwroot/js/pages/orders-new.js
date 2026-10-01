function setActiveTab(tabName) {
    const tabs = [
        "basic",
        "cargo",
        "stops",
        "review"
    ];

    if (!tabs.includes(tabName)) {
        tabName = "basic";
    }

    state.activeTab = tabName;

    document
        .querySelectorAll("[data-order-tab]")
        .forEach(button => {

            const active =
                button.dataset.orderTab === tabName;

            button.classList.toggle(
                "vehicle-new-btn-primary",
                active
            );

            button.classList.toggle(
                "vehicle-new-btn-secondary",
                !active
            );

            button.setAttribute(
                "aria-selected",
                active ? "true" : "false"
            );

            button.tabIndex =
                active ? 0 : -1;
        });

    document
        .querySelectorAll("[data-order-panel]")
        .forEach(panel => {

            panel.hidden =
                panel.dataset.orderPanel !== tabName;
        });

    document
        .querySelectorAll("[data-flow-step]")
        .forEach(step => {

            const active =
                step.dataset.flowStep === tabName;

            step.classList.toggle(
                "active",
                active
            );
        });

    updateReview();
}