# ProMap Cargo — Truck Routing

## Goal
Maintain truck-aware routing with a deterministic PostGIS → OSRM fallback strategy.

## Decision order
1. Validate coordinates and request.
2. Apply truck profile/preset, ADR/hazmat, restrictions, dimensions and weight.
3. Attempt PostGIS/local graph when `PreferPostGis=true`.
4. Verify that the local graph can produce a valid route and that expansion limits are respected.
5. If local routing is unavailable, incomplete, or exceeds configured limits, call OSRM.
6. Return one normalized route contract to the UI and expose the selected engine in diagnostics/logging.

## Never
- silently return an empty route
- require the user to download the whole Europe PBF just to use OSRM fallback
- mix route response formats between PostGIS and OSRM
- hide a fallback behind client-side routing logic

## Relevant endpoints
- `POST /api/routing/route`
- `GET /api/health`
- `GET /api/maps/config`
- `GET /api/geocoding/search`

## Validation
Check `RouteRequest` and related contracts before changing controller/service code. Keep truck, target, departure/profile fields aligned across Contracts, API and JS.
