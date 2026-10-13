// Builds wwwroot/lib/lucide-icons.js: lucide's createIcons() with only the icons whose names
// appear in the project (data-lucide="..." or a quoted string in Pages/, wwwroot/js/,
// Controllers/, Services/). An icon missing from the bundle still renders: the bundle then
// loads the full wwwroot/lib/lucide.min.js once. Rebuilding only makes such pages lighter.
//
//   cd Tools/lucide-subset && npm install && npm run build
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";
import esbuild from "esbuild";

const require = createRequire(import.meta.url);
const lucide = require("lucide");
const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..", "..");

function walk(dir, exts, out = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (["node_modules", "bin", "obj", ".git", "lib", "maps", "offline-bundles", "css"].includes(entry.name)) continue;
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(full, exts, out);
        else if (exts.some((e) => entry.name.endsWith(e))) out.push(full);
    }
    return out;
}

const sources = [
    ...walk(path.join(root, "Pages"), [".cshtml", ".cs"]),
    ...walk(path.join(root, "wwwroot"), [".js", ".html"]),
    ...walk(path.join(root, "Controllers"), [".cs"]),
    ...walk(path.join(root, "Services"), [".cs"]),
].map((f) => fs.readFileSync(f, "utf8")).join("\n");

const pascal = (name) => name.split(/[-_\s]+/).map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join("");
const names = new Set();
for (const m of sources.matchAll(/["'`]([a-z][a-z0-9]*(?:-[a-z0-9]+)*)["'`]/g)) names.add(m[1]);
for (const m of sources.matchAll(/data-lucide="([a-z][a-z0-9-]*)"/g)) names.add(m[1]);
const icons = [...new Set([...names].map(pascal).filter((key) => lucide.icons[key]))].sort();

const entry = path.join(here, "entry.generated.js");
fs.writeFileSync(entry, `import { createIcons, ${icons.join(", ")} } from "lucide";
const icons = { ${icons.join(", ")} };
const fullUrl = (document.currentScript?.src || "/lib/lucide-icons.js").replace(/lucide-icons\\.js(\\?.*)?$/, "lucide.min.js");
let fullRequested = false;

// An icon outside the bundle (a new data-lucide name): load the full library once; it
// replaces window.lucide, so this and later createIcons() calls draw every icon.
function loadFullFor(missing) {
    if (fullRequested || missing.length === 0) return;
    fullRequested = true;
    const script = document.createElement("script");
    script.src = fullUrl;
    script.onload = () => window.lucide?.createIcons?.();
    document.head.appendChild(script);
}

function toPascal(name) {
    return name.split(/[-_\\s]+/).map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join("");
}

window.lucide = {
    icons,
    createIcons(options = {}) {
        createIcons({ ...options, icons: options.icons || icons });
        const missing = [...document.querySelectorAll("[data-lucide]:not(svg)")]
            .map((el) => el.getAttribute("data-lucide"))
            .filter((name) => name && !icons[toPascal(name)]);
        loadFullFor(missing);
    },
};
`);

const outfile = path.join(root, "wwwroot", "lib", "lucide-icons.js");
await esbuild.build({
    entryPoints: [entry],
    bundle: true,
    minify: true,
    format: "iife",
    target: ["es2019"],
    banner: { js: `/* lucide ${require("lucide/package.json").version} (ISC): ${icons.length} icons used by ProMap Cargo; others load lucide.min.js. Built by Tools/lucide-subset. */` },
    outfile,
});
fs.rmSync(entry);
console.log(`${icons.length} icons, ${fs.statSync(outfile).size} bytes -> ${path.relative(root, outfile)}`);
