(() => {
    "use strict";
    const root = window.ProMapCargo = window.ProMapCargo || {};
    const bus = new EventTarget();
    root.events = {
        on(name, handler, options) { bus.addEventListener(name, handler, options); return () => bus.removeEventListener(name, handler, options); },
        once(name, handler) { bus.addEventListener(name, handler, { once: true }); },
        emit(name, detail = {}) { bus.dispatchEvent(new CustomEvent(name, { detail })); }
    };
})();
