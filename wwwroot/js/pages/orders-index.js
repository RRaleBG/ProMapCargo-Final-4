(() => {
            "use strict";

            const rows = Array.from(document.querySelectorAll(".pm-order-row"));
            const searchInput = document.getElementById("orderSearch");
            const customerSelect = document.getElementById("orderCustomer");
            const pickupSelect = document.getElementById("orderPickup");
            const statusSelect = document.getElementById("orderStatus");
            const dateSelect = document.getElementById("orderDate");
            const visibleCount = document.getElementById("visibleOrderCount");
            const emptyState = document.getElementById("ordersEmpty");
            const detailOrderId = document.getElementById("detailOrderId");
            const detailCustomer = document.getElementById("detailCustomer");
            let selectedStatus = "all";

            function normalize(value) {
                return String(value || "")
                    .trim()
                    .toLowerCase();
            }

            function applyFilters() {

                const search =
                    normalize(searchInput?.value);

                const customer =
                    normalize(customerSelect?.value);

                const pickup =
                    normalize(pickupSelect?.value);

                const status =
                    normalize(statusSelect?.value);

                let visible = 0;

                rows.forEach(row => {

                    const rowText =
                        normalize(row.textContent);

                    const rowCustomer =
                        normalize(row.dataset.customer);

                    const rowStatus =
                        normalize(row.dataset.status);

                    const searchMatch =
                        !search ||
                        rowText.includes(search);

                    const customerMatch =
                        !customer ||
                        rowCustomer === customer;

                    const statusMatch =
                        !status ||
                        rowStatus === status;

                    const chipStatusMatch =
                        selectedStatus === "all" ||
                        rowStatus === selectedStatus;

                    const pickupMatch =
                        !pickup ||
                        rowText.includes(pickup);

                    const show =
                        searchMatch &&
                        customerMatch &&
                        statusMatch &&
                        chipStatusMatch &&
                        pickupMatch;

                    row.style.display =
                        show ? "" : "none";

                    if (show) {
                        visible++;
                    }
                });

                if (visibleCount) {
                    visibleCount.textContent =
                        String(visible);
                }

                if (emptyState) {
                    emptyState.style.display =
                        visible === 0
                            ? "block"
                            : "none";
                }
            }

            function selectRow(row) {

                rows.forEach(item => {
                    item.classList.remove("selected");
                });

                row.classList.add("selected");

                const id =
                    row.dataset.orderId ||
                    "Transportni nalog";

                const customer =
                    row.dataset.customer ||
                    "";

                if (detailOrderId) {
                    detailOrderId.textContent = id;
                }

                if (detailCustomer) {
                    detailCustomer.textContent =
                        customer +
                        " · detalji naloga";
                }
            }

            function bindRows() {

                rows.forEach(row => {

                    row.addEventListener(
                        "click",
                        event => {

                            if (
                                event.target.closest(
                                    ".pm-orders-action"
                                )
                            ) {
                                return;
                            }

                            selectRow(row);

                        }
                    );

                });

            }

            function bindStatusChips() {

                document
                    .querySelectorAll(
                        ".pm-orders-status-chip"
                    )
                    .forEach(chip => {

                        chip.addEventListener(
                            "click",
                            () => {

                                document
                                    .querySelectorAll(
                                        ".pm-orders-status-chip"
                                    )
                                    .forEach(item => {
                                        item.classList.remove(
                                            "active"
                                        );
                                    });

                                chip.classList.add("active");

                                selectedStatus =
                                    normalize(
                                        chip.dataset.status
                                    ) || "all";

                                applyFilters();

                            }
                        );

                    });
            }

            function resetFilters() {

                if (searchInput) {
                    searchInput.value = "";
                }

                if (customerSelect) {
                    customerSelect.value = "";
                }

                if (pickupSelect) {
                    pickupSelect.value = "";
                }

                if (statusSelect) {
                    statusSelect.value = "";
                }

                if (dateSelect) {
                    dateSelect.value = "";
                }

                selectedStatus = "all";

                document
                    .querySelectorAll(
                        ".pm-orders-status-chip"
                    )
                    .forEach(chip => {

                        chip.classList.toggle(
                            "active",
                            chip.dataset.status === "all"
                        );

                    });

                applyFilters();
            }

            function refreshOrders() {

                const button =
                    document.getElementById(
                        "ordersRefresh"
                    );

                if (!button) {
                    return;
                }

                const original =
                    button.innerHTML;

                button.disabled = true;

                button.innerHTML =
                    '<span class="pm-orders-btn-icon">↻</span> Osvežavanje…';

                window.setTimeout(() => {

                    button.disabled = false;
                    button.innerHTML = original;

                }, 700);
            }

            function newOrder() {

                window.alert(
                    "Forma za novi transportni nalog je spremna za povezivanje sa Orders API-jem."
                );
            }

            function editOrder() {

                window.alert(
                    "Edit forma za izabrani nalog je spremna za povezivanje sa Orders API-jem."
                );
            }

            function moreOrderActions() {

                window.alert(
                    "Dodatne akcije naloga biće povezane sa Orders API-jem."
                );
            }

            function bindEvents() {

                searchInput?.addEventListener(
                    "input",
                    applyFilters
                );

                customerSelect?.addEventListener(
                    "change",
                    applyFilters
                );

                pickupSelect?.addEventListener(
                    "change",
                    applyFilters
                );

                statusSelect?.addEventListener(
                    "change",
                    applyFilters
                );

                dateSelect?.addEventListener(
                    "change",
                    applyFilters
                );

                document
                    .getElementById("applyFilters")
                    ?.addEventListener(
                        "click",
                        applyFilters
                    );

                document
                    .getElementById("resetFilters")
                    ?.addEventListener(
                        "click",
                        resetFilters
                    );

                document
                    .getElementById("ordersRefresh")
                    ?.addEventListener(
                        "click",
                        refreshOrders
                    );

                document
                    .getElementById("newOrderButton")
                    ?.addEventListener(
                        "click",
                        newOrder
                    );

                document
                    .getElementById("editOrderButton")
                    ?.addEventListener(
                        "click",
                        editOrder
                    );

                document
                    .getElementById("orderMoreButton")
                    ?.addEventListener(
                        "click",
                        moreOrderActions
                    );

                document
                    .querySelectorAll(
                        ".pm-orders-action"
                    )
                    .forEach(button => {

                        button.addEventListener(
                            "click",
                            event => {

                                event.stopPropagation();

                                const row =
                                    button.closest(
                                        ".pm-order-row"
                                    );

                                if (row) {
                                    selectRow(row);
                                }

                            }
                        );

                    });

            }

            function initialize() {

                bindRows();
                bindStatusChips();
                bindEvents();
                applyFilters();

            }

            if (
                document.readyState ===
                "loading"
            ) {

                document.addEventListener("DOMContentLoaded",
                    initialize,
                    {
                        once: true
                    }
                );
            } 
            else {

                initialize();

            }

        })();
