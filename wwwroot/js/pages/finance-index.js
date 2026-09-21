(() => {

    "use strict";

    const page = document.querySelector(".pm-finance-page");

    if (!page) {
        return;
    }

    /* =========================================================
       ELEMENTS
    ========================================================== */

    const modal =
        document.getElementById("expenseModal");

    const openExpenseButton =
        document.getElementById("openExpenseModal");

    const closeExpenseButtons =
        document.querySelectorAll("[data-close-expense-modal]");

    const expenseForm =
        document.getElementById("expenseForm");

    const expenseCategory =
        document.getElementById("expenseCategory");

    const expensePaymentMethod =
        document.getElementById("expensePaymentMethod");

    const expenseCardField =
        document.getElementById("expenseCardField");

    const fuelFields =
        document.getElementById("fuelSpecificFields");

    const searchInput =
        document.getElementById("financeSearch");

    const categoryFilter =
        document.getElementById("financeCategoryFilter");

    const statusFilter =
        document.getElementById("financeStatusFilter");

    const table =
        document.getElementById("financeTransactionTable");

    const emptyState =
        document.getElementById("financeTableEmpty");

    const modeButtons =
        document.querySelectorAll("[data-finance-mode]");

    const contextTitle =
        document.getElementById("financeContextTitle");

    const refreshButton =
        document.getElementById("financeRefreshButton");

    const exportButton =
        document.getElementById("exportFinanceButton");

    const toast =
        document.getElementById("financeToast");

    const toastTitle =
        document.getElementById("financeToastTitle");

    const toastText =
        document.getElementById("financeToastText");


    /* =========================================================
       NUMBER ANIMATION
    ========================================================== */

    const currencyFormatter =
        new Intl.NumberFormat("sr-RS", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });

    function animateFinanceValues() {

        document
            .querySelectorAll("[data-finance-value]")
            .forEach(element => {

                const target =
                    Number(element.dataset.financeValue || 0);

                const duration = 900;
                const started = performance.now();

                const frame = now => {

                    const progress =
                        Math.min((now - started) / duration, 1);

                    const eased =
                        1 - Math.pow(1 - progress, 3);

                    const current =
                        target * eased;

                    element.textContent =
                        `${currencyFormatter.format(current)} €`;

                    if (progress < 1) {
                        requestAnimationFrame(frame);
                    }

                };

                requestAnimationFrame(frame);

            });

    }


    /* =========================================================
       PROGRESS
    ========================================================== */

    function animateProgressBars() {

        requestAnimationFrame(() => {

            document
                .querySelectorAll(".finance-progress-bar")
                .forEach(bar => {

                    const width =
                        Number(bar.dataset.width || 0);

                    setTimeout(() => {
                        bar.style.width = `${width}%`;
                    }, 180);

                });

        });

    }


    /* =========================================================
       MODAL
    ========================================================== */

    function openExpenseModal() {

        modal.classList.add("open");

        modal.setAttribute(
            "aria-hidden",
            "false"
        );

        document.body.style.overflow =
            "hidden";

        updateExpenseFields();

    }

    function closeExpenseModal() {

        modal.classList.remove("open");

        modal.setAttribute(
            "aria-hidden",
            "true"
        );

        document.body.style.overflow =
            "";

    }

    openExpenseButton?.addEventListener(
        "click",
        openExpenseModal
    );

    closeExpenseButtons.forEach(button => {

        button.addEventListener(
            "click",
            closeExpenseModal
        );

    });

    document.addEventListener(
        "keydown",
        event => {

            if (
                event.key === "Escape" &&
                modal.classList.contains("open")
            ) {
                closeExpenseModal();
            }

        }
    );


    /* =========================================================
       EXPENSE FORM CONDITIONAL FIELDS
    ========================================================== */

    function updateExpenseFields() {

        const isFuel =
            expenseCategory.value === "fuel";

        fuelFields.style.display =
            isFuel ? "block" : "none";

        const requiresCard =
            expensePaymentMethod.value !== "cash";

        expenseCardField.style.display =
            requiresCard ? "block" : "none";

    }

    expenseCategory?.addEventListener(
        "change",
        updateExpenseFields
    );

    expensePaymentMethod?.addEventListener(
        "change",
        updateExpenseFields
    );


    /* =========================================================
       SAVE EXPENSE - UI ONLY
    ========================================================== */

    expenseForm?.addEventListener(
        "submit",
        event => {

            event.preventDefault();

            const saveButton =
                expenseForm.querySelector(
                    'button[type="submit"]'
                );

            const original =
                saveButton.innerHTML;

            saveButton.disabled = true;

            saveButton.innerHTML =
                "Čuvanje...";

            setTimeout(() => {

                saveButton.disabled = false;
                saveButton.innerHTML = original;

                closeExpenseModal();

                showToast(
                    "Trošak evidentiran",
                    "UI unos je uspešno dodat. API povezivanje radimo kasnije."
                );

                expenseForm.reset();

                updateExpenseFields();

            }, 650);

        }
    );


    /* =========================================================
       FILTER TABLE
    ========================================================== */

    function filterTransactions() {

        const query =
            (searchInput.value || "")
                .trim()
                .toLowerCase();

        const category =
            categoryFilter.value;

        const status =
            statusFilter.value;

        const rows =
            [...table.querySelectorAll("tr")];

        let visible = 0;

        rows.forEach(row => {

            const searchable =
                (
                    row.dataset.search ||
                    row.textContent ||
                    ""
                ).toLowerCase();

            const rowCategory =
                row.dataset.category;

            const rowStatus =
                row.dataset.status;

            const matchesSearch =
                !query ||
                searchable.includes(query);

            const matchesCategory =
                category === "all" ||
                rowCategory === category;

            const matchesStatus =
                status === "all" ||
                rowStatus === status;

            const shouldShow =
                matchesSearch &&
                matchesCategory &&
                matchesStatus;

            row.style.display =
                shouldShow ? "" : "none";

            if (shouldShow) {
                visible++;
            }

        });

        emptyState.style.display =
            visible === 0
                ? "block"
                : "none";

    }

    searchInput?.addEventListener(
        "input",
        filterTransactions
    );

    categoryFilter?.addEventListener(
        "change",
        filterTransactions
    );

    statusFilter?.addEventListener(
        "change",
        filterTransactions
    );


    /* =========================================================
       MODE SWITCH
    ========================================================== */

    modeButtons.forEach(button => {

        button.addEventListener(
            "click",
            () => {

                modeButtons.forEach(item =>
                    item.classList.remove("active")
                );

                button.classList.add("active");

                const mode =
                    button.dataset.financeMode;

                if (mode === "dispatch") {

                    page.classList.add(
                        "finance-dispatch-mode"
                    );

                    contextTitle.textContent =
                        "Dispatch · Marko Petrović";

                    showToast(
                        "Dispatch pregled",
                        "Prikazan je operativni finansijski kontekst vozača i ture."
                    );

                }
                else {

                    page.classList.remove(
                        "finance-dispatch-mode"
                    );

                    contextTitle.textContent =
                        "Marko Petrović";

                    showToast(
                        "Pregled vozača",
                        "Prikazane su finansije trenutno izabranog vozača."
                    );

                }

            }
        );

    });


    /* =========================================================
       REFRESH
    ========================================================== */

    refreshButton?.addEventListener(
        "click",
        () => {

            refreshButton.disabled = true;

            const original =
                refreshButton.innerHTML;

            refreshButton.innerHTML =
                "<span>↻</span> Osvežavanje...";

            setTimeout(() => {

                refreshButton.disabled = false;

                refreshButton.innerHTML =
                    original;

                animateFinanceValues();
                animateProgressBars();

                showToast(
                    "Finansije osvežene",
                    "Prikaz je uspešno osvežen."
                );

            }, 650);

        }
    );


    /* =========================================================
       EXPORT
    ========================================================== */

    exportButton?.addEventListener(
        "click",
        () => {

            showToast(
                "Izvoz",
                "UI za izvoz je spreman. Backend izvoz povezujemo kasnije."
            );

        }
    );


    /* =========================================================
       PREVIOUS TRIP
    ========================================================== */

    document
        .getElementById("previousTripDetails")
        ?.addEventListener(
            "click",
            () => {

                showToast(
                    "TR-2026-00887",
                    "Detaljni obračun prethodne ture biće otvoren iz API podataka."
                );

            }
        );


    /* =========================================================
       TOAST
    ========================================================== */

    let toastTimeout = null;

    function showToast(title, text) {

        toastTitle.textContent =
            title;

        toastText.textContent =
            text;

        toast.classList.add("show");

        if (toastTimeout) {
            clearTimeout(toastTimeout);
        }

        toastTimeout =
            setTimeout(
                () => toast.classList.remove("show"),
                3200
            );

    }


    /* =========================================================
       INITIALIZE
    ========================================================== */

    animateFinanceValues();

    animateProgressBars();

    updateExpenseFields();

})();
