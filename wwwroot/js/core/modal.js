(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};
    let active = null;
    let lastFocus = null;
    function open(target) {
        const modal = typeof target === "string" ? document.querySelector(target) : target;
        if (!modal) return;
        lastFocus = document.activeElement;
        modal.hidden = false;
        modal.setAttribute("aria-hidden", "false");
        document.body.classList.add("has-modal");
        active = modal;
        modal.querySelector("[autofocus], button, input, select, textarea, [tabindex]:not([tabindex='-1'])")?.focus();
    }
    function close(target = active) {
        const modal = typeof target === "string" ? document.querySelector(target) : target;
        if (!modal) return;
        modal.hidden = true;
        modal.setAttribute("aria-hidden", "true");
        document.body.classList.remove("has-modal");
        active = null;
        lastFocus?.focus?.();
    }
    document.addEventListener("click", event => {
        const openTrigger = event.target.closest("[data-modal-open]");
        if (openTrigger) { event.preventDefault(); open(openTrigger.dataset.modalOpen); return; }
        if (event.target.closest("[data-modal-close]")) { event.preventDefault(); close(); }
    });
    document.addEventListener("keydown", event => { if (event.key === "Escape" && active) close(); });
    root.modal = { open, close };
})();
