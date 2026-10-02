/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Pages/**/*.cshtml",
    "./wwwroot/js/**/*.js"
  ],
  theme: {
    extend: {
      colors: {
        pm: {
          bg: "#031d18",
          surface: "#0b2d26",
          surfaceStrong: "#10382f",
          border: "#1c5448",
          text: "#f0fdf8",
          muted: "#8eb8aa",
          accent: "#2dd4bf",
          emerald: "#10b981",
          warning: "#fbbf24",
          danger: "#fb7185"
        }
      },
      fontFamily: {
        sans: ["Inter", "Segoe UI", "system-ui", "sans-serif"]
      },
      boxShadow: {
        'pm-soft': "0 16px 45px rgba(0,0,0,0.22)",
        'pm-glow': "0 0 35px rgba(16,185,129,0.09)"
      },
      borderRadius: {
        'pm': "12px",
        'pm-lg': "16px"
      },
      backdropBlur: {
        pm: "18px"
      }
    }
  },
  plugins: []
};
