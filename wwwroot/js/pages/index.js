(() => {
    "use strict";

    const header = document.getElementById("publicHeader");
    const mobileMenuButton = document.getElementById("mobileMenuButton");
    const mobileNav = document.getElementById("mobileNav");

    const updateHeader = () => {
        if (!header) {
            return;
        }

        if (window.scrollY > 24) {
            header.classList.add("scrolled");
        } else {
            header.classList.remove("scrolled");
        }
    };

    window.addEventListener("scroll", updateHeader, {
        passive: true
    });

    updateHeader();

    if (mobileMenuButton && mobileNav) {
        mobileMenuButton.addEventListener("click", () => {
            const isOpen = mobileNav.classList.toggle("open");

            mobileMenuButton.setAttribute(
                "aria-expanded",
                isOpen ? "true" : "false"
            );
        });

        mobileNav.querySelectorAll("a").forEach(link => {
            link.addEventListener("click", () => {
                mobileNav.classList.remove("open");
                mobileMenuButton.setAttribute(
                    "aria-expanded",
                    "false"
                );
            });
        });
    }

    const revealElements = document.querySelectorAll(".pm-reveal");

    if ("IntersectionObserver" in window) {
        const revealObserver = new IntersectionObserver(
            entries => {
                entries.forEach(entry => {
                    if (!entry.isIntersecting) {
                        return;
                    }

                    entry.target.classList.add("visible");
                    revealObserver.unobserve(entry.target);
                });
            },
            {
                threshold: 0.12,
                rootMargin: "0px 0px -50px 0px"
            }
        );

        revealElements.forEach((element, index) => {
            element.style.transitionDelay = `${Math.min(index % 4, 3) * 70}ms`;
            revealObserver.observe(element);
        });
    } else {
        revealElements.forEach(element => {
            element.classList.add("visible");
        });
    }

    const engineNodes = document.querySelectorAll(".pm-engine-node");

    if (engineNodes.length > 0) {
        let activeIndex = 0;

        setInterval(() => {
            engineNodes.forEach(node => {
                node.classList.remove("active");
            });

            engineNodes[activeIndex].classList.add("active");
            activeIndex = (activeIndex + 1) % engineNodes.length;
        }, 1800);
    }

    const heroVisual = document.querySelector(".pm-hero-visual");
    const prefersReducedMotion =
        window.matchMedia &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const hasTouch =
        "ontouchstart" in window ||
        navigator.maxTouchPoints > 0;

    if (heroVisual && !prefersReducedMotion && !hasTouch) {
        heroVisual.addEventListener("mousemove", event => {
            const rect = heroVisual.getBoundingClientRect();
            const x = (event.clientX - rect.left) / rect.width - 0.5;
            const y = (event.clientY - rect.top) / rect.height - 0.5;

            heroVisual.style.transform = `perspective(1000px) rotateY(${x * 2}deg) rotateX(${y * -2}deg)`;
        });

        heroVisual.addEventListener("mouseleave", () => {
            heroVisual.style.transform = "";
        });
    }

    document.querySelectorAll('a[href^="#"]').forEach(link => {
        link.addEventListener("click", event => {
            const targetId = link.getAttribute("href");

            if (!targetId || targetId === "#") {
                return;
            }

            const target = document.querySelector(targetId);

            if (!target) {
                return;
            }

            event.preventDefault();

            target.scrollIntoView({
                behavior: prefersReducedMotion ? "auto" : "smooth",
                block: "start"
            });
        });
    });
})();
