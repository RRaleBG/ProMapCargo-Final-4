const fs = require("node:fs");
const path = require("node:path");

const source = path.resolve(__dirname, "../node_modules/lucide/dist/umd/lucide.min.js");
const destinationDirectory = path.resolve(__dirname, "../wwwroot/lib");
const destination = path.join(destinationDirectory, "lucide.min.js");

if (!fs.existsSync(source)) {
    throw new Error(`Lucide UMD bundle was not found at ${source}. Run npm ci first.`);
}

fs.mkdirSync(destinationDirectory, { recursive: true });
fs.copyFileSync(source, destination);
console.log(`Copied self-hosted Lucide bundle to ${destination}`);
