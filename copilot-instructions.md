# ProMap Cargo — GitHub Copilot Instructions

## 1. PROJECT IDENTITY

ProMap Cargo is a truck-aware fleet operations, dispatch, transport management and navigation platform.

The application is a server-rendered ASP.NET Core application with Razor Pages, API controllers, PostgreSQL/PostGIS, an OSM-derived routing graph, OSRM fallback routing, local PMTiles vector maps, Leaflet, MapLibre, SignalR telemetry, ASP.NET Identity and Docker.

The primary goal is a production-oriented logistics platform for truck routing and fleet operations.

Do not treat this repository as a generic CRUD application.

The routing engine, map infrastructure, OSM graph, truck restrictions, PMTiles delivery and Navigation UI are core functionality.

---

# 2. SOURCE OF TRUTH

Before changing code:

1. Inspect the existing implementation.
2. Follow the current repository architecture.
3. Reuse existing services, models, helpers and infrastructure.
4. Do not invent parallel implementations when an existing implementation already provides the required functionality.
5. Do not replace working infrastructure merely because another approach is easier.
6. Do not silently change API contracts.
7. Do not silently change database schema.
8. Do not silently replace local map infrastructure with external map providers.
9. Do not assume files that are ignored by Git do not exist locally.
10. Do not assume that a file visible in the local workspace is present in GitHub.

Large map datasets are intentionally excluded from Git.

The following may exist locally but are intentionally not committed:

* OSM PBF files
* PMTiles archives
* OSRM generated datasets
* local map datasets
* database volumes
* temporary importer files
* generated routing data

Check `.gitignore` before concluding that a missing GitHub file is missing from the actual application.

---

# 3. CURRENT TECHNOLOGY STACK

Use the technology versions already defined by the repository.

Current main application target:

* .NET 10
* ASP.NET Core
* Razor Pages
* MVC/API Controllers
* SignalR
* Entity Framework Core 10
* PostgreSQL
* PostGIS
* Npgsql
* NetTopologySuite
* Dapper
* Itinero where already used
* Leaflet
* MapLibre GL
* PMTiles
* local OpenMapTiles/Protomaps-compatible glyphs
* Docker
* Docker Compose
* GitHub Actions

Do not downgrade the project to .NET 9 or another framework version unless explicitly requested.

If documentation says ASP.NET Core 9 but the `.csproj` targets .NET 10, the `.csproj` is authoritative for the implementation.

Documentation should be corrected to match the actual project.

---

# 4. REPOSITORY STRUCTURE

Important directories and responsibilities:

## Application

* `Program.cs`

  * application bootstrap
  * dependency injection
  * middleware
  * static files
  * CSP
  * routing
  * Razor Pages
  * SignalR
  * PMTiles serving
  * database bootstrap

* `ProMapCargo.Api.csproj`

  * main web application
  * .NET version
  * NuGet dependencies

* `ProMapCargo.sln`

  * solution entry point

## Controllers

`Controllers/`

Contains HTTP API endpoints.

Important controllers include:

* `RoutingController.cs`
* `TripRoutingController.cs`
* `GeocodingController.cs`
* `NavigationTelemetryController.cs`
* `RestrictionsController.cs`
* `RestrictionAdminController.cs`
* `OperationsController.cs`
* `BusinessController.cs`
* `DriverController.cs`
* `AlertsController.cs`

Controllers must remain thin.

Business logic belongs in services/repositories, not duplicated inside controllers.

---

# 5. RAZOR PAGES

The UI is Razor Pages based.

Important pages:

* `Pages/Index.cshtml`
* `Pages/Dispatch/Index.cshtml`
* `Pages/Navigation/Index.cshtml`
* `Pages/Monitoring/Index.cshtml`
* `Pages/Orders/Index.cshtml`
* `Pages/Trips/Index.cshtml`
* `Pages/Vehicles/Index.cshtml`
* `Pages/Drivers/Index.cshtml`
* `Pages/Alerts/Index.cshtml`
* `Pages/Compliance/Index.cshtml`
* `Pages/Finance/Index.cshtml`
* `Pages/Reports/Index.cshtml`
* `Pages/Audit/Index.cshtml`
* `Pages/Moderation/Index.cshtml`
* `Pages/Settings/Index.cshtml`
* `Pages/Profile/Index.cshtml`
* `Pages/Admin/Index.cshtml`

Shared layout:

* `Pages/Shared/_Layout.cshtml`

Do not replace Razor Pages with a SPA framework.

Do not create React, Vue, Angular or Blazor infrastructure unless explicitly requested.

---

# 6. NAVIGATION PAGE IS A CRITICAL SYSTEM

The Navigation page is not a mock/demo page.

Primary files:

* `Pages/Navigation/Index.cshtml`
* `wwwroot/js/navigation.js`
* `wwwroot/js/promap-routing.js`
* `wwwroot/js/promap-maneuvers.js`
* `wwwroot/js/promap-gps.js`
* `wwwroot/js/promap-map-enhancements.js`
* `wwwroot/js/promap-map-layers.js`
* `wwwroot/js/promap-pmtiles-init.js`

Navigation must support:

* start location
* destination
* geocoding
* coordinate validation
* truck profile
* truck dimensions
* weight
* axle load
* axles
* maximum speed
* hazardous materials / ADR
* restricted road avoidance
* route calculation
* PostGIS truck-aware routing
* OSRM fallback
* route alternatives where available
* route geometry
* route summary
* distance
* duration
* ETA
* truck safety status
* warnings
* maneuvers
* live GPS
* off-route detection
* rerouting
* SignalR telemetry where applicable
* local map rendering

Do not reduce Navigation to only drawing two markers.

---

# 7. ROUTING ARCHITECTURE

Routing has two conceptual layers.

## Primary routing engine

PostGIS + OSM graph.

Relevant files include:

* `Routing/PostGisRoutingService.cs`
* `Routing/PostGisRoutingRepository.cs`
* `Routing/PostGisAStarRouter.cs`
* `Routing/EdgeSnapper.cs`
* `Routing/TruckEdgeEvaluator.cs`
* `Routing/TurnRestrictionMatcher.cs`
* `Routing/ManeuverBuilder.cs`

The primary truck profile must use the PostGIS graph whenever a valid active graph exists.

## Fallback routing engine

OSRM.

Relevant files:

* `Services/OsrmRoutingService.cs`
* `Services/IRoutingService.cs`

The API must gracefully fall back to OSRM when:

* no active PostGIS graph exists
* PostGIS routing fails
* graph snapping fails
* graph routing produces no route
* another recoverable PostGIS routing error occurs

Do not remove the fallback merely because PostGIS is the preferred engine.

---

# 8. ROUTING ENGINE SELECTION

Current intended behavior:

For truck routing:

1. Validate coordinates.
2. Normalize the request.
3. Attempt PostGIS routing.
4. If PostGIS produces a valid route, return it.
5. If PostGIS cannot route, fall back to OSRM.
6. Clearly identify the engine in diagnostics.
7. Never silently claim that OSRM is truck-aware.

For non-truck profiles, use the existing routing contract.

Do not claim that standard OSRM `driving` is equivalent to truck-aware routing.

OSRM fallback is a compatibility/fallback route, not a replacement for the PostGIS truck graph.

---

# 9. ROUTE REQUEST CONTRACT

The canonical route request model is:

`Models/RouteRequest.cs`

Current properties include:

* `Start`
* `Destination`
* `End`
* `Profile`
* `AvoidRestricted`
* `Truck`
* `DepartureAt`

`Target` is a computed compatibility property resolving:

`Destination ?? End`

Do not introduce `Target` as a second JSON request field.

Do not break backward compatibility between `destination` and `end` without explicit approval.

When changing the route contract:

1. update the model
2. update the controller
3. update PostGIS routing
4. update OSRM routing
5. update Navigation JavaScript
6. update documentation
7. search the entire repository for old property names
8. build the complete solution

Never fix a compile error by merely renaming one property in one file.

---

# 10. COORDINATE CONTRACT

All geographic coordinates use:

* latitude
* longitude

Internal GeoJSON coordinates use:

* longitude
* latitude

This distinction is critical.

`GeoPoint` represents:

* `Lat`
* `Lon`

GeoJSON LineString coordinates must be:

`[longitude, latitude]`

Never reverse these silently.

Every routing endpoint must validate:

* latitude is finite
* longitude is finite
* latitude is between -90 and 90
* longitude is between -180 and 180

Do not accept zero coordinates as a valid real-world route unless explicitly intended.

When a UI has text locations such as:

`Beograd, Srbija`

the browser must geocode them before routing.

Never send the literal text value as coordinates.

---

# 11. TRUCK PROFILE

Truck routing must respect the existing `TruckProfile`.

Relevant fields include:

* gross weight
* height
* width
* length
* axle load
* number of axles
* maximum speed
* ADR/hazmat information where supported

Default heavy-truck profile currently used by the routing system is approximately:

* 40 t
* 4.0 m height
* 2.55 m width
* 16.5 m length

Do not hard-code truck values in multiple locations.

If defaults change, centralize them.

Truck restrictions must be evaluated against graph edge metadata.

---

# 12. TRUCK RESTRICTION LOGIC

Truck-aware routing must consider OSM-derived restrictions such as:

* access
* vehicle
* motor_vehicle
* hgv
* goods
* hazmat
* maxheight
* maxwidth
* maxlength
* maxweight
* maxaxleload
* maxspeed
* maxspeed:hgv
* turn restrictions

Relevant components:

* `TruckEdgeEvaluator`
* `IRestrictionEngine`
* `PostgresRestrictionEngine`
* `PostgresRestrictionRepository`
* `TurnRestrictionMatcher`

Do not bypass the evaluator and manually accept all road edges.

Do not treat every OSM road as truck-routable.

---

# 13. POSTGIS GRAPH

The routing graph is versioned.

Important tables include:

* `routing_graph_versions`
* `osm_nodes`
* `osm_ways`
* `osm_way_nodes`
* `road_edges`
* `turn_restrictions`
* `compiled_turn_restrictions`
* routing partition/cell tables

Relevant SQL:

* `Sql/03-routing-graph.sql`
* `Sql/04-operational-indexes.sql`

A graph version must only become active after successful import.

The router should select the latest ready/activated graph version.

Never route against an incomplete `building` graph.

Never delete the currently active graph before a replacement graph has successfully imported.

---

# 14. OSM IMPORTER

The OSM importer is a separate executable.

Project:

`Importer/ProMapCargo.OsmImporter.csproj`

Important files:

* `Importer/Program.cs`
* `Importer/GraphImporter.cs`
* `Importer/ImportDbContext.cs`
* `Importer/OsmRestrictionImporter.cs`

The importer reads OSM PBF data and populates the PostGIS routing graph.

Typical source:

`osm/*.osm.pbf`

Do not commit large OSM PBF datasets.

Do not implement map rendering inside the importer.

Do not mix application UI logic into importer code.

---

# 15. MAP ARCHITECTURE

The frontend uses a local map architecture.

The application must remain independent of remote CDN-hosted frontend runtime dependencies.

Local frontend runtime assets are under:

* `wwwroot/lib`
* `wwwroot/fonts`
* `wwwroot/styles`
* `wwwroot/maps`

The repository contains local:

* Leaflet
* MapLibre
* PMTiles
* SignalR
* glyph assets

Do not replace local assets with CDN URLs unless explicitly requested.

Do not introduce remote OSM/CARTO/TomTom/Esri basemap tiles when the existing local PMTiles architecture can provide the required map.

---

# 16. PMTILES

PMTiles is the local vector map distribution mechanism.

Expected runtime paths:

* `/maps/serbia.pmtiles`
* `/maps/europe.pmtiles`

The API exposes:

`/maps/{region}.pmtiles`

through `Program.cs`.

The physical files are expected under:

`wwwroot/maps`

Do not hard-code a filesystem path into browser JavaScript.

Browser URLs must be root-relative:

`/maps/serbia.pmtiles`

not:

`wwwroot/maps/serbia.pmtiles`

and not:

`C:\...`

---

# 17. LARGE MAP FILES

Large map files are intentionally not stored in Git.

Examples:

* `*.osm.pbf`
* `*.osm`
* `*.osrm`
* `*.osrm.*`
* `*.pmtiles`
* generated map datasets

If GitHub does not contain `wwwroot/maps/serbia.pmtiles`, do not conclude that the application is broken.

First inspect:

* `.gitignore`
* Docker volume mounts
* local filesystem
* map generation scripts
* startup serving code
* browser request path

A missing large file from GitHub can be intentional.

---

# 18. MAP GENERATION

Relevant scripts include:

* `scripts/prepare-local-map-assets.ps1`
* `scripts/prepare-local-map-assets.sh`
* `scripts/build-serbia-pmtiles.ps1`
* `scripts/build-serbia-pmtiles.sh`
* `scripts/apply-local-map.ps1`

When changing PMTiles generation:

1. preserve the input OSM PBF workflow
2. preserve output location
3. preserve `/maps/{region}.pmtiles`
4. preserve range requests
5. preserve PMTiles MIME type
6. preserve local glyph paths
7. update both Windows PowerShell and shell scripts where applicable

Do not update only one platform script if both are maintained.

---

# 19. MAPLIBRE STYLE

Primary style:

`wwwroot/styles/promap-dark.json`

The style must work with:

* local PMTiles
* local glyphs
* local MapLibre runtime

Do not replace local glyphs with remote glyph URLs without explicit approval.

Do not hard-code the PMTiles URL in a way that prevents switching between Serbia and Europe.

Use the existing substitution/configuration mechanism.

---

# 20. LEAFLET VS MAPLIBRE

The application intentionally uses both technologies.

Leaflet:

* interaction
* markers
* map controls
* navigation overlays where applicable

MapLibre:

* vector basemap
* local PMTiles rendering
* labels
* styled vector map layers

Do not remove one library simply because the other is already present.

Do not create duplicate map engines inside the same page.

Reuse the existing map-layer infrastructure.

---

# 21. SHARED MAP HELPER

Primary shared helper:

`wwwroot/js/promap-map-layers.js`

Use it whenever possible.

Do not create a new basemap implementation in:

* Navigation
* Dispatch
* Monitoring

unless the existing helper genuinely cannot support the requirement.

If a new map source is needed, first extend the shared infrastructure.

---

# 22. MAP INITIALIZATION

When debugging a blank map, inspect the full chain:

1. HTML map container exists.
2. Map container has non-zero height.
3. Leaflet/MapLibre scripts loaded.
4. PMTiles library loaded.
5. PMTiles archive URL is correct.
6. Browser requests the archive.
7. HTTP response is successful.
8. Range requests work.
9. style JSON loads.
10. glyphs load.
11. PMTiles source initializes.
12. layers become visible.
13. map is resized after layout initialization.

Do not assume a blank map means the routing API is broken.

Map rendering and route calculation are separate subsystems.

---

# 23. NAVIGATION MAP DEBUGGING

If the Navigation page displays start/end markers but no route line:

Check in this order:

1. browser console
2. Network request to `/api/routing/route`
3. request JSON
4. HTTP status
5. response JSON
6. `Routes`
7. `SelectedRouteIndex`
8. `Geometry`
9. GeoJSON coordinate order
10. route layer creation
11. map fit operation
12. map style visibility

Do not immediately rewrite the map.

The routing API and route rendering must be diagnosed independently.

---

# 24. ROUTE GEOMETRY CONTRACT

Route geometry is GeoJSON-compatible.

Expected structure:

```json
{
  "type": "LineString",
  "coordinates": [
    [20.4573, 44.8178],
    [20.5, 44.9]
  ]
}
```

The first number is longitude.

The second number is latitude.

Never emit:

```text
[latitude, longitude]
```

for GeoJSON.

---

# 25. ROUTE RESPONSE

Use the existing:

`Models/RouteResponse.cs`

The response can contain:

* code
* routes
* selected route index
* truck safety
* violations
* summary
* diagnostics
* maneuvers

Diagnostics must identify:

* routing engine
* fallback status
* graph version where available
* expanded states
* failure reason where applicable

Never report `Engine = "PostGIS-AStar"` when OSRM actually produced the route.

Never report `UsedFallback = false` for an OSRM fallback.

---

# 26. OSRM

OSRM is a fallback/compatibility routing engine.

Configured internally through:

`Routing:OsrmBaseUrl`

Docker normally provides:

`http://osrm:5000`

A public OSRM fallback may exist through:

`Routing:OsrmFallbackBaseUrl`

Do not assume public OSRM has truck restrictions.

Do not expose public OSRM as equivalent to the internal truck-aware PostGIS router.

If public OSRM is used, clearly identify it as fallback behavior.

---

# 27. GEOCODING

Geocoding currently uses Nominatim-compatible infrastructure.

Relevant files:

* `Services/NominatimGeocodingService.cs`
* `Controllers/GeocodingController.cs`
* `Models/GeocodingResult.cs`

Do not confuse geocoding with routing.

Geocoding converts:

`"Beograd, Srbija"`

into coordinates.

Routing consumes coordinates.

The Navigation UI must preserve this separation.

---

# 28. DATABASE

There is one primary PostgreSQL/PostGIS database:

`promapcargo`

The API and OSM importer use the same database.

Do not create a second routing database unless explicitly requested.

Database technologies:

* PostgreSQL
* PostGIS
* EF Core
* Npgsql
* NetTopologySuite
* Dapper

Use EF Core for application/domain persistence where appropriate.

Use Dapper/Npgsql where the existing routing graph implementation requires efficient spatial/graph queries.

Do not rewrite the graph repository into EF Core simply for consistency.

---

# 29. DATABASE NAMING

The application uses PostgreSQL snake_case naming.

Maintain:

* snake_case table names
* snake_case columns

Do not introduce mixed naming conventions.

If schema changes are necessary:

1. update SQL
2. update EF model/configuration
3. update repository queries
4. update importer
5. update seed logic
6. update documentation

---

# 30. DATABASE BOOTSTRAP

`Program.cs` performs startup database bootstrap.

Current behavior includes:

* database connectivity check
* EF database creation
* routing graph SQL execution
* operational index SQL execution
* role initialization

Do not make startup fail merely because the optional routing graph is not imported.

The application should be able to start without an active routing graph and then use OSRM fallback.

However, do not hide programming/database errors during development.

Log bootstrap failures clearly.

---

# 31. SEED DATA

Development seed data may create:

* company
* administrator
* roles
* vehicles
* drivers
* transport orders
* stops
* trips
* restrictions

Development administrator defaults must never be treated as production credentials.

Never add real secrets to source control.

Never copy credentials from `appsettings.json` into production documentation.

---

# 32. DOCKER

Docker is a first-class supported deployment mode.

Current services include:

* `postgres`
* `osrm`
* `api`

Expected internal service names include:

* `postgres`
* `osrm`

Inside Docker:

* API must connect to `postgres`, not `localhost`
* API must connect to OSRM using `osrm:5000`

`localhost` inside the API container refers to the API container itself.

Never change Docker internal connection strings to `localhost`.

---

# 33. DOCKER MAP MOUNTS

The Docker API service mounts local map assets.

Important locations:

* `./wwwroot/maps:/app/wwwroot/maps`
* `./wwwroot/styles:/app/wwwroot/styles`

Do not remove these mounts when modifying Docker.

If a map works locally but not in Docker, inspect:

1. mount
2. host file
3. container file
4. API route
5. MIME type
6. range requests
7. browser URL

---

# 34. STATIC FILES

`Program.cs` configures custom MIME types for:

* `.pmtiles`
* `.pbf`

Do not remove these mappings.

PMTiles requires range processing.

The PMTiles endpoint must support HTTP range requests.

Do not replace:

`enableRangeProcessing: true`

with a simple full-file response.

---

# 35. CONTENT SECURITY POLICY

The application defines a CSP.

When adding browser functionality:

* prefer local scripts
* prefer local styles
* prefer local workers
* preserve `worker-src`
* preserve `blob:` where MapLibre requires it
* do not blindly disable CSP

If a CSP change is necessary, make it narrowly scoped.

Do not solve frontend errors by setting:

`Content-Security-Policy: *`

---

# 36. FRONTEND ASSET RULE

Browser-facing paths must be root-relative.

Correct:

```text
/lib/leaflet/leaflet.js
/js/navigation.js
/maps/serbia.pmtiles
/styles/promap-dark.json
/fonts/Noto Sans Regular/0-255.pbf
```

Incorrect:

```text
wwwroot/lib/leaflet/leaflet.js
wwwroot/maps/serbia.pmtiles
C:\project\wwwroot\maps\serbia.pmtiles
```

Server filesystem paths and browser URLs are different concepts.

---

# 37. JAVASCRIPT ARCHITECTURE

Frontend JavaScript is modularized.

Important modules:

* `wwwroot/js/app.js`
* `wwwroot/js/navigation.js`
* `wwwroot/js/promap-routing.js`
* `wwwroot/js/promap-gps.js`
* `wwwroot/js/promap-maneuvers.js`
* `wwwroot/js/promap-map-enhancements.js`
* `wwwroot/js/promap-map-layers.js`
* `wwwroot/js/promap-pmtiles-init.js`
* `wwwroot/js/core/*`
* `wwwroot/js/services/*`

Do not put all functionality into `app.js`.

Do not create duplicate global functions with the same name.

Prefer the existing `window.ProMap` namespace where appropriate.

---

# 38. JAVASCRIPT INITIALIZATION

Navigation initialization must be idempotent.

The existing application uses initialization guards such as:

`window.__promapNavigationInitialized`

Preserve this behavior.

Do not initialize the same map twice.

Do not attach duplicate click handlers every time a script executes.

---

# 39. RAZOR + JAVASCRIPT CONTRACT

IDs and `data-*` attributes in `Pages/Navigation/Index.cshtml` are API-like contracts between Razor and JavaScript.

Before changing an element ID:

1. search all JavaScript
2. search all CSS
3. search all Razor files
4. update all references

Do not rename:

* `navMap`
* `navStart`
* `navEnd`
* route buttons
* route summary elements
* maneuver containers
* GPS elements

without checking all references.

---

# 40. UI DESIGN

The application has an established ProMap Cargo dark fleet-operations visual language.

Preserve:

* dark operational UI
* green/teal accent system
* high information density
* clear hierarchy
* truck/fleet-oriented terminology
* responsive layout
* accessible controls

Do not introduce random colors or unrelated design systems.

Do not replace the existing visual system with Bootstrap defaults.

---

# 41. RESPONSIVE DESIGN

Navigation and command-center pages must remain usable on:

* desktop
* laptop
* tablet
* mobile

Do not assume a fixed desktop width.

When changing map layouts, ensure the map container receives a valid height.

If the map is inside a hidden/collapsed container, call the appropriate map resize/invalidate mechanism after it becomes visible.

---

# 42. SIGNALR

Navigation telemetry uses SignalR.

Important endpoint:

`/hubs/navigation`

Relevant files include:

* `Services/NavigationHub.cs`
* `Controllers/NavigationTelemetryController.cs`
* `wwwroot/lib/signalr/signalr.min.js`

Do not replace SignalR with polling unless explicitly requested.

Telemetry must not block basic route calculation.

Live GPS should degrade gracefully if SignalR or browser geolocation is unavailable.

---

# 43. GPS

Browser GPS is optional.

The application must still allow:

* manual start location
* geocoded start location
* normal routing

if GPS is unavailable.

Never assume `navigator.geolocation` exists.

Handle:

* permission denied
* unavailable position
* timeout
* inaccurate position

without crashing the Navigation page.

---

# 44. OFF-ROUTE / REROUTING

Live navigation can detect deviation from the current route.

When modifying rerouting:

* debounce reroutes
* preserve `lastRerouteAt`
* avoid routing loops
* do not issue requests continuously
* preserve current route until a valid replacement exists
* distinguish temporary GPS drift from actual off-route movement

Do not reroute on every GPS update.

---

# 45. MANEUVERS

Maneuver generation is a backend concern where possible.

Relevant file:

`Routing/ManeuverBuilder.cs`

Frontend maneuver rendering belongs in:

`wwwroot/js/promap-maneuvers.js`

Do not duplicate complicated maneuver inference independently in the browser if the backend already provides maneuvers.

---

# 46. API ERROR CONTRACT

API errors should be structured.

Examples include:

* `InvalidStart`
* `InvalidDestination`
* `RoutingUnavailable`
* `NoRoute`
* `NoGraph`
* `NoSnap`
* `OsrmError`

Do not replace structured API errors with plain text.

Frontend code must display useful user-facing messages while preserving diagnostic information in the console/logs.

Do not expose sensitive exception details in production responses.

---

# 47. LOGGING

Use structured logging.

Prefer:

```csharp
logger.LogInformation(
    "Routing request: {Profile} {StartLat},{StartLon} -> {EndLat},{EndLon}",
    profile,
    start.Lat,
    start.Lon,
    end.Lat,
    end.Lon);
```

Do not use large `Console.WriteLine` debugging blocks in production code.

Do not log:

* passwords
* secrets
* access tokens
* private credentials
* unnecessary personal data

---

# 48. PERFORMANCE

Routing is performance-sensitive.

Do not:

* load the entire road graph into memory
* query every edge without spatial filtering
* serialize massive datasets unnecessarily
* perform N+1 database queries
* parse WKT repeatedly when avoidable
* calculate routes on every keystroke
* download entire PMTiles archives when range access is possible

Use:

* spatial indexes
* graph version filtering
* edge snapping
* bounded A* expansion
* efficient SQL
* Dapper/Npgsql where appropriate
* browser caching where safe

---

# 49. POSTGIS ROUTING PERFORMANCE

The A* router has an expansion limit.

Configured value:

`Routing:MaxExpandedStates`

Do not silently remove this safety limit.

If the limit is reached:

* return a clear diagnostic
* allow fallback where appropriate
* do not hang indefinitely

Snap radius is configurable through:

`Routing:SnapRadiusMeters`

Do not arbitrarily increase it to hide coordinate problems.

---

# 50. GRAPH VERSIONING

The graph is protected by versioning.

Never:

* route against an unfinished graph
* activate a failed graph
* destroy the active graph during import
* mutate active graph data destructively

Importer flow should conceptually be:

1. create graph version
2. status = building
3. import nodes
4. import ways
5. import edges
6. import restrictions
7. compile restrictions where applicable
8. validate
9. mark ready
10. activate only after success

---

# 51. TURN RESTRICTIONS

Turn restrictions are stateful.

Do not treat routing state as only:

`node`

The router may need:

* current node
* previous edge/way
* turn restriction context

Do not remove previous-edge state from the A* search merely to simplify code.

---

# 52. DATABASE SQL

SQL files are part of the application architecture.

Important:

* `Sql/01-indexes.sql`
* `Sql/02-useful-queries.sql`
* `Sql/03-routing-graph.sql`
* `Sql/04-operational-indexes.sql`

Do not modify SQL schema without checking:

* importer
* routing repository
* EF Core
* seed
* tests
* documentation

---

# 53. IMPORTER AND APPLICATION MUST AGREE

The importer and API share the same graph schema.

Any change to:

`road_edges`

must be reviewed against:

* `GraphImporter.cs`
* `PostGisRoutingRepository.cs`
* `TruckEdgeEvaluator.cs`
* `PostGisAStarRouter.cs`
* SQL schema

Do not modify one side independently.

---

# 54. DOCUMENTATION CONSISTENCY

Documentation must describe the actual implementation.

If code says .NET 10, documentation must not say .NET 9.

If maps are local PMTiles, documentation must not describe remote basemap tiles as the primary architecture.

If routing is PostGIS-first with OSRM fallback, documentation must not describe OSRM as the primary truck routing engine.

When architecture changes, update:

* `README.md`
* relevant scripts
* Docker documentation
* Copilot instructions if necessary

---

# 55. TESTING REQUIREMENTS

Before declaring a change complete, perform the strongest available validation.

At minimum:

```bash
dotnet restore ProMapCargo.sln
dotnet build ProMapCargo.sln
```

When Docker-related code changes:

```bash
docker compose config
docker compose build
```

When practical:

```bash
docker compose up
```

Then verify:

* API starts
* PostgreSQL connects
* Razor Pages load
* Navigation page loads
* static assets load
* PMTiles endpoint responds
* route API responds
* OSRM fallback works when PostGIS graph is unavailable

Do not claim a build is successful without actually building when the environment allows it.

---

# 56. MAP VERIFICATION

When changing map code, verify the actual browser/network chain.

Check:

```text
GET /maps/serbia.pmtiles
GET /styles/promap-dark.json
GET /fonts/Noto Sans Regular/...
```

Check for:

* 200 responses
* 206 range responses where expected
* no 404
* no CSP violation
* no CORS failure
* no worker failure
* no invalid style source
* no missing glyphs

Do not mark a map issue fixed only because the JavaScript has no syntax error.

---

# 57. ROUTING VERIFICATION

For a standard test route:

```text
Beograd -> Novi Sad
```

verify:

1. geocoding resolves both locations
2. coordinates are valid
3. POST `/api/routing/route` is sent
4. request contains `start`
5. request contains `destination`
6. profile is `truck`
7. truck parameters are present
8. API selects PostGIS if a graph is active
9. otherwise OSRM fallback is used
10. response contains at least one route
11. geometry contains valid LineString coordinates
12. frontend draws the route
13. map fits the route
14. summary is updated
15. diagnostics identify the engine

---

# 58. NEVER MASK ROOT CAUSES

Do not solve an error by:

* disabling validation
* returning fake coordinates
* drawing a straight line between start and destination
* hard-coding a demo route
* swallowing exceptions
* disabling CSP globally
* replacing the routing engine with a mock
* inserting fake PMTiles data
* returning HTTP 200 for a failed route
* hiding API failures in JavaScript

A temporary fallback is acceptable only when it is a real configured fallback such as OSRM.

---

# 59. NO FAKE FUNCTIONALITY

Do not create UI controls that appear functional but do nothing.

Every visible operational control should either:

* perform its intended operation
* be clearly disabled with an explanation
* be explicitly marked as unavailable

Do not leave placeholder buttons in production UI.

---

# 60. NO DUPLICATE ARCHITECTURES

Before adding:

* another routing service
* another map service
* another database context
* another geocoder
* another PMTiles loader
* another global JS state object

search the repository first.

Prefer extending the existing implementation.

---

# 61. SECURITY

Never commit:

* production passwords
* API keys
* private tokens
* credentials
* certificates
* private map licenses

Development credentials may exist only as clearly documented development defaults.

Production secrets must come from environment variables, secret stores or deployment configuration.

Do not weaken authentication or authorization to solve a development problem.

---

# 62. AUTHENTICATION / IDENTITY

The project uses ASP.NET Identity.

Do not bypass Identity by introducing a custom ad-hoc authentication mechanism.

Respect:

* users
* roles
* claims
* company/tenant context

Relevant infrastructure includes:

* `ProMapCargoDbContext`
* `IdentityClaimsFactory`
* `CurrentUserContext`

When adding an operational endpoint, determine whether it should be:

* anonymous
* authenticated
* role-restricted

Do not default every endpoint to anonymous access.

---

# 63. API COMPATIBILITY

Before changing an API:

Search for:

```text
/api/routing
/api/geocoding
/api/navigation
/hubs/navigation
```

and inspect both server and browser consumers.

A backend change is incomplete until the frontend consumer is compatible.

---

# 64. JSON CONTRACT

Existing JSON names use explicit `JsonPropertyName` attributes in important request models.

Preserve the established names.

Examples:

```text
start
destination
end
profile
avoidRestricted
truck
departureAt
```

Do not rename them to C# property names unless the API contract is intentionally changed.

---

# 65. C# STYLE

Use:

* nullable reference types
* implicit usings
* modern C#
* primary constructors where they improve clarity
* records for immutable DTOs where appropriate
* async/await
* cancellation tokens
* structured logging

Avoid unnecessary abstractions.

Do not refactor unrelated files during a focused bug fix.

---

# 66. ASYNC / CANCELLATION

HTTP/database operations should accept and propagate `CancellationToken`.

Do not use:

```csharp
CancellationToken.None
```

when a request token is available.

Do not block async operations with:

* `.Result`
* `.Wait()`

---

# 67. SQL SAFETY

Use parameters.

Do not concatenate user-controlled values into SQL.

Dynamic SQL is acceptable only when identifiers/clauses are controlled by the application.

Dapper queries must use parameter objects.

---

# 68. JAVASCRIPT STYLE

Use modern browser JavaScript.

Prefer:

* `const`
* `let`
* async/await
* explicit error handling
* small functions
* existing helper modules

Avoid:

* unnecessary jQuery
* new global variables
* inline duplicated API clients
* hidden magic coordinates

---

# 69. FRONTEND ERROR HANDLING

Frontend errors must distinguish:

* geocoding failure
* invalid coordinates
* routing failure
* no route
* map loading failure
* PMTiles failure
* GPS failure
* SignalR failure

Do not show:

`Routing API nije dostupan`

when the actual failure is a map asset 404.

Error messages should identify the subsystem.

---

# 70. MAP ERROR HANDLING

Map errors should contain enough diagnostics to answer:

* which URL was requested?
* which asset failed?
* was the failure HTTP, JavaScript, style or worker related?
* which map archive was selected?

Do not hide errors behind generic:

`Map failed`

messages.

---

# 71. NO EXTERNAL MAP PROVIDER BY DEFAULT

The current architecture intentionally uses local map assets.

Do not introduce:

* Google Maps
* Bing Maps
* TomTom
* Mapbox
* CARTO
* remote OSM tile servers

as a replacement for the local basemap unless explicitly requested.

External geocoding/routing services are separate concerns and do not justify replacing the local basemap.

---

# 72. LOCAL ASSET SCRIPTS

`prepare-local-map-assets.ps1` currently downloads frontend runtime dependencies into the repository.

This is a build/preparation tool, not runtime application behavior.

Do not add runtime CDN dependencies just because the preparation script downloads from a CDN.

The resulting assets are expected to be served locally.

---

# 73. GITIGNORE

Respect `.gitignore`.

Do not force-add large generated assets unless explicitly requested.

In particular:

* OSM PBF
* OSRM generated files
* PMTiles
* generated map data
* temporary files

should remain outside normal source control.

---

# 74. WHEN A FILE IS MISSING

Before concluding that a file is missing:

1. inspect Git tree
2. inspect `.gitignore`
3. inspect local filesystem if available
4. inspect Docker mounts
5. inspect generation scripts
6. inspect runtime serving path

For example, absence of:

`wwwroot/maps/serbia.pmtiles`

from Git does not automatically mean the application cannot load it.

---

# 75. WHEN A PAGE IS EMPTY

If a Razor page renders only a minimal placeholder:

1. inspect the actual `.cshtml`
2. inspect `_Layout.cshtml`
3. inspect route/page mapping
4. inspect referenced JavaScript
5. inspect CSS
6. inspect browser console
7. inspect network requests

Do not regenerate the entire project immediately.

Do not delete existing pages without proving they are obsolete.

---

# 76. WHEN COMPILATION FAILS

Always fix the underlying contract.

For example, if the compiler says:

```text
'RouteRequest' does not contain a definition for 'Truck'
```

do not blindly change all references.

First inspect:

* `RouteRequest.cs`
* all references to `.Truck`
* JSON contract
* controller
* routing services
* frontend request builder

Then determine whether:

* the model is stale
* consumers are stale
* the wrong branch/file is being built
* a namespace collision exists
* a duplicate model exists

The repository must have one authoritative `RouteRequest`.

---

# 77. BRANCH / VERSION CONFUSION

If code appears inconsistent:

Check:

* current branch
* current commit
* repository tree
* `.csproj`
* duplicate files
* generated output
* Docker build context

Do not assume the code shown in one file represents the entire repository.

---

# 78. BUILD CONTEXT

Docker build context is the repository root.

Important:

```text
Dockerfile
ProMapCargo.sln
ProMapCargo.Api.csproj
Importer/ProMapCargo.OsmImporter.csproj
```

must remain compatible with the Docker build instructions.

Do not move projects without updating:

* solution
* Dockerfile
* project references
* CI
* scripts

---

# 79. GITHUB ACTIONS

The repository has CI/build infrastructure.

Changes should remain compatible with GitHub Actions.

Do not rely exclusively on:

* local Visual Studio configuration
* user-specific paths
* Windows-only commands

unless the feature is explicitly Windows-specific.

---

# 80. CROSS-PLATFORM SCRIPTS

Where both exist:

* `.ps1`
* `.sh`

keep them conceptually equivalent.

PowerShell is important for Windows development.

Shell scripts are important for Linux/Docker/CI workflows.

---

# 81. FILE PATHS

Use portable paths in C#.

Do not hard-code:

```text
C:\Users\...
```

Use:

* `Path.Combine`
* `IWebHostEnvironment.WebRootPath`
* `ContentRootPath`

The browser must never receive server filesystem paths.

---

# 82. MAP PATH SECURITY

The dynamic PMTiles endpoint must only serve files from:

`wwwroot/maps`

Do not allow path traversal.

The region parameter must resolve to a controlled filename.

Do not turn it into an arbitrary filesystem path.

---

# 83. ROUTING SECURITY

Routing endpoints may be anonymous where currently configured, but they must still validate:

* coordinates
* profile
* truck values
* numeric ranges
* request size

Do not trust browser-provided truck parameters.

---

# 84. TRUCK PARAMETER VALIDATION

When validating truck parameters, reject impossible values such as:

* negative weight
* zero/negative dimensions
* negative axle load
* impossible axle count
* invalid maximum speed

Validation should protect both the database and routing algorithm.

---

# 85. NO SILENT CONTRACT CHANGES

When modifying:

* route request
* route response
* GeoJSON
* PMTiles URLs
* map style source names
* database schema
* SignalR messages

update all consumers.

Use repository-wide search before committing.

---

# 86. CHANGE SCOPE

For bug fixes:

* make the smallest coherent change
* avoid unrelated refactors
* preserve existing behavior
* explain architectural consequences

For architectural changes:

* update documentation
* update tests
* update Docker
* update scripts
* update frontend
* update backend
* update schema where required

---

# 87. CODE REVIEW PRIORITIES

When reviewing changes, prioritize:

1. correctness
2. routing correctness
3. coordinate correctness
4. database correctness
5. map asset correctness
6. API compatibility
7. security
8. Docker compatibility
9. performance
10. maintainability
11. visual consistency

Do not prioritize cosmetic refactoring over functional correctness.

---

# 88. ROUTING BUG TRIAGE

When route calculation fails, use this decision tree:

### A. API returns InvalidStart/InvalidDestination

Inspect:

* geocoding
* frontend coordinate extraction
* request JSON
* `GeoPoint`

### B. API returns NoGraph

Inspect:

* `routing_graph_versions`
* importer
* active graph version
* graph activation

### C. API returns NoSnap

Inspect:

* graph coverage
* coordinate order
* SRID
* snap radius
* road edge spatial index

### D. API returns NoRoute

Inspect:

* edge direction
* truck evaluator
* turn restrictions
* graph connectivity
* A* expansion limit

### E. OSRM fallback fails

Inspect:

* `Routing:OsrmBaseUrl`
* Docker `osrm` service
* `/data/serbia-latest.osrm`
* OSRM container logs

### F. API returns route but map shows no line

Inspect:

* response JSON
* geometry
* coordinate order
* route layer
* map style
* map visibility
* map resize

---

# 89. MAP BUG TRIAGE

When the map itself does not render:

### Step 1

Confirm the map container exists.

### Step 2

Confirm its CSS height.

### Step 3

Confirm MapLibre/Leaflet scripts are loaded locally.

### Step 4

Confirm PMTiles library is loaded.

### Step 5

Confirm PMTiles URL.

### Step 6

Confirm archive HTTP response.

### Step 7

Confirm Range support.

### Step 8

Confirm style JSON.

### Step 9

Confirm glyph requests.

### Step 10

Confirm browser console.

Never start by modifying routing code when the basemap itself is not loading.

---

# 90. ROUTE LINE BUG TRIAGE

If the basemap is visible but the route line is not:

Do not regenerate PMTiles.

Check:

1. API response
2. route geometry
3. `coordinates`
4. selected route
5. route layer creation
6. line source
7. line layer
8. coordinate projection
9. map instance
10. route visibility

The route line is application overlay logic, not PMTiles basemap data.

---

# 91. NO FAKE MAP DATA

Do not generate fake road networks to make the UI look populated.

If the map dataset is missing, report that the dataset is missing.

If routing graph is missing, use the configured OSRM fallback.

Do not fabricate a PostGIS route.

---

# 92. NO FAKE ROUTING DIAGNOSTICS

Diagnostics must represent actual execution.

Examples:

If PostGIS succeeded:

```text
Engine = PostGIS-AStar
UsedFallback = false
GraphVersion = actual graph version
```

If OSRM fallback succeeded:

```text
Engine = OSRM
UsedFallback = true
GraphVersion = null
```

Never fabricate a graph version.

---

# 93. DATABASE FAILURE BEHAVIOR

If PostgreSQL is unavailable:

* application should start where practical
* database-dependent features should fail clearly
* logs should identify the dependency
* routing should fall back only if the configured fallback is available

Do not return fake database records.

---

# 94. EXTERNAL DEPENDENCY FAILURE

External services include:

* Nominatim
* optional public OSRM fallback

They can fail.

Design the application so:

* UI remains usable
* errors are clear
* retries are bounded
* local map does not depend on external basemap availability

---

# 95. LOCAL MAP INDEPENDENCE

The local basemap should continue working if:

* Nominatim is unavailable
* OSRM is unavailable
* PostgreSQL is unavailable

The map rendering subsystem should not depend on the routing subsystem.

---

# 96. DO NOT COUPLE MAP AND ROUTING

A route request must not be responsible for initializing the basemap.

A PMTiles load failure must not be interpreted as a routing failure.

A routing failure must not prevent the base map from rendering.

Keep the subsystems independent.

---

# 97. DO NOT COUPLE GEOCODING AND MAP RENDERING

Geocoding failure should not destroy the map.

Map failure should not destroy the geocoding form.

Routing failure should not destroy either.

---

# 98. PAGE SCRIPT LOADING

Shared scripts are loaded from `_Layout.cshtml`.

Page-specific scripts should be placed in the Razor `Scripts` section where appropriate.

Do not include the same library twice.

Do not load a CDN copy in one page while loading a local copy in another.

---

# 99. CACHE BUSTING

Use ASP.NET Core:

```text
asp-append-version="true"
```

for static assets where already established.

Do not manually append random query strings.

---

# 100. CSS ARCHITECTURE

Existing CSS is split across:

* `tokens.css`
* `base.css`
* `components.css`
* `forms.css`
* `tables.css`
* `animations.css`
* `compat.css`
* `site.css`

Prefer the existing system.

Do not place hundreds of lines of page CSS into JavaScript.

Do not introduce another global CSS framework.

---

# 101. ACCESSIBILITY

Controls must have:

* labels
* accessible names
* keyboard accessibility
* visible focus where appropriate

Map controls and navigation buttons must not depend exclusively on color.

Do not remove ARIA labels already present.

---

# 102. INTERNATIONALIZATION

The current UI contains Serbian terminology.

Preserve existing Serbian labels unless explicitly asked to translate them.

Backend identifiers should remain English/C# conventions.

Do not randomly translate domain concepts.

---

# 103. COMMENTS

Comments should explain:

* why something is necessary
* architectural constraints
* non-obvious behavior

Avoid comments that simply restate the code.

---

# 104. GENERATED FILES

Do not manually edit generated:

* binaries
* build outputs
* generated map archives
* OSRM datasets

Modify the source/generation script instead.

---

# 105. BINARY TOOLS

The repository may contain helper binaries such as PMTiles tooling.

Do not replace them with runtime downloads.

Do not add unnecessary executable binaries.

If a binary is already part of the repository workflow, understand why before removing it.

---

# 106. DOCUMENTATION COMMANDS

When documenting commands, ensure commands match the actual project.

Prefer:

```bash
dotnet restore ProMapCargo.sln
dotnet build ProMapCargo.sln
docker compose config
docker compose up --build
```

Do not document commands for projects/files that do not exist.

---

# 107. CLEAN BUILD PRINCIPLE

Before finalizing a change, check for:

* compile errors
* stale namespaces
* stale property names
* duplicate classes
* duplicate JavaScript initialization
* missing static assets
* missing Docker mounts
* incorrect environment variable names
* inconsistent documentation

---

# 108. SEARCH BEFORE EDIT

Before modifying an important symbol, search for all usages.

Especially search before changing:

* `RouteRequest`
* `RouteResponse`
* `TruckProfile`
* `GeoPoint`
* `Target`
* `Destination`
* `Profile`
* `PostGisRoutingService`
* `OsrmRoutingService`
* `navigation.js`
* `/api/routing/route`
* `/maps/`
* `serbia.pmtiles`
* `europe.pmtiles`

---

# 109. ONE AUTHORITATIVE IMPLEMENTATION

For every domain concept there should be one authoritative implementation.

Examples:

Routing request:

`Models/RouteRequest.cs`

Truck evaluation:

`Routing/TruckEdgeEvaluator.cs`

PostGIS routing:

`Routing/PostGisRoutingService.cs`

OSRM:

`Services/OsrmRoutingService.cs`

Navigation UI:

`wwwroot/js/navigation.js`

Shared map layers:

`wwwroot/js/promap-map-layers.js`

PMTiles serving:

`Program.cs`

Do not create shadow implementations.

---

# 110. WHEN REFACTORING ROUTING

Routing refactors must preserve:

* coordinate contract
* truck restrictions
* graph version
* turn restrictions
* fallback
* route geometry
* diagnostics
* cancellation
* performance limits

A routing refactor is not successful if it merely compiles.

---

# 111. WHEN REFACTORING MAPS

Map refactors must preserve:

* local assets
* PMTiles
* glyphs
* MapLibre
* Leaflet
* route overlays
* markers
* map interactions
* responsive layout

A map refactor is not successful if the page loads but routes disappear.

---

# 112. WHEN ADDING NEW MAP REGIONS

If adding a new region:

1. add the generation workflow
2. define archive naming
3. add runtime path
4. verify static serving
5. verify range processing
6. update map selector if needed
7. update documentation
8. do not commit the huge generated archive unless explicitly requested

---

# 113. WHEN ADDING NEW ROUTING PROFILE

If adding a profile:

1. update request contract
2. validate profile
3. update PostGIS routing behavior
4. update OSRM mapping
5. update UI
6. update diagnostics
7. test fallback
8. document semantics

Do not silently map a new truck profile to generic driving without explicitly identifying the limitation.

---

# 114. WHEN ADDING NEW TRUCK RESTRICTION

If adding a restriction:

1. identify OSM source tag
2. add importer support
3. store the value in graph schema if necessary
4. update `RoadEdge`
5. update `TruckEdgeEvaluator`
6. test both directions
7. test fallback behavior
8. update documentation

---

# 115. PULL REQUEST EXPECTATIONS

Every meaningful PR should explain:

* what changed
* why
* affected components
* database impact
* Docker impact
* map impact
* routing impact
* validation performed

Do not mix unrelated feature work into a routing bug fix.

---

# 116. COPILOT RESPONSE BEHAVIOR

When asked to fix something:

1. inspect the relevant files
2. inspect callers
3. inspect configuration
4. inspect related frontend code
5. inspect schema if database-related
6. identify the root cause
7. propose the smallest coherent fix
8. implement it
9. build/test
10. report exactly what was changed

Do not immediately generate replacement files without understanding the current implementation.

---

# 117. COPILOT MUST NOT GUESS

If information is missing:

* inspect the repository
* search for the symbol
* inspect configuration
* inspect Docker
* inspect scripts

Do not invent:

* endpoints
* table names
* properties
* environment variables
* map URLs
* file paths
* package versions

---

# 118. COPILOT MUST DISTINGUISH LOCAL VS GITHUB STATE

GitHub may intentionally omit large runtime datasets.

When investigating a map problem, distinguish:

```text
source-controlled code
```

from:

```text
local generated data
```

and:

```text
Docker-mounted runtime data
```

A repository tree is not necessarily the complete runtime filesystem.

---

# 119. CURRENT MAP ARCHITECTURE

The intended current architecture is:

```text
Browser
  |
  +-- Razor Pages
  |
  +-- Leaflet
  |
  +-- MapLibre
  |
  +-- PMTiles
  |      |
  |      +-- /maps/serbia.pmtiles
  |      +-- /maps/europe.pmtiles
  |
  +-- Local glyphs
  |
  +-- /api/geocoding
  |
  +-- /api/routing/route
  |
  +-- /hubs/navigation
         |
         +-- GPS / telemetry
```

The basemap is local.

Routing is server-side.

Geocoding is separate.

Telemetry is separate.

---

# 120. CURRENT ROUTING ARCHITECTURE

```text
Navigation UI
     |
     v
/api/routing/route
     |
     v
RoutingController
     |
     +--------------------------+
     |                          |
     v                          v
PostGisRoutingService       OSRM fallback
     |                          |
     v                          v
PostGIS graph                osrm:5000
     |
     +-- EdgeSnapper
     +-- TruckEdgeEvaluator
     +-- PostGisAStarRouter
     +-- TurnRestrictionMatcher
     +-- ManeuverBuilder
     |
     v
RouteResponse
     |
     v
Navigation UI
```

Do not bypass this architecture without an explicit architectural decision.

---

# 121. CURRENT DATABASE ARCHITECTURE

```text
PostgreSQL/PostGIS
        |
        +-- Application/Identity data
        |
        +-- Business data
        |
        +-- Restrictions
        |
        +-- OSM graph
        |
        +-- Routing graph versions
        |
        +-- Turn restrictions
```

There is one primary database.

---

# 122. CURRENT OSM IMPORT ARCHITECTURE

```text
OSM PBF
  |
  v
ProMapCargo.OsmImporter
  |
  +-- nodes
  +-- ways
  +-- way nodes
  +-- road edges
  +-- restrictions
  |
  v
PostGIS versioned graph
  |
  v
ready
  |
  v
active graph
  |
  v
PostGIS routing
```

---

# 123. CURRENT MAP DATA ARCHITECTURE

```text
OSM PBF
   |
   v
PMTiles generation scripts
   |
   v
wwwroot/maps/*.pmtiles
   |
   v
Docker volume / local filesystem
   |
   v
Program.cs
   |
   v
/maps/{region}.pmtiles
   |
   v
PMTiles client
   |
   v
MapLibre
```

Do not confuse this map pipeline with the PostGIS routing graph pipeline.

They may use the same OSM source but are different generated datasets.

---

# 124. DETAILED FILE STRUCTURE (ProMapCargo Solution)

## 124.1 ProMapCargo.Api Project Root

```
ProMapCargo.Api/
├── Program.cs                           ← Application bootstrap (ASP.NET Core setup)
├── ProMapCargo.Api.csproj               ← Project file (.NET 10)
├── appsettings.json                     ← Configuration (connection strings, logging)
├── appsettings.Development.json         ← Development-specific config
├── NuGet.config                         ← NuGet package sources
├── .env                                 ← Docker environment variables
├── docker-compose.yml                   ← Local development stack (Postgres, OSRM, API)
├── Dockerfile                           ← Container image definition
├── libman.json                          ← Frontend library management
├── .dockerignore                        ← Docker build exclusions
├── .gitignore                           ← Git exclusions (maps, volumes, temp files)
├── README.md                            ← Project documentation
├── FIXES_APPLIED.md                     ← Fix history
├── ProMapCargo.sln                      ← Solution file
│
├── Controllers/                         ← HTTP API endpoints
│   ├── RoutingController.cs             ← POST /api/routing/route (main truck routing)
│   ├── TripRoutingController.cs         ← Trip route calculation
│   ├── GeocodingController.cs           ← Nominatim geocoding integration
│   ├── NavigationTelemetryController.cs ← GPS/telemetry endpoints
│   ├── RestrictionsController.cs        ← Truck restriction query API
│   ├── RestrictionAdminController.cs    ← Restriction management
│   ├── OperationsController.cs          ← Fleet operations endpoints
│   ├── BusinessController.cs            ← Business logic endpoints
│   ├── DriverController.cs              ← Driver management API
│   ├── AlertsController.cs              ← Alert management API
│   └── MobileAuthController.cs          ← Mobile JWT authentication
│
├── Routing/                             ← PostGIS truck routing engine
│   ├── PostGisRoutingService.cs         ← Orchestrator (snap → A* → geometry → maneuvers)
│   ├── PostGisRoutingRepository.cs      ← Graph queries (graph versions, edges, nodes)
│   ├── PostGisAStarRouter.cs            ← A* implementation (pathfinding)
│   ├── EdgeSnapper.cs                   ← Snap coordinates to graph edges
│   ├── TruckEdgeEvaluator.cs            ← Edge cost (truck constraints, restrictions)
│   ├── TurnRestrictionMatcher.cs        ← Turn restriction validation
│   ├── ManeuverBuilder.cs               ← Generate turn instructions
│   └── RoutingModels.cs                 ← Domain models (traversal, maneuver, etc.)
│
├── Services/                            ← Business services
│   ├── IRoutingService.cs               ← Routing interface (fallback contract)
│   ├── OsrmRoutingService.cs            ← OSRM fallback implementation
│   ├── IGeocodingService.cs             ← Geocoding interface
│   ├── NominatimGeocodingService.cs     ← Nominatim geocoding implementation
│   ├── IRestrictionEngine.cs            ← Restriction checking interface
│   ├── PostgresRestrictionEngine.cs     ← PostGIS-backed restrictions
│   ├── RestrictionEngine.cs             ← In-memory restrictions fallback
│   ├── IRestrictionRepository.cs        ← Restriction data interface
│   ├── PostgresRestrictionRepository.cs ← SQL restriction queries
│   ├── JsonRestrictionRepository.cs     ← Local JSON restrictions
│   ├── MobileTokenService.cs            ← JWT token generation/validation
│   ├── ApiAuthHandler.cs                ← HTTP auth handler
│   ├── NavigationHub.cs                 ← SignalR hub (GPS/telemetry)
│   ├── CurrentUserContext.cs            ← User identity context
│   ├── IdentityClaimsFactory.cs         ← ASP.NET Identity claims
│   └── BusinessServices.cs              ← Logistics operations logic
│
├── Models/                              ← Data contracts
│   ├── RouteRequest.cs                  ← Request body: start, end, truck profile
│   ├── RouteResponse.cs                 ← Response body: geometry, maneuvers, summary
│   ├── TruckProfile.cs                  ← Truck dimensions, weight, restrictions
│   ├── VehicleProfile.cs                ← Generic vehicle profile
│   ├── RouteOptions.cs                  ← Route calculation options
│   ├── GeoPoint.cs                      ← Coordinate model (latitude/longitude)
│   ├── GeocodingResult.cs               ← Nominatim result
│   ├── Restriction.cs                   ← Restriction definition
│   ├── RoadRestriction.cs               ← Road-specific restriction
│   ├── BusinessModels.cs                ← Orders, trips, vehicles, drivers
│   ├── MobileAuthModels.cs              ← Mobile login/register
│   ├── MobileAuthOptions.cs             ← Mobile auth configuration
│   └── MobileRefreshToken.cs            ← Refresh token model
│
├── Data/                                ← Entity Framework Core
│   ├── ProMapCargoDbContext.cs          ← EF DbContext (application + routing tables)
│   └── restrictions.json                ← Local restriction dataset (fallback)
│
├── Sql/                                 ← Database scripts
│   ├── 03-routing-graph.sql             ← PostGIS schema (nodes, edges, restrictions, cells)
│   ├── 01-indexes.sql                   ← Query optimization (spatial, btree)
│   ├── 02-useful-queries.sql            ← Diagnostic queries
│   ├── 04-operational-indexes.sql       ← Operational performance indexes
│   └── import-plan.md                   ← Import workflow documentation
│
├── Pages/                               ← Razor Pages (server-rendered UI)
│   ├── Index.cshtml                     ← Dashboard (main page)
│   ├── Navigation/Index.cshtml          ← **CRITICAL: Route calculation & live map**
│   ├── Dispatch/Index.cshtml            ← Fleet dispatch management
│   ├── Monitoring/Index.cshtml          ← Live fleet monitoring
│   ├── Orders/Index.cshtml              ← Order management
│   ├── Trips/Index.cshtml               ← Trip management
│   ├── Vehicles/Index.cshtml            ← Vehicle fleet list
│   ├── Vehicles/New.cshtml              ← New vehicle form
│   ├── Drivers/Index.cshtml             ← Driver list
│   ├── Driver/Index.cshtml              ← Individual driver profile
│   ├── Alerts/Index.cshtml              ← Alert management
│   ├── Compliance/Index.cshtml          ← Compliance reporting
│   ├── Audit/Index.cshtml               ← Audit log
│   ├── Finance/Index.cshtml             ← Financial analytics
│   ├── Reports/Index.cshtml             ← Report generation
│   ├── Moderation/Index.cshtml          ← Content moderation
│   ├── Settings/Index.cshtml            ← Application settings
│   ├── Profile/Index.cshtml             ← User profile
│   ├── Admin/Index.cshtml               ← Admin panel
│   ├── Login/Index.cshtml               ← Authentication form
│   ├── promap-layer-panel.html          ← Layer panel component
│   ├── Shared/_Layout.cshtml            ← Master layout template
│   ├── _ViewImports.cshtml              ← Global directives (@using, @inject)
│   └── _ViewStart.cshtml                ← View initialization
│
├── wwwroot/                             ← Static web assets
│   ├── css/
│   │   ├── base.css                     ← Base styles
│   │   ├── site.css                     ← Global theme
│   │   ├── animations.css               ← Animation definitions
│   │   ├── compat.css                   ← Browser compatibility CSS
│   │   ├── components.css               ← Component styles (buttons, cards, etc.)
│   │   ├── forms.css                    ← Form styling
│   │   ├── tables.css                   ← Table styling
│   │   ├── tokens.css                   ← Design tokens (colors, spacing)
│   │   └── pages/                       ← Per-page styles
│   │       ├── index.css
│   │       ├── navigation-index.css     ← Navigation page styles
│   │       ├── dispatch-index.css
│   │       ├── monitoring-index.css
│   │       ├── orders-index.css
│   │       ├── trips-index.css
│   │       ├── vehicles-index.css
│   │       ├── drivers-index.css
│   │       ├── driver-index.css
│   │       ├── alerts-index.css
│   │       ├── compliance-index.css
│   │       ├── audit-index.css
│   │       ├── finance-index.css
│   │       ├── reports-index.css
│   │       ├── moderation-index.css
│   │       ├── settings-index.css
│   │       ├── profile-index.css
│   │       ├── admin-index.css
│   │       ├── login-index.css
│   │       └── vehicles-new.css
│   │
│   ├── js/
│   │   ├── app.js                       ← Application initialization
│   │   ├── navigation.js                ← **Navigation page orchestration**
│   │   ├── promap-routing.js            ← Routing API client
│   │   ├── promap-maneuvers.js          ← Maneuver rendering
│   │   ├── promap-gps.js                ← GPS tracking & off-route detection
│   │   ├── promap-map-enhancements.js   ← Map layer management
│   │   ├── promap-map-layers.js         ← Layer definitions (roads, restrictions)
│   │   ├── promap-pmtiles-init.js       ← PMTiles initialization
│   │   ├── navigation-integration-snippet.js ← Integration snippet
│   │   ├── alerts.js                    ← Alert notifications
│   │   ├── core/                        ← Utility modules
│   │   │   ├── compat.js                ← Browser compatibility
│   │   │   ├── dom.js                   ← DOM helpers
│   │   │   ├── events.js                ← Event utilities
│   │   │   ├── format.js                ← String/date formatting
│   │   │   ├── http.js                  ← HTTP client
│   │   │   ├── modal.js                 ← Modal dialog control
│   │   │   ├── toast.js                 ← Notification toasts
│   │   │   └── validation.js            ← Form validation
│   │   ├── services/
│   │   │   └── alerts.js                ← Alert service
│   │   └── pages/                       ← Page-specific logic
│   │       ├── index.js                 ← Dashboard scripts
│   │       ├── dispatch-index.js
│   │       ├── monitoring-index.js
│   │       ├── orders-index.js
│   │       ├── finance-index.js
│   │       ├── drivers-index.js
│   │       └── vehicles-index.js
│   │
│   ├── lib/                             ← Third-party libraries
│   │   ├── leaflet/
│   │   │   ├── leaflet.js
│   │   │   ├── leaflet.css
│   │   │   └── images/
│   │   │       ├── marker-icon.png
│   │   │       ├── marker-icon-2x.png
│   │   │       └── marker-shadow.png
│   │   ├── maplibre-gl/
│   │   │   ├── dist/
│   │   │   │   ├── maplibre-gl.js       ← MapLibre GL vector map library
│   │   │   │   └── maplibre-gl.css
│   │   │   └── promap-maplibre-bridge.js ← Custom Leaflet + MapLibre bridge
│   │   ├── maplibre-gl-leaflet/
│   │   │   └── leaflet-maplibre-gl.js   ← Integration layer
│   │   ├── pmtiles/
│   │   │   └── dist/pmtiles.js          ← PMTiles protocol client
│   │   └── signalr/
│   │       └── signalr.min.js           ← SignalR WebSocket client
│   │
│   ├── maps/                            ← Vector tile archives
│   │   ├── serbia.pmtiles               ← Serbia basemap tiles
│   │   └── europe.pmtiles               ← Europe basemap tiles
│   │
│   ├── fonts/                           ← MapLibre glyph ranges
│   │   └── Noto Sans Regular/
│   │       ├── 0-255.pbf                ← Unicode range 0-255
│   │       ├── 256-511.pbf              ← Unicode range 256-511
│   │       └── 1024-1279.pbf            ← Unicode range 1024-1279
│   │
│   └── styles/
│       ├── promap-dark.json             ← MapLibre GL style definition
│       └── promap-cargo-v3-reference.html ← Style reference
│
├── profiles/                            ← OSRM routing profiles
│   └── osrm/
│       ├── car.lua                      ← Car routing profile
│       └── truck.lua                    ← Truck routing profile (fallback fallback)
│
├── scripts/                             ← Build & setup scripts
│   ├── apply-local-map.ps1              ← PowerShell: apply PMTiles
│   ├── build-europe-osrm-truck.ps1      ← PowerShell: build OSRM Europe
│   ├── build-europe-osrm-truck.sh       ← Bash: build OSRM Europe
│   ├── build-serbia-pmtiles.ps1         ← PowerShell: build Serbia tiles
│   ├── build-serbia-pmtiles.sh          ← Bash: build Serbia tiles
│   ├── prepare-local-map-assets.ps1     ← PowerShell: map prep
│   ├── prepare-local-map-assets.sh      ← Bash: map prep
│   ├── start-osrm.sh                    ← Bash: start OSRM container
│   └── verify.sh                        ← Bash: verification script
│
├── osm/                                 ← OSM data (local, not committed)
│   ├── .gitkeep
│   ├── serbia-latest.osm.pbf            ← Serbia OSM export (PBF format)
│   ├── europe-latest.osm.pbf            ← Europe OSM export (PBF format)
│   ├── serbia-latest.osrm                ← OSRM pre-compiled data (binary)
│   ├── serbia-latest.osrm.* (various)   ← OSRM auxiliary files
│   └── europe-latest.osrm.timestamp     ← OSRM timestamp
│
├── Properties/
│   └── launchSettings.json              ← Debug launch configuration
│
├── pmtiles-metadata.json                ← PMTiles metadata
├── pmtiles-tool/
│   ├── pmtiles.exe                      ← PMTiles CLI tool
│   ├── README.md
│   └── LICENSE
│
└── (Generated files during runtime)
    ├── bin/                             ← Compiled binaries
    ├── obj/                             ← Intermediate objects
    └── .vs/                             ← Visual Studio cache
```

## 124.2 ProMapCargo.Mobile Project (MAUI)

```
ProMapCargo.Mobile/
├── ProMapCargo.Mobile.csproj            ← MAUI project file (.NET 10)
│
├── App.xaml                             ← App resource dictionary
├── App.xaml.cs                          ← App code-behind
├── AppShell.xaml                        ← Navigation shell
├── AppShell.xaml.cs
├── MainPage.xaml                        ← Landing page
├── MainPage.xaml.cs
├── MauiProgram.cs                       ← MAUI service bootstrap (DI)
│
├── Views/                               ← XAML UI pages
│   ├── LoginPage.xaml                   ← Mobile login
│   ├── LoginPage.xaml.cs
│   ├── DashboardPage.xaml               ← Fleet dashboard
│   └── DashboardPage.xaml.cs
│
├── ViewModels/                          ← MVVM logic
│   ├── ViewModelBase.cs                 ← Base class (INotifyPropertyChanged)
│   ├── LoginViewModel.cs                ← Login state & commands
│   └── DashboardViewModel.cs            ← Dashboard state
│
├── Models/                              ← Data contracts
│   ├── AuthModels.cs                    ← Login/token models
│   ├── BusinessModels.cs                ← Fleet operations models
│   └── MobileAppOptions.cs              ← Configuration
│
├── Services/                            ← Cross-platform services
│   ├── ApiClient.cs                     ← HTTP client factory
│   ├── ApiAuthHandler.cs                ← JWT injection handler
│   ├── MobileSessionService.cs          ← Session management
│   └── TokenStore.cs                    ← Secure token storage
│
├── Converters/                          ← XAML value converters
│   └── StringNotEmptyConverter.cs
│
├── Resources/                           ← App resources
│   ├── Styles/
│   │   ├── Colors.xaml                  ← Color palette
│   │   └── Styles.xaml                  ← XAML styles
│   ├── Fonts/
│   │   ├── OpenSans-Regular.ttf
│   │   └── OpenSans-Semibold.ttf
│   ├── Images/
│   │   └── dotnet_bot.png
│   ├── Splash/
│   │   └── splash.svg
│   ├── AppIcon/
│   │   ├── appicon.svg
│   │   └── appiconfg.svg
│   └── Raw/
│       └── AboutAssets.txt
│
└── Platforms/                           ← Platform-specific code
    ├── Android/
    │   ├── MainActivity.cs              ← Activity entry point
    │   ├── MainApplication.cs           ← Application init
    │   ├── AndroidManifest.xml
    │   ├── Resources/
    │   │   └── values/
    │   │       └── colors.xml           ← Theme colors
    │   └── (generated ProGuard configs)
    ├── iOS/
    │   ├── AppDelegate.cs
    │   ├── Program.cs                   ← iOS entry point
    │   ├── Info.plist                   ← Configuration
    │   └── Resources/
    │       └── PrivacyInfo.xcprivacy
    ├── MacCatalyst/
    │   ├── AppDelegate.cs
    │   ├── Program.cs
    │   ├── Entitlements.plist
    │   └── Info.plist
    └── Windows/
        ├── App.xaml
        ├── App.xaml.cs
        ├── app.manifest
        └── Package.appxmanifest
```

## 124.3 ProMapCargo.OsmImporter Project

```
Importer/
├── ProMapCargo.OsmImporter.csproj       ← Console app (.NET 10)
│
├── Program.cs                           ← CLI entry point
│   ├── Validates PBF file path
│   ├── Resolves database connection
│   ├── Generates graph version (Unix timestamp)
│   ├── Invokes GraphImporter.ImportAsync()
│   └── Prints summary
│
├── GraphImporter.cs                     ← Main orchestrator (516 lines)
│   ├── ImportAsync()
│   │   ├── Create database connection
│   │   ├── Execute schema (03-routing-graph.sql)
│   │   ├── Cleanup old version data
│   │   ├── Insert routing_graph_versions (status='building')
│   │   ├── Read & parse PBF file
│   │   ├── Call CopyNodes()
│   │   ├── Call CopyWays()
│   │   ├── Call CopyRestrictions()
│   │   ├── Update to status='ready'
│   │   └── Log completion
│   ├── CopyNodes()
│   │   ├── Binary COPY to osm_nodes
│   │   └── Preserves node ID and geometry
│   ├── CopyWays()
│   │   ├── Binary COPY to osm_ways
│   │   ├── Binary COPY to osm_way_nodes
│   │   └── Calls WriteEdges() for each way
│   ├── WriteEdges()
│   │   ├── Builds edges from consecutive nodes
│   │   ├── Evaluates truck attributes
│   │   ├── Calculates geometry
│   │   ├── Calculates length via ST_Length()
│   │   └── Executes INSERT to road_edges
│   ├── CopyRestrictions()
│   │   ├── Parses turn restrictions from relations
│   │   └── Inserts to turn_restrictions
│   └── Helper functions
│       ├── Tags() — Parse OSM tag dictionary
│       ├── Direction() — Determine edge directionality
│       ├── DefaultSpeed() — Infer speed from highway type
│       ├── HierarchyPenalty() — Truck constraint penalty
│       └── Speed(), Weight(), Num() — Parse numeric tags
│
├── ImportDbContext.cs                   ← Legacy EF DbContext (may consolidate)
├── OsmRestrictionImporter.cs            ← Restriction parsing from relations
│
└── (References Sql/03-routing-graph.sql at runtime)
    └── Creates tables, constraints, spatial indexes
```

## 124.4 Solution Root

```
ProMapCargo/
├── ProMapCargo.sln                      ← Solution file
├── .github/
│   └── copilot-instructions.md          ← Extended Copilot reference (.github version)
├── copilot-instructions.md              ← This file (root version, merged all projects)
│
├── docker-compose.yml                   ← Multi-container local stack
├── Dockerfile                           ← API container image
├── .dockerignore
├── .gitignore                           ← .gitkeep for osm/, volumes/, etc.
├── .gitattributes                       ← Line ending rules
│
├── .env                                 ← Docker environment
├── README.md                            ← Main documentation
├── FIXES_APPLIED.md                     ← Cumulative fix log
│
├── Sql/
│   ├── 03-routing-graph.sql             ← PostGIS schema (shared reference)
│   ├── 01-indexes.sql
│   ├── 02-useful-queries.sql
│   ├── 04-operational-indexes.sql
│   └── import-plan.md
│
├── osm/                                 ← OSM data (local only, .gitignore'd)
│   ├── .gitkeep
│   ├── *.osm.pbf                        ← OSM country exports
│   ├── *.osrm*                          ← Compiled OSRM data
│   └── *.osrm.timestamp
│
├── scripts/                             ← Utility scripts
│   ├── apply-local-map.ps1              ← PowerShell
│   ├── apply-local-map.sh               ← Bash
│   ├── build-europe-osrm-truck.ps1
│   ├── build-europe-osrm-truck.sh
│   ├── build-serbia-pmtiles.ps1
│   ├── build-serbia-pmtiles.sh
│   ├── prepare-local-map-assets.ps1
│   ├── prepare-local-map-assets.sh
│   ├── start-osrm.sh
│   └── verify.sh
│
├── pmtiles-tool/
│   ├── pmtiles.exe                      ← PMTiles CLI (Windows)
│   └── README.md
│
└── ProMapCargo.Api, ProMapCargo.Mobile, Importer/
    └── (See above sections 124.1, 124.2, 124.3)
```

---

# 125. FINAL VALIDATION CHECKLIST

Before declaring a feature complete:

## Build

* `dotnet restore` succeeds
* `dotnet build` succeeds
* no warnings/errors caused by the change

## Backend

* API starts
* database connection works
* routing endpoint works
* fallback works

## Database

* schema is valid
* indexes exist
* graph version behavior is correct

## Maps

* local runtime assets load
* PMTiles loads
* glyphs load
* map renders
* route overlay renders

## Navigation

* geocoding works
* start/destination coordinates are valid
* truck parameters are sent
* route is calculated
* summary updates
* maneuvers update
* GPS failure is graceful

## Docker

* compose config is valid
* API image builds
* PostgreSQL starts
* OSRM starts
* API connects to internal services
* map mounts exist

## Documentation

* README matches actual implementation
* .NET version is correct
* routing architecture is correct
* map architecture is correct

---

# 126. GOLDEN RULES

Always remember:

1. Do not break the existing architecture to fix one bug.
2. Do not invent missing infrastructure.
3. Do not confuse map rendering with routing.
4. Do not confuse geocoding with routing.
5. Do not confuse OSRM fallback with truck-aware routing.
6. Do not confuse GitHub contents with local generated datasets.
7. Do not reverse latitude/longitude.
8. Do not bypass truck restrictions.
9. Do not route against an inactive graph.
10. Do not remove range processing from PMTiles.
11. Do not introduce CDN runtime dependencies.
12. Do not duplicate existing services.
13. Do not hide root causes with fake fallbacks.
14. Do not claim a fix without validation.
15. Keep backend, frontend, database, Docker and documentation synchronized.
16. Prefer a small, correct change over a large rewrite.
17. Search the repository before changing contracts.
18. Preserve existing API compatibility unless explicitly changing it.
19. Treat Navigation and truck routing as core production functionality.
20. If uncertain, inspect the repository before guessing.
