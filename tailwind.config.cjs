module.exports = {
    content: [
        "./Pages/**/*.cshtml",
        "./wwwroot/js/**/*.js",
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
                    bg: "#010d0b",
                    surface: "#041f1a",
                    elevated: "#07342b",
                    border: "#1c5448",
                    accent: "#10b981",
                    teal: "#2dd4bf",
                    text: "#f0fdf8",
                    muted: "#8eb8aa",
                    dim: "#638b80",
                },
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
