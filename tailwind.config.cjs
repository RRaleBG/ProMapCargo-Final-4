/** @type {import('tailwindcss').Config} */
module.exports = {
    content: [
        "./Pages/**/*.cshtml",
        "./Views/**/*.cshtml",
        "./wwwroot/js/**/*.js",
        "./Styles/**/*.css"
    ],
    prefix: "tw-",
    important: true,
    corePlugins: {
        preflight: false,
    },
    theme: {
        extend: {
            colors: {
                pm: {
                    'bg-deep': '#010d0b', // Generiše: bg-pm-bg-deep
                    text: '#d9f8ed',       // Generiše: text-pm-text
                    teal: '#2dd4bf',       // Generiše: text-pm-teal, border-pm-teal
                    emerald: {
                        400: '#34d399',
                        500: '#10b981',    // Generiše: bg-pm-emerald-500
                        600: '#059669',
                    },
                    danger: '#fb7185',
                    warning: '#fbbf24'
                }
            },
            spacing: {
                sidebar: '238px', // Pravilno postavljeno van colors objekta
            },
            fontFamily: {
                sans: ["Inter", "system-ui", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "sans-serif"],
            },
            boxShadow: {
                panel: "0 16px 45px rgba(0, 0, 0, 0.22)",
            },
        },
    },
    plugins: [],
};