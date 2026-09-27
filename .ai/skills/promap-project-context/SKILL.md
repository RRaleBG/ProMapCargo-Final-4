# ProMap Cargo — Project Context

## Scope
Treat `D:\Projects\ProMapCargo-Final-4` as the workspace root.

## Architecture
- .NET 10 Blazor Web App / Interactive Server
- Web, Api, Core, Contracts, Infrastructure, UnitTests, IntegrationTests
- MapLibre GL JS + PMTiles + GeoJSON route layers
- PostGIS is the preferred truck-aware routing graph
- OSRM is the routing fallback
- Docker services: `promap-api`, `promap-postgres`, `promap-osrm`

## Routing contract
Use `Routing:PreferPostGis`, `Routing:OsrmBaseUrl`, `Routing:MaxExpandedStates`, and `Routing:SnapRadiusMeters` as the routing configuration.
The API should prefer local PostGIS when the graph can serve the request and fall back to OSRM when local routing cannot safely/efficiently answer it.

## Map contract
Important files:
- `promap-map-layers.js`
- `navigation.js`
- `promap-map-enhancements.js`
- `promap-dark.json`

Required route sources/layers must remain internally consistent:
- `route-main`
- `route-alternative`
- `route-restrictions`
- `route-warnings`

Do not introduce a second map instance or duplicate MapLibre source registration. Preserve one MapLibre map, one PMTiles source, and GeoJSON route sources.

## Safety rules
Never commit secrets, `.env`, generated OSM PBF, OSRM generated data, `/maps`, or `/data`.
Respect `.gitignore`: OSM raw data and OSRM generated files are intentionally excluded.
Run a build after structural changes and inspect browser-console errors after MapLibre changes.
