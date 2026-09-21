# Copilot Instructions

## Project guidelines
- This repository should remain independent of CDN-hosted frontend runtime assets.
- Map styles belong under `/styles`.
- PMTiles archives belong under `/maps`.
- Browser-facing asset URLs must be root-relative. Do not use `wwwroot/` in client URLs.

## Repository overview
- Solution: `ProMapCargo.sln`
- Main web app: `ProMapCargo.Api.csproj`
- Importer: `Importer/ProMapCargo.OsmImporter.csproj`
- Target framework: `.NET 10`
- UI stack: Razor Pages + controllers + SignalR
- Local mapping stack: Leaflet for interaction, MapLibre for the non-interactive basemap overlay, PMTiles for vector archives
- Data stack: PostgreSQL + PostGIS
- Routing stack: PostGIS-first routing with OSRM integration

## Current local-only map architecture
- Static frontend map assets are served from `wwwroot/lib`, `wwwroot/fonts`, `wwwroot/styles`, and `wwwroot/maps`.
- PMTiles archives are served by `Program.cs` through `/maps/{region}.pmtiles`.
- The shared local basemap helper is `wwwroot/js/promap-map-layers.js`.
- The Navigation page uses `wwwroot/js/navigation.js` with `serbia.pmtiles` as the default local archive.
- Dispatch, Monitoring, and Navigation now use local PMTiles basemaps instead of remote OSM/CARTO/TomTom/Esri tile sources.
- Local glyphs for MapLibre labels are served from `/fonts/{fontstack}/{range}.pbf`.

## Important repository rules
- Keep generated map data, tool caches, and helper binaries out of Git. Follow `.gitignore` for `.planetiler`, raw OSM files, OSRM outputs, and generated PMTiles archives.
- Keep PMTiles generation scripts aligned with runtime serving paths. Generated `.pmtiles` files go to `wwwroot/maps`.
- Prefer the shared local basemap helper for Leaflet pages instead of introducing new remote basemap code.
- If a page needs a different archive, switch between `/maps/serbia.pmtiles` and `/maps/europe.pmtiles` explicitly.
- Preserve the existing Razor Pages structure and keep diffs minimal.

## Key files for map work
- `Pages/Navigation/Index.cshtml`
- `Pages/Dispatch/Index.cshtml`
- `Pages/Monitoring/Index.cshtml`
- `Pages/Shared/_Layout.cshtml`
- `wwwroot/js/navigation.js`
- `wwwroot/js/promap-map-layers.js`
- `wwwroot/styles/promap-dark.json`
- `Program.cs`
- `scripts/build-serbia-pmtiles.ps1`
- `scripts/build-serbia-pmtiles.sh`
- `.gitignore`

## Current findings
1. Local frontend runtime dependencies are now self-hosted under `wwwroot/lib` and `wwwroot/fonts`.
2. The old remote basemap controller endpoints were removed because pages now read PMTiles archives directly through `/maps/{region}.pmtiles`.
3. `serbia.pmtiles` is the default operational basemap; Monitoring can toggle to `europe.pmtiles`.
4. `promap-dark.json` is now wired for local glyphs and local PMTiles archive substitution.
5. Routing and geocoding configuration still exist separately from the basemap stack and should be evaluated independently when changing backend dependencies.

## Recommended next steps
1. Verify Navigation, Dispatch, and Monitoring in the browser against local `serbia.pmtiles` and `europe.pmtiles` files.
2. Update `README.md` so the operational documentation fully matches the local-only frontend asset strategy.
3. Decide whether OSRM and geocoding should also be localized by default, since the basemap/frontend runtime is now self-hosted.
4. Add automated tests for map page initialization and local asset availability where practical.
