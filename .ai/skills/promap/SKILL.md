# ProMap Cargo Project Skill

## Project

ProMap Cargo

## Architecture

Frontend:

- Razor Pages
- JavaScript
- Tailwind CSS
- MapLibre GL
- PMTiles

Backend:

- ASP.NET Core
- C#
- Entity Framework Core
- PostgreSQL
- PostGIS

Routing:

- local PostGIS graph
- truck-aware routing
- OSRM fallback

Maps:

- MapLibre
- PMTiles
- GeoJSON route sources

## Routing

Preferred routing engine:

PostGIS

Fallback:

OSRM

Configuration:

Routing:PreferPostGis
Routing:OsrmBaseUrl
Routing:MaxExpandedStates
Routing:SnapRadiusMeters

## Map sources

Known route sources:

route-main
route-alternative
route-restrictions
route-warnings

## Map style

Main style:

promap-dark.json

Map implementation:

promap-map-layers.js

## Important rule

Never create another map implementation when an existing MapLibre implementation exists.

Never introduce Leaflet.

Never duplicate route sources.

Never duplicate MapLibre initialization.

## Debugging

When a map does not render:

1. Check MapLibre initialization.
2. Check style JSON.
3. Check sources.
4. Check layers.
5. Check PMTiles URL.
6. Check browser console.
7. Check network requests.
8. Check CSP.
9. Check route GeoJSON.
10. Check layer/source ordering.

When routing fails:

1. Validate coordinates.
2. Inspect RouteRequest.
3. Inspect controller.
4. Inspect routing service.
5. Check PostGIS.
6. Check OSRM.
7. Check fallback logic.
8. Check browser request.
9. Check server logs.
10. Identify the first real failure.

Do not treat downstream errors as the root cause.
