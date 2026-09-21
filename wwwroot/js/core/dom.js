(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};
    root.dom = {
        qs(selector, scope = document) { return scope.querySelector(selector); },
        qsa(selector, scope = document) { return Array.from(scope.querySelectorAll(selector)); },
        byId(id) { return document.getElementById(id); },
        on(target, event, handler, options) { if (target) target.addEventListener(event, handler, options); return () => target?.removeEventListener(event, handler, options); },
        text(target, value) { if (target) target.textContent = value ?? ""; },
        show(target) { if (target) target.hidden = false; },
        hide(target) { if (target) target.hidden = true; },
        toggle(target, force) { if (target) target.hidden = force === undefined ? !target.hidden : !force; },
        closest(target, selector) { return target instanceof Element ? target.closest(selector) : null; }
    };
})();
