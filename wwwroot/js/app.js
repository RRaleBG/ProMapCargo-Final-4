(() => {
    "use strict";
    const root = (window.ProMapCargo = window.ProMapCargo || {});

    function initializeTheme() {
        let theme = "emerald";
        try {
            theme = localStorage.getItem("pm-theme") || theme;
        } catch { }
        if (!new Set(["emerald", "white"]).has(theme)) theme = "emerald";
        document.documentElement.dataset.theme = theme;
    }

    root.theme = {
        get() {
            return document.documentElement.dataset.theme || "emerald";
        },
        set(theme) {
            if (!new Set(["emerald", "white"]).has(theme)) return;
            document.documentElement.dataset.theme = theme;
            try {
                localStorage.setItem("pm-theme", theme);
            } catch { }
            root.events?.emit("theme:changed", { theme });
        },
        toggle() {
            this.set(this.get() === "emerald" ? "white" : "emerald");
        },
    };

    function initializeGlobalSearch() {
        const input = document.querySelector(
            ".pm-global-search input[type='search']",
        );
        const list = document.getElementById("pm-search-results");

        document.addEventListener("keydown", (event) => {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
                event.preventDefault();
                input?.focus();
                input?.select();
            }
        });

        if (!input || !list) return;

        // Quick-jump: builds its index from the sidebar navigation.
        const pages = Array.from(
            document.querySelectorAll(".pm-navigation a.pm-nav-item"),
        )
            .map((a) => ({
                label: (a.querySelector(".pm-nav-label")?.textContent || "").trim(),
                href: a.getAttribute("href") || "",
            }))
            .filter((p) => p.label && p.href);

        const normalize = (value) =>
            String(value || "")
                .toLowerCase()
                .normalize("NFD")
                .replace(/[\u0300-\u036f]/g, "")
                .replace(/đ/g, "dj");

        let matches = [];
        let active = -1;

        function close() {
            list.hidden = true;
            list.innerHTML = "";
            matches = [];
            active = -1;
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
        }

        function setActive(index) {
            active = index;
            Array.from(list.children).forEach((li, i) => {
                const on = i === active;
                li.classList.toggle("is-active", on);
                li.setAttribute("aria-selected", String(on));
                if (on) input.setAttribute("aria-activedescendant", li.id);
            });
        }

        function render() {
            const query = normalize(input.value.trim());
            if (!query) return close();
            matches = pages.filter((p) => normalize(p.label).includes(query)).slice(0, 8);
            list.innerHTML = "";
            if (!matches.length) {
                const empty = document.createElement("li");
                empty.className = "pm-search-empty";
                empty.textContent = "Nema rezultata";
                list.appendChild(empty);
            } else {
                matches.forEach((p, i) => {
                    const li = document.createElement("li");
                    li.id = `pm-search-option-${i}`;
                    li.setAttribute("role", "option");
                    li.textContent = p.label;
                    li.addEventListener("mousedown", (e) => {
                        e.preventDefault();
                        window.location.href = p.href;
                    });
                    list.appendChild(li);
                });
                setActive(0);
            }
            list.hidden = false;
            input.setAttribute("aria-expanded", "true");
        }

        input.addEventListener("input", render);
        input.addEventListener("blur", () => window.setTimeout(close, 120));
        input.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                input.value = "";
                close();
            } else if (event.key === "ArrowDown" && matches.length) {
                event.preventDefault();
                setActive((active + 1) % matches.length);
            } else if (event.key === "ArrowUp" && matches.length) {
                event.preventDefault();
                setActive((active - 1 + matches.length) % matches.length);
            } else if (event.key === "Enter" && matches[active]) {
                event.preventDefault();
                window.location.href = matches[active].href;
            }
        });
    }

    function initializeSystemStatus() {
        const holders = Array.from(document.querySelectorAll("[data-system-status]"));
        if (!holders.length) return;

        function apply(online) {
            holders.forEach((holder) => {
                holder.classList.toggle("is-offline", !online);
                const text = holder.querySelector("[data-system-status-text]");
                const badge = holder.querySelector("[data-system-status-badge]");
                if (text) text.textContent = online ? (badge ? "Sistem operativan" : "Sistem online") : "Nema veze sa serverom";
                if (badge) badge.textContent = online ? "LIVE" : "OFFLINE";
            });
        }

        async function probe() {
            if (!navigator.onLine) return apply(false);
            const controller = new AbortController();
            const timer = window.setTimeout(() => controller.abort(), 5000);
            try {
                // Any HTTP response (even 401/404) proves the server is reachable.
                await fetch("/api/alerts/count", { method: "HEAD", cache: "no-store", signal: controller.signal });
                apply(true);
            } catch {
                apply(false);
            } finally {
                window.clearTimeout(timer);
            }
        }

        window.addEventListener("online", probe);
        window.addEventListener("offline", () => apply(false));
        probe();
        window.setInterval(probe, 30000);
    }

    function initializeDoubleSubmitProtection() {
        document.addEventListener(
            "submit",
            (event) => {
                const form = event.target;
                if (
                    !(form instanceof HTMLFormElement) ||
                    form.dataset.allowDoubleSubmit === "true"
                )
                    return;
                form
                    .querySelectorAll("button[type='submit'],input[type='submit']")
                    .forEach((button) => {
                        button.disabled = true;
                        button.setAttribute("aria-busy", "true");
                    });
            },
            true,
        );
    }

    function initializeLucideIcons() {
    document.addEventListener('DOMContentLoaded', () => {
            if (window.lucide) {
                lucide.createIcons();
            }
        });
    }

    function initializeReveal() {
        const elements = Array.from(document.querySelectorAll(".pm-reveal"));
        if (!elements.length) return;
        if (!("IntersectionObserver" in window)) {
            elements.forEach((el) => el.classList.add("is-visible"));
            return;
        }
        const observer = new IntersectionObserver(
            (entries) =>
                entries.forEach((entry) => {
                    if (entry.isIntersecting) {
                        entry.target.classList.add("is-visible");
                        observer.unobserve(entry.target);
                    }
                }),
            { threshold: 0.08 },
        );
        elements.forEach((el) => observer.observe(el));
    }

    function initializeSidebar() {
        const toggle = document.getElementById("pm-sidebar-toggle");
        if (!toggle) return;
        document.querySelectorAll(".pm-nav-item").forEach((link) =>
            link.addEventListener("click", () => {
                if (window.matchMedia("(max-width: 980px)").matches)
                    toggle.checked = false;
            }),
        );
    }

    function initializeUnhandledErrors() {
        window.addEventListener("unhandledrejection", (event) => {
            const error = event.reason;
            if (error?.name === "HttpError") root.toast?.error(error.message);
        });
    }

    document.addEventListener("DOMContentLoaded", async () => {
        initializeTheme();
        initializeLucideIcons();
        initializeGlobalSearch();
        initializeSystemStatus();
        initializeDoubleSubmitProtection();
        initializeReveal();
        initializeSidebar();
        initializeUnhandledErrors();
        await root.alerts?.refresh?.();
        root.events?.emit("app:ready");
    });
})();
