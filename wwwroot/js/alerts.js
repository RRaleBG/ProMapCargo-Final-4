(function () {
    "use strict";

    window.ProMap = window.ProMap || {};

    const STORAGE_KEY = "promapcargo.alerts.v1";

    const DEFAULT_ALERTS = [
        {
            id: "ALT-9021",
            severity: "critical",
            category: "navigation",
            type: "OFF_ROUTE",
            status: "open",
            title: "Vozilo je van planirane rute",
            message: "Vozilo BG-241-AA je 184 m van aktivne truck rute.",
            vehicleId: "V-001",
            vehicle: "BG-241-AA",
            driver: "Milan Petrović",
            trip: "TR-2026-00887",
            location: "Novi Sad, Srbija",
            distanceMeters: 184,
            source: "Navigation",
            createdAt: "2026-09-18T18:42:00+02:00",
            action: "Otvorite navigaciju i proverite poziciju vozila.",
            actionUrl: "/navigation"
        },
        {
            id: "ALT-9022",
            severity: "high",
            category: "compliance",
            type: "DOCUMENT_EXPIRING",
            status: "open",
            title: "Dokument vozila uskoro ističe",
            message: "Registracija vozila BG-123-AB ističe za 6 dana.",
            vehicleId: "V-002",
            vehicle: "BG-123-AB",
            driver: "Marko Petrović",
            trip: "TR-2026-00891",
            location: "Beograd, Srbija",
            source: "Compliance",
            createdAt: "2026-09-18T17:31:00+02:00",
            action: "Otvorite Compliance i unesite novi dokument.",
            actionUrl: "/compliance"
        },
        {
            id: "ALT-9023",
            severity: "high",
            category: "gps",
            type: "GPS_LOST",
            status: "open",
            title: "GPS signal izgubljen",
            message: "Vozilo NI-401-TT nema validan GPS signal već 4 minuta.",
            vehicleId: "V-003",
            vehicle: "NI-401-TT",
            driver: "Nikola Ilić",
            trip: "TR-2026-00892",
            location: "Niš, Srbija",
            source: "Monitoring",
            createdAt: "2026-09-18T16:58:00+02:00",
            action: "Proverite GPS uređaj i poslednju poznatu poziciju.",
            actionUrl: "/monitoring"
        },
        {
            id: "ALT-9024",
            severity: "critical",
            category: "navigation",
            type: "TRUCK_RESTRICTION",
            status: "open",
            title: "Ruta sadrži truck restriction",
            message: "Ruta za vozilo 40 t prolazi kroz segment sa ograničenjem visine 4.0 m.",
            vehicleId: "V-004",
            vehicle: "NS-777-AA",
            driver: "Petar Jovanović",
            trip: "TR-2026-00893",
            location: "Novi Sad, Srbija",
            source: "Routing",
            createdAt: "2026-09-18T16:21:00+02:00",
            action: "Otvorite navigaciju i izračunajte novu truck rutu.",
            actionUrl: "/navigation"
        },
        {
            id: "ALT-9025",
            severity: "medium",
            category: "border",
            type: "BORDER_DELAY",
            status: "open",
            title: "Zadržavanje na granici",
            message: "Procena čekanja na Horgoš prelazu je 31 minut.",
            vehicleId: "V-005",
            vehicle: "SU-551-TT",
            driver: "Stefan Marković",
            trip: "TR-2026-00894",
            location: "Horgoš",
            source: "Dispatch",
            createdAt: "2026-09-18T15:49:00+02:00",
            action: "Proverite status ture i eventualno promenite plan.",
            actionUrl: "/trips"
        },
        {
            id: "ALT-9026",
            severity: "high",
            category: "maintenance",
            type: "SERVICE_DUE",
            status: "open",
            title: "Vozilo zahteva servis",
            message: "Mercedes-Benz Actros BG-908-KK je dostigao servisni interval.",
            vehicleId: "V-006",
            vehicle: "BG-908-KK",
            driver: "Aleksandar Nikolić",
            trip: null,
            location: "Beograd, Srbija",
            source: "Fleet",
            createdAt: "2026-09-18T14:17:00+02:00",
            action: "Otvorite vozilo i unesite servisni događaj.",
            actionUrl: "/vehicles"
        },
        {
            id: "ALT-9027",
            severity: "high",
            category: "tachograph",
            type: "DRIVING_TIME",
            status: "open",
            title: "Vozač se približava limitu vožnje",
            message: "Preostalo vreme vožnje je ispod internog operativnog praga.",
            vehicleId: "V-007",
            vehicle: "KG-221-ZZ",
            driver: "Dejan Stojanović",
            trip: "TR-2026-00895",
            location: "Kragujevac, Srbija",
            source: "Driver / Tachograph",
            createdAt: "2026-09-18T13:42:00+02:00",
            action: "Proverite tachograph stanje i planirajte odmor.",
            actionUrl: "/drivers"
        },
        {
            id: "ALT-9028",
            severity: "medium",
            category: "fuel",
            type: "FUEL_CARD",
            status: "open",
            title: "Neobična transakcija fuel karticom",
            message: "Fuel card FC-0042 ima transakciju koja zahteva proveru.",
            vehicleId: "V-008",
            vehicle: "BG-440-AA",
            driver: "Ivan Simić",
            trip: "TR-2026-00896",
            location: "Šabac, Srbija",
            source: "Finance",
            createdAt: "2026-09-18T12:33:00+02:00",
            action: "Otvorite finansije i proverite fuel card transakciju.",
            actionUrl: "/finance"
        },
        {
            id: "ALT-9029",
            severity: "medium",
            category: "orders",
            type: "ORDER_DELAY",
            status: "open",
            title: "Transportni nalog kasni",
            message: "Transportni nalog TO-2026-00191 kasni 24 minuta.",
            vehicleId: "V-009",
            vehicle: "NS-123-AA",
            driver: "Miloš Đorđević",
            trip: "TR-2026-00897",
            location: "Subotica, Srbija",
            source: "Transport Orders",
            createdAt: "2026-09-18T11:54:00+02:00",
            action: "Otvorite transportni nalog i proverite SLA.",
            actionUrl: "/orders"
        },
        {
            id: "ALT-9030",
            severity: "critical",
            category: "compliance",
            type: "DOCUMENT_EXPIRED",
            status: "open",
            title: "Compliance dokument je istekao",
            message: "TIR dokument za vozilo NI-401-TT je istekao.",
            vehicleId: "V-003",
            vehicle: "NI-401-TT",
            driver: "Nikola Ilić",
            trip: null,
            location: "Niš, Srbija",
            source: "Compliance",
            createdAt: "2026-09-18T10:42:00+02:00",
            action: "Vozilo je compliance blokirano dok se dokument ne ažurira.",
            actionUrl: "/compliance"
        },
        {
            id: "ALT-9031",
            severity: "info",
            category: "system",
            type: "ROUTING_FALLBACK",
            status: "open",
            title: "Routing koristi OSRM fallback",
            message: "PostGIS graph nije korišćen za poslednju rutu; aktiviran je OSRM fallback.",
            vehicleId: null,
            vehicle: null,
            driver: null,
            trip: "TR-2026-00898",
            location: "Sistem",
            source: "Routing Engine",
            createdAt: "2026-09-18T10:12:00+02:00",
            action: "Proverite PostGIS graph ako se fallback ponavlja.",
            actionUrl: "/navigation"
        },
        {
            id: "ALT-9032",
            severity: "medium",
            category: "driver",
            type: "LICENSE_EXPIRING",
            status: "open",
            title: "Vozačka dozvola uskoro ističe",
            message: "Dokument vozača ističe u narednih 14 dana.",
            vehicleId: null,
            vehicle: null,
            driver: "Marko Petrović",
            trip: null,
            location: "Fleet",
            source: "Drivers",
            createdAt: "2026-09-18T09:27:00+02:00",
            action: "Otvorite profil vozača i ažurirajte dokument.",
            actionUrl: "/drivers"
        },
        {
            id: "ALT-9033",
            severity: "high",
            category: "vehicle",
            type: "FAULT",
            status: "open",
            title: "Prijavljen kvar na vozilu",
            message: "Vozilo BG-777-KK ima aktivan kvar koji zahteva proveru.",
            vehicleId: "V-011",
            vehicle: "BG-777-KK",
            driver: "Vladimir Jovanović",
            trip: null,
            location: "Beograd, Srbija",
            source: "Fleet",
            createdAt: "2026-09-18T08:48:00+02:00",
            action: "Otvorite vozilo i proverite prijavljeni kvar.",
            actionUrl: "/vehicles"
        },
        {
            id: "ALT-9034",
            severity: "low",
            category: "finance",
            type: "EXPENSE_PENDING",
            status: "open",
            title: "Putni trošak čeka odobrenje",
            message: "Novi putni trošak vozača čeka finansijsku proveru.",
            vehicleId: "V-012",
            vehicle: "KG-500-AA",
            driver: "Nikola Marković",
            trip: "TR-2026-00899",
            location: "Finansije",
            source: "Finance",
            createdAt: "2026-09-18T08:11:00+02:00",
            action: "Otvorite Finansije i pregledajte trošak.",
            actionUrl: "/finance"
        }
    ];

    const CATEGORY_LABELS = {
        navigation: "Navigacija",
        gps: "GPS",
        compliance: "Compliance",
        border: "Granica",
        maintenance: "Servis",
        tachograph: "Tachograph",
        fuel: "Gorivo / Fuel card",
        orders: "Transportni nalozi",
        trips: "Ture",
        driver: "Vozači",
        vehicle: "Vozila",
        finance: "Finansije",
        system: "Sistem"
    };

    const SEVERITY_LABELS = {
        critical: "Critical",
        high: "High",
        medium: "Medium",
        low: "Low",
        info: "Info"
    };

    function cloneDefaults() {
        return DEFAULT_ALERTS.map(function (item) {
            return Object.assign({}, item);
        });
    }

    function readAlerts() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);

            if (!raw) {
                const defaults = cloneDefaults();

                localStorage.setItem(
                    STORAGE_KEY,
                    JSON.stringify(defaults)
                );

                return defaults;
            }

            const parsed = JSON.parse(raw);

            if (!Array.isArray(parsed)) {
                return cloneDefaults();
            }

            return parsed;
        } catch (error) {
            console.warn(
                "ProMap Alerts storage error:",
                error
            );

            return cloneDefaults();
        }
    }

    function writeAlerts(alerts) {
        try {
            localStorage.setItem(
                STORAGE_KEY,
                JSON.stringify(alerts)
            );
        } catch (error) {
            console.warn(
                "ProMap Alerts write error:",
                error
            );
        }

        updateBadges(alerts);

        window.dispatchEvent(
            new CustomEvent(
                "promap:alerts-changed",
                {
                    detail: {
                        alerts: alerts
                    }
                }
            )
        );
    }

    function getAlerts() {
        return readAlerts();
    }

    function getOpenAlerts() {
        return getAlerts().filter(function (alert) {
            return alert.status === "open";
        });
    }

    function getOpenCount() {
        return getOpenAlerts().length;
    }

    function getCriticalCount() {
        return getOpenAlerts().filter(function (alert) {
            return alert.severity === "critical";
        }).length;
    }

    function getTodayCount() {
        const today = new Date();

        return getAlerts().filter(function (alert) {
            const date = new Date(alert.createdAt);

            return (
                date.getFullYear() === today.getFullYear() &&
                date.getMonth() === today.getMonth() &&
                date.getDate() === today.getDate()
            );
        }).length;
    }

    function updateBadges(alerts) {
        const source = Array.isArray(alerts)
            ? alerts
            : readAlerts();

        const count = source.filter(function (alert) {
            return alert.status === "open";
        }).length;

        document
            .querySelectorAll(
                ".pm-nav-count[data-alert-count]"
            )
            .forEach(function (element) {
                element.textContent = count;

                element.hidden = count <= 0;
            });

        document
            .querySelectorAll(
                ".pm-notification-badge[data-alert-count]"
            )
            .forEach(function (element) {
                element.textContent = count;

                element.hidden = count <= 0;
            });
    }

    function updateBadgesNow() {
        updateBadges(readAlerts());
    }

    function setStatus(id, status) {
        const alerts = readAlerts();

        const index = alerts.findIndex(function (alert) {
            return String(alert.id) === String(id);
        });

        if (index === -1) {
            return null;
        }

        alerts[index].status = status;

        alerts[index].updatedAt =
            new Date().toISOString();

        writeAlerts(alerts);

        return alerts[index];
    }

    function acknowledge(id) {
        return setStatus(
            id,
            "acknowledged"
        );
    }

    function resolve(id) {
        return setStatus(
            id,
            "resolved"
        );
    }

    function reopen(id) {
        return setStatus(
            id,
            "open"
        );
    }

    function snooze(id, minutes) {
        const alerts = readAlerts();

        const index = alerts.findIndex(function (alert) {
            return String(alert.id) === String(id);
        });

        if (index === -1) {
            return null;
        }

        const until =
            Date.now() +
            Math.max(
                1,
                Number(minutes) || 30
            ) *
            60 *
            1000;

        alerts[index].status = "snoozed";

        alerts[index].snoozedUntil =
            new Date(until).toISOString();

        alerts[index].updatedAt =
            new Date().toISOString();

        writeAlerts(alerts);

        return alerts[index];
    }

    function getCategoryLabel(category) {
        return (
            CATEGORY_LABELS[category] ||
            category ||
            "Sistem"
        );
    }

    function getSeverityLabel(severity) {
        return (
            SEVERITY_LABELS[severity] ||
            severity ||
            "Info"
        );
    }

    function init() {
        updateBadgesNow();

        window.addEventListener(
            "storage",
            function (event) {
                if (event.key === STORAGE_KEY) {
                    updateBadgesNow();
                }
            }
        );

        window.addEventListener(
            "promap:alerts-changed",
            function (event) {
                updateBadges(
                    event.detail
                        ? event.detail.alerts
                        : null
                );
            }
        );
    }

    window.ProMap.Alerts = {
        storageKey: STORAGE_KEY,
        defaults: DEFAULT_ALERTS,
        get: getAlerts,
        getOpen: getOpenAlerts,
        getOpenCount: getOpenCount,
        getCriticalCount: getCriticalCount,
        getTodayCount: getTodayCount,
        acknowledge: acknowledge,
        resolve: resolve,
        reopen: reopen,
        snooze: snooze,
        setStatus: setStatus,
        categoryLabel: getCategoryLabel,
        severityLabel: getSeverityLabel,
        refreshBadges: updateBadgesNow
    };

    if (
        document.readyState === "loading"
    ) {
        document.addEventListener(
            "DOMContentLoaded",
            init
        );
    } else {
        init();
    }
})();