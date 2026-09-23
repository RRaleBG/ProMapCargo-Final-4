document.addEventListener("DOMContentLoaded", () => {

    const root = document.querySelector(
        '[data-module="moderation"]'
    );

    if (!root) {
        return;
    }


    /* =========================================================
       ELEMENTS
       ========================================================= */

    const cards = [
        ...root.querySelectorAll(".moderation-request")
    ];

    const filters = [
        ...root.querySelectorAll(".moderation-filter")
    ];

    const search = root.querySelector(
        "#moderationSearch"
    );

    const riskFilter = root.querySelector(
        "#riskFilter"
    );

    const domainFilter = root.querySelector(
        "#domainFilter"
    );

    const queueCount = root.querySelector(
        "#queueCount"
    );

    const pendingCount = root.querySelector(
        "#pendingCount"
    );

    const refreshButton = root.querySelector(
        "#refreshModeration"
    );

    const pendingButton = root.querySelector(
        "#showPending"
    );

    const decisionReason = root.querySelector(
        "#decisionReason"
    );

    const toast = root.querySelector(
        "#moderationToast"
    );

    const toastTitle = root.querySelector(
        "#toastTitle"
    );

    const toastText = root.querySelector(
        "#toastText"
    );


    let selectedStatus = "all";


    /* =========================================================
       TOAST
       ========================================================= */

    let toastTimer = null;

    function showToast(title, message) {

        if (!toast) {
            return;
        }

        if (toastTimer) {
            clearTimeout(toastTimer);
        }

        if (toastTitle) {
            toastTitle.textContent = title;
        }

        if (toastText) {
            toastText.textContent = message;
        }

        toast.classList.add("show");

        toastTimer = setTimeout(() => {
            toast.classList.remove("show");
        }, 2400);
    }


    /* =========================================================
       FILTERING
       ========================================================= */

    function applyFilters() {

        const query = (
            search?.value || ""
        )
            .toLowerCase()
            .trim();

        const risk =
            riskFilter?.value || "all";

        const domain =
            domainFilter?.value || "all";

        let visible = 0;
        let pending = 0;

        cards.forEach(card => {

            const statusMatch =
                selectedStatus === "all" ||
                card.dataset.status === selectedStatus;

            const riskMatch =
                risk === "all" ||
                card.dataset.risk === risk;

            const domainMatch =
                domain === "all" ||
                card.dataset.domain === domain;

            const searchText =
                card.dataset.search || "";

            const searchMatch =
                !query ||
                searchText
                    .toLowerCase()
                    .includes(query);

            const isVisible =
                statusMatch &&
                riskMatch &&
                domainMatch &&
                searchMatch;

            card.hidden = !isVisible;

            if (isVisible) {
                visible++;
            }

            if (
                card.dataset.status === "pending"
            ) {
                pending++;
            }

        });


        if (queueCount) {
            queueCount.textContent =
                `${ visible } zahteva`;
        }

        if (pendingCount) {
            pendingCount.textContent =
                pending;
        }

    }


    /* =========================================================
       STATUS FILTERS
       ========================================================= */

    filters.forEach(filter => {

        filter.addEventListener(
            "click",
            () => {

                filters.forEach(item => {
                    item.classList.remove("active");
                });

                filter.classList.add("active");

                selectedStatus =
                    filter.dataset.status || "all";

                applyFilters();
            }
        );

    });


    /* =========================================================
       SEARCH / SELECT FILTERS
       ========================================================= */

    search?.addEventListener(
        "input",
        applyFilters
    );

    riskFilter?.addEventListener(
        "change",
        applyFilters
    );

    domainFilter?.addEventListener(
        "change",
        applyFilters
    );


    /* =========================================================
       REQUEST SELECTION
       ========================================================= */

    cards.forEach(card => {

        card.addEventListener(
            "click",
            () => {

                cards.forEach(item => {
                    item.classList.remove(
                        "selected"
                    );
                });

                card.classList.add(
                    "selected"
                );


                const requestId =
                    card.dataset.id || "REQUEST";

                const title =
                    card.querySelector("h3")
                        ?.textContent
                        ?.trim() || "";


                const metadata =
                    card.querySelector(
                        ".request-meta"
                    )
                        ?.textContent
                        ?.replace(/\s+/g, " ")
                        ?.trim() || "";


                const risk =
                    card.dataset.risk || "medium";


                const detailTitle =
                    root.querySelector(
                        "#detailTitle"
                    );

                const detailSubtitle =
                    root.querySelector(
                        "#detailSubtitle"
                    );

                const detailRisk =
                    root.querySelector(
                        "#detailRisk"
                    );


                if (detailTitle) {
                    detailTitle.textContent =
                        `${ requestId } · ${ title } `;
                }


                if (detailSubtitle) {
                    detailSubtitle.textContent =
                        metadata;
                }


                if (detailRisk) {

                    detailRisk.textContent =
                        `${ risk.toUpperCase() } RISK`;

                    detailRisk.className =
                        `risk - badge risk - ${ risk } `;
                }

            }
        );

    });


    /* =========================================================
       SHOW PENDING
       ========================================================= */

    pendingButton?.addEventListener(
        "click",
        () => {

            const pendingFilter =
                root.querySelector(
                    '[data-status="pending"]'
                );

            if (pendingFilter) {
                pendingFilter.click();
            }

            const queue =
                root.querySelector(
                    "#moderationQueue"
                );

            queue?.scrollIntoView({
                behavior: "smooth",
                block: "start"
            });

        }
    );


    /* =========================================================
       REFRESH
       ========================================================= */

    refreshButton?.addEventListener(
        "click",
        () => {

            applyFilters();

            showToast(
                "Queue osvežena",
                "Moderation zahtevi su ponovo učitani."
            );

        }
    );


    /* =========================================================
       DECISION
       ========================================================= */

    root.querySelectorAll(
        ".decision-btn"
    ).forEach(button => {

        button.addEventListener(
            "click",
            () => {

                const selected =
                    root.querySelector(
                        ".moderation-request.selected"
                    );

                if (!selected) {
                    showToast(
                        "Nije izabran zahtev",
                        "Izaberite zahtev iz moderation queue."
                    );

                    return;
                }


                const decision =
                    button.dataset.decision;

                const reason =
                    decisionReason?.value
                        ?.trim() || "";


                /*
                 * Reject and Request Changes
                 * require a reason.
                 */
                if (
                    decision !== "approved" &&
                    !reason
                ) {

                    showToast(
                        "Potrebno obrazloženje",
                        "Za odbijanje ili traženje izmena unesite razlog."
                    );

                    decisionReason?.focus();

                    return;
                }


                const requestId =
                    selected.dataset.id;


                /*
                 * "changes" returns the request
                 * to the pending queue.
                 */
                if (decision === "changes") {

                    selected.dataset.status =
                        "pending";

                }
                else {

                    selected.dataset.status =
                        decision;

                }


                /*
                 * Remove old status badge.
                 */
                const oldBadge =
                    selected.querySelector(
                        ".status-badge"
                    );

                oldBadge?.remove();


                /*
                 * Add new status badge.
                 */
                if (
                    decision === "approved" ||
                    decision === "rejected"
                ) {

                    const badge =
                        document.createElement(
                            "span"
                        );

                    badge.className =
                        "status-badge " +
                        (
                            decision === "approved"
                                ? "status-approved"
                                : "status-rejected"
                        );

                    badge.textContent =
                        decision === "approved"
                            ? "APPROVED"
                            : "REJECTED";


                    selected
                        .querySelector(
                            ".request-top"
                        )
                        ?.appendChild(badge);
                }


                /*
                 * Feedback.
                 */
                let message;

                if (decision === "approved") {

                    message =
                        `${ requestId } · zahtev je odobren.`;

                }
                else if (decision === "rejected") {

                    message =
                        `${ requestId } · zahtev je odbijen.`;

                }
                else {

                    message =
                        `${ requestId } · zahtev je vraćen na izmene.`;

                }


                showToast(
                    "Odluka evidentirana",
                    message
                );


                /*
                 * Clear decision reason
                 * after successful local action.
                 */
                if (decisionReason) {
                    decisionReason.value = "";
                }


                applyFilters();

            }
        );

    });


    /* =========================================================
       INITIAL STATE
       ========================================================= */

    applyFilters();

});