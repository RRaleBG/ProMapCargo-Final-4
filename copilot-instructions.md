# Copilot Instructions

## Repository overview
- Solution: `ProMapCargo.sln`
- Main web app: `ProMapCargo.Api.csproj`
- Importer: `Importer/ProMapCargo.OsmImporter.csproj`
- Target framework in the current workspace: `.NET 10`
- UI stack: Razor Pages + controllers + SignalR
- Mapping stack on `Pages/Navigation/Index.cshtml`: Leaflet for interaction, MapLibre for the local PMTiles basemap overlay, PMTiles as the vector tile archive format
- Data stack: PostgreSQL + PostGIS
- Routing stack: PostGIS-first routing with OSRM fallback

## Important architecture notes
- The web app and importer use the same PostgreSQL database (`promapcargo`).
- Static map assets live under `wwwroot/`.
- PMTiles archives are expected under `wwwroot/maps/` and are served by `Program.cs` through `/maps/{region}.pmtiles`.
- The local vector style file is `wwwroot/styles/promap-dark.json`.
- The Navigation page is wired from `Pages/Navigation/Index.cshtml` to `wwwroot/js/navigation.js`.

## Confirmed findings from repo analysis
1. The Navigation page local basemap bootstrap in `wwwroot/js/navigation.js` used incorrect URL paths with a `wwwroot/` prefix. Static web assets in ASP.NET Core must be requested from the site root, for example `/lib/...`, `/styles/...`, `/maps/...`.
2. The same Navigation script attempted to import MapLibre from the UNPKG CDN, even though the repo already contains a local asset strategy and local PMTiles support. This made the local basemap dependent on a CDN path instead of the local app assets.
3. `scripts/build-serbia-pmtiles.sh` wrote generated PMTiles output into `wwwroot/styles/`, but runtime serving expects PMTiles files in `wwwroot/maps/`. This mismatch can leave the app serving stale or missing archives.
4. `scripts/build-serbia-pmtiles.ps1` already targets `wwwroot/maps/`, so the shell script was inconsistent with the PowerShell script.
5. `Program.cs` correctly serves `/maps/{region}.pmtiles` with range processing and a PMTiles MIME type.
6. `Controllers/LocalMapTilesController.cs` exposes `[Route("styles")]` and returns `maps/serbia.pmtiles`. That route shape is inconsistent with the generic `/maps/{region}.pmtiles` endpoint in `Program.cs` and appears redundant.
7. `appsettings.json` sets `Routing:OsrmBaseUrl` to `http://osrm:5000`, which works in Docker Compose but not when the API is started directly from Visual Studio on the host. For host-run debugging, the exposed OSRM port is `http://localhost:5001` from `docker-compose.yml`.
8. `Pages/Shared/_Layout.cshtml` still references CDN-hosted Leaflet and SignalR assets, while the repo also contains scripts intended to prepare local frontend assets. This is a deployment/offline consistency gap.
9. `README.md` still describes the app as ASP.NET Core 9, while the current workspace context says the projects target `.NET 10`.
10. No dedicated test project was found in the solution. Validation currently depends on build/runtime verification.

## Files that matter most for map/navigation work
- `Pages/Navigation/Index.cshtml`
- `wwwroot/js/navigation.js`
- `wwwroot/styles/promap-dark.json`
- `Program.cs`
- `Controllers/RoutingController.cs`
- `Services/OsrmRoutingService.cs`
- `docker-compose.yml`
- `appsettings.json`
- `scripts/build-serbia-pmtiles.ps1`
- `scripts/build-serbia-pmtiles.sh`
- `scripts/prepare-local-map-assets.ps1`
- `scripts/prepare-local-map-assets.sh`

## Rules for future changes
- Keep Navigation map asset URLs root-relative. Do not use `wwwroot/` in browser-facing URLs.
- Prefer the existing local asset flow for map libraries when working on the PMTiles basemap.
- Keep PMTiles generation scripts aligned with runtime serving paths.
- Reuse the generic `/maps/{region}.pmtiles` endpoint unless there is a clear reason to keep a controller-specific map route.
- When running locally outside Docker, verify that all service base URLs point to host-reachable addresses.
- Keep changes minimal and follow the existing Razor Pages + controller split.

## Recommended next steps
1. Verify the Navigation page in the browser after the local asset path fix and confirm the Serbia PMTiles basemap now renders.
2. Decide whether local host debugging should use `Routing:OsrmBaseUrl=http://localhost:5001` by default, or whether that value should move to `appsettings.Development.json`.
3. Remove or consolidate `Controllers/LocalMapTilesController.cs` if the generic `Program.cs` PMTiles endpoint is the intended long-term path.
4. Decide whether to fully switch `_Layout.cshtml` to local Leaflet and SignalR assets for offline/self-contained deployments.
5. Update `README.md` so the documented runtime/framework version matches the actual workspace target framework.
6. Add at least one automated test project for the web app, starting with routing/service tests around Navigation and OSRM/PostGIS selection logic.
7. Consider adding a lightweight startup/runtime health check for map assets, PMTiles availability, and OSRM reachability.
