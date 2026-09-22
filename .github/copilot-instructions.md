# ProMap Cargo — Unified GitHub Copilot Instructions

## 1. PROJECT IDENTITY

ProMap Cargo is a truck-aware fleet operations, dispatch, transport management and navigation platform.

The solution consists of three main projects:
- **ProMapCargo.Api** — ASP.NET Core server application
- **ProMapCargo.Mobile** — .NET MAUI mobile application  
- **ProMapCargo.OsmImporter** — OSM PostGIS graph importer

The primary goal is a production-oriented logistics platform for truck routing and fleet operations.

Do not treat this repository as a generic CRUD application. The routing engine, map infrastructure, OSM graph, truck restrictions, PMTiles delivery and Navigation UI are core functionality.

---

## 2. SOURCE OF TRUTH

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

Large map datasets are intentionally excluded from Git:
* OSM PBF files
* PMTiles archives
* OSRM generated datasets
* local map datasets
* database volumes
* temporary importer files
* generated routing data

Check `.gitignore` before concluding that a missing GitHub file is missing from the actual application.

---

## 3. CURRENT TECHNOLOGY STACK

Use the technology versions already defined by the repository.

### Main Technologies
* **.NET 10** (across all projects)
* **ASP.NET Core** (Razor Pages, MVC Controllers)
* **Entity Framework Core 10**
* **PostgreSQL** + **PostGIS**
* **Npgsql** + **NetTopologySuite**
* **SignalR** (live telemetry)
* **ASP.NET Identity**
* **Docker** + **Docker Compose**

### Web & Maps
* **Leaflet** + **MapLibre GL**
* **PMTiles** (local vector tiles)
* **OpenMapTiles-compatible glyphs**

### Mobile
* **.NET MAUI** (multi-platform)
* **No Xamarin Forms** (MAUI equivalent only)

### Routing & Import
* **PostGIS** (primary truck routing)
* **OSRM** (fallback routing)
* **OsmSharp** (PBF parsing)

### Build & CI
* **GitHub Actions**
* **NuGet package management**

Do not downgrade to .NET 9 or earlier unless explicitly requested.

---

## 4. PROJECT STRUCTURE

### ProMapCargo.Api (Main Web Application)

#### Project Root Files
* `Program.cs` — Application bootstrap, DI, middleware, static files, Razor Pages, SignalR, PMTiles serving
* `ProMapCargo.Api.csproj` — .NET version, NuGet dependencies
* `appsettings.json` — Configuration
* `docker-compose.yml` — Local Docker orchestration
* `Dockerfile` — API container definition
* `ProMapCargo.sln` — Solution entry point

#### Controllers (`Controllers/`)
HTTP API endpoints for:
- **Routing**: `RoutingController.cs`, `TripRoutingController.cs`
- **Navigation**: `NavigationTelemetryController.cs`
- **Geocoding**: `GeocodingController.cs`
- **Restrictions**: `RestrictionsController.cs`, `RestrictionAdminController.cs`
- **Operations**: `OperationsController.cs`, `DriverController.cs`, `BusinessController.cs`
- **Monitoring**: `AlertsController.cs`
- **Authentication**: `MobileAuthController.cs`

**Rule**: Controllers must remain thin. Business logic belongs in services/repositories.

#### Models (`Models/`)
Data contracts:
- `RouteRequest.cs`, `RouteResponse.cs` — Routing API models
- `TruckProfile.cs`, `VehicleProfile.cs` — Vehicle profiles
- `RouteOptions.cs` — Route options (truck dimensions, weight, ADR)
- `GeoPoint.cs`, `GeocodingResult.cs` — Geocoding models
- `Restriction.cs`, `RoadRestriction.cs` — Restriction definitions
- `BusinessModels.cs`, `MobileAuthModels.cs` — Domain models

#### Routing Layer (`Routing/`)
Truck-aware PostGIS routing engine:
- `PostGisRoutingService.cs` — Route calculation orchestrator
- `PostGisRoutingRepository.cs` — Graph version lookup, edge/node queries
- `PostGisAStarRouter.cs` — A* pathfinding implementation
- `EdgeSnapper.cs` — Snap request/response to graph edges
- `TruckEdgeEvaluator.cs` — Road restrictions, truck constraints evaluation
- `TurnRestrictionMatcher.cs` — Turn restriction validation
- `ManeuverBuilder.cs` — Route maneuver/instruction generation
- `RoutingModels.cs` — Routing domain models

**Primary Behavior**:
1. Validate coordinates via snapping
2. Run A* on PostGIS graph
3. Extract route geometry with snap fractions
4. Build maneuvers
5. Return full route response

**Fallback**: If any step fails, return `NoGraph` and let controller delegate to OSRM.

#### Services (`Services/`)
Core business logic:
- `IRoutingService.cs`, `OsrmRoutingService.cs` — Fallback OSRM routing
- `IGeocodingService.cs`, `NominatimGeocodingService.cs` — Nominatim geocoding
- `IRestrictionEngine.cs`, `PostgresRestrictionEngine.cs`, `RestrictionEngine.cs` — Truck restriction evaluation
- `IRestrictionRepository.cs`, `PostgresRestrictionRepository.cs`, `JsonRestrictionRepository.cs` — Restriction data
- `MobileTokenService.cs`, `ApiAuthHandler.cs` — Mobile authentication & JWT
- `NavigationHub.cs` — SignalR hub for live telemetry
- `CurrentUserContext.cs`, `IdentityClaimsFactory.cs` — ASP.NET Identity integration
- `BusinessServices.cs` — Logistics operations

#### Data (`Data/`)
* `ProMapCargoDbContext.cs` — EF Core DbContext
* `restrictions.json` — Local restriction dataset

#### Razor Pages (`Pages/`)
Server-rendered UI using .NET MAUI-agnostic patterns:
- `Index.cshtml` — Dashboard
- `Navigation/Index.cshtml` — **Critical system** (route calculation, live map, maneuvers, GPS)
- `Dispatch/Index.cshtml` — Fleet dispatch
- `Monitoring/Index.cshtml` — Live monitoring
- `Orders/Index.cshtml`, `Trips/Index.cshtml`, `Vehicles/Index.cshtml`, `Drivers/Index.cshtml` — Business entities
- `Compliance/Index.cshtml`, `Audit/Index.cshtml`, `Finance/Index.cshtml`, `Reports/Index.cshtml` — Analytics
- `Alerts/Index.cshtml`, `Moderation/Index.cshtml` — Operations
- `Profile/Index.cshtml`, `Settings/Index.cshtml`, `Admin/Index.cshtml` — User management
- `Login/Index.cshtml` — Authentication

**No SPA frameworks** (React, Vue, Angular). Prefer Razor Pages + JavaScript (progressive enhancement).

#### Static Assets (`wwwroot/`)

**CSS** (`wwwroot/css/`)
- `base.css`, `site.css` — Global styling
- `animations.css`, `compat.css` — Animations, browser compatibility
- `components.css`, `forms.css`, `tables.css` — Component styles
- `tokens.css` — Design tokens
- `pages/*.css` — Page-specific styles

**JavaScript** (`wwwroot/js/`)
- `app.js` — Application initialization
- `navigation.js` — Navigation page orchestration
- `promap-routing.js` — Routing API client
- `promap-maneuvers.js` — Maneuver rendering
- `promap-gps.js` — Live GPS tracking & off-route detection
- `promap-map-enhancements.js` — Map layer management
- `promap-map-layers.js` — Layer definitions (roads, traffic, restrictions)
- `promap-pmtiles-init.js` — PMTiles initialization
- `alerts.js` — Alert notifications
- `core/*.js` — Core utilities (DOM, events, HTTP, validation, modals, toasts, format)
- `services/alerts.js` — Alert service
- `pages/*.js` — Page-specific logic

**Libraries**
- `lib/leaflet/` — Leaflet map library
- `lib/maplibre-gl/` — MapLibre GL (vector maps)
- `lib/maplibre-gl-leaflet/` — Leaflet + MapLibre bridge
- `lib/pmtiles/` — PMTiles protocol handler
- `lib/signalr/` — SignalR WebSocket client

**Map Assets**
- `maps/serbia.pmtiles` — Serbia vector tiles
- `maps/europe.pmtiles` — Europe vector tiles
- `fonts/Noto Sans Regular/*.pbf` — Glyphs for MapLibre text rendering
- `styles/promap-dark.json` — MapLibre style definition

#### SQL Scripts (`Sql/`)
- `03-routing-graph.sql` — PostGIS routing schema (nodes, edges, restrictions, cells)
- `01-indexes.sql` — Query optimization indexes
- `02-useful-queries.sql` — Diagnostic and maintenance queries
- `04-operational-indexes.sql` — Operational performance indexes

#### Configuration Files
- `NuGet.config` — NuGet package sources
- `.dockerignore` — Docker build exclusions
- `.env` — Docker Compose environment variables
- `libman.json` — Frontend library management

---

### ProMapCargo.Mobile (.NET MAUI Application)

#### Project Structure
* `ProMapCargo.Mobile.csproj` — MAUI project definition

#### Application (`ProMapCargo.Mobile/`)
- `App.xaml`, `App.xaml.cs` — MAUI app root
- `AppShell.xaml`, `AppShell.xaml.cs` — Navigation shell
- `MainPage.xaml`, `MainPage.xaml.cs` — Landing page
- `MauiProgram.cs` — MAUI service configuration

#### Views (`ProMapCargo.Mobile/Views/`)
XAML UI pages:
- `LoginPage.xaml`, `LoginPage.xaml.cs` — Mobile authentication
- `DashboardPage.xaml`, `DashboardPage.xaml.cs` — Fleet dashboard

#### ViewModels (`ProMapCargo.Mobile/ViewModels/`)
MVVM logic:
- `ViewModelBase.cs` — Base class (binding, validation, commands)
- `LoginViewModel.cs` — Login logic
- `DashboardViewModel.cs` — Dashboard state

#### Models (`ProMapCargo.Mobile/Models/`)
Data contracts:
- `AuthModels.cs` — Login/logout
- `BusinessModels.cs` — Fleet operations
- `MobileAppOptions.cs` — Configuration

#### Services (`ProMapCargo.Mobile/Services/`)
Cross-platform services:
- `ApiClient.cs` — HTTP client factory
- `ApiAuthHandler.cs` — JWT token injection
- `MobileSessionService.cs` — Session management
- `TokenStore.cs` — Secure token storage

#### Converters (`ProMapCargo.Mobile/Converters/`)
- `StringNotEmptyConverter.cs` — XAML value converters

#### Resources
- `Resources/Styles/` — XAML theme definitions (`Colors.xaml`, `Styles.xaml`)
- `Resources/Fonts/` — OpenSans TTF fonts
- `Resources/Images/` — App images
- `Resources/Splash/` — Splash screen
- `Resources/Raw/` — Raw text assets

#### Platform-Specific (`ProMapCargo.Mobile/Platforms/`)

**Android** (`Platforms/Android/`)
- `MainActivity.cs` — Activity entry point
- `MainApplication.cs` — Application initialization
- `AndroidManifest.xml` — Manifest
- `Resources/values/colors.xml` — Theme colors

**iOS** (`Platforms/iOS/`)
- `AppDelegate.cs` — Delegate
- `Program.cs` — Entry point
- `Info.plist` — Configuration
- `Resources/PrivacyInfo.xcprivacy` — Privacy policy

**macOS Catalyst** (`Platforms/MacCatalyst/`)
- `AppDelegate.cs`, `Program.cs`
- `Entitlements.plist`, `Info.plist`

**Windows** (`Platforms/Windows/`)
- `App.xaml`, `App.xaml.cs`
- `app.manifest`, `Package.appxmanifest`

---

### ProMapCargo.OsmImporter (OSM Graph Importer)

#### Project Files
* `ProMapCargo.OsmImporter.csproj` — .NET 10 console app

#### Core Components

**Program.cs**
- CLI entry point
- Validates PBF file path
- Resolves database connection string (env vars: `ConnectionStrings__Postgres`, `PROMAP_POSTGRES`)
- Invokes `GraphImporter.ImportAsync()`
- Prints import summary

**GraphImporter.cs** (516 lines)
Main orchestrator:
- **ImportAsync()**: Master workflow
  1. Opens database connection
  2. Executes schema SQL (`03-routing-graph.sql`)
  3. Cleans up old data for version
  4. Reads PBF file; extracts nodes, ways, restrictions
  5. Calls `CopyNodes()`, `CopyWays()`, `CopyRestrictions()`
  6. Updates `routing_graph_versions` status to `ready` with `activated_at`
  7. Logs completion with counts

- **CopyNodes()**: Binary import of OSM nodes → `osm_nodes`
- **CopyWays()**: Binary import of OSM ways → `osm_ways`, `osm_way_nodes`; calls `WriteEdges()` for each way
- **WriteEdges()**: Builds road edges from consecutive way nodes; calls SQL `INSERT` for each edge
- **CopyRestrictions()**: Parses turn restrictions; imports to `turn_restrictions`

**ImportDbContext.cs**
- Legacy EF Core DbContext (may be unused; check for consolidation)

**OsmRestrictionImporter.cs**
- Turn restriction parsing from OSM relations

#### SQL Integration (`Sql/03-routing-graph.sql`)
Importer executes this schema file to create:
- `routing_graph_versions` — Version tracking (status: building → ready)
- `osm_nodes` — Node coordinates
- `osm_ways` — Way metadata
- `osm_way_nodes` — Way node sequences
- `road_edges` — Routable segments (graph edges)
- `turn_restrictions` — Turn prohibitions
- `routing_cells`, `routing_node_cells`, `routing_edge_cells` — Spatial indexing (empty after import; computed later if needed)
- `routing_cell_adjacency`, `routing_boundaries` — Spatial adjacency

#### Database Connection
Default fallback (if env vars not set):
```
Host=localhost;Port=5432;Database=promapcargo;Username=promap;Password=promap_dev_change_me
```

**Important**: When run on Windows host, use `Host=localhost`. When run in Docker, use `Host=postgres` (Docker DNS).

#### Processing Pipeline
1. **PBF Parsing** — OsmSharp reads `.osm.pbf` file; filters by highway tags
2. **Node Extraction** — All node coordinates stored in memory (Dictionary<long, Coordinate>)
3. **Way Filtering** — Only routable highways (motorway, trunk, primary, secondary, tertiary, unclassified, residential, service, track, etc.)
4. **Edge Creation** — For each way, create edges between consecutive nodes
5. **Attribute Preservation** — Truck constraints (maxheight, maxwidth, maxweight, maxaxleload, maxspeed)
6. **Restriction Import** — Turn restrictions from relations (via nodes/ways)
7. **Graph Activation** — Version marked `ready` once all data is imported

#### Key Functions
- `Tags()` — Parse OSM tag dictionary (handles duplicate keys)
- `Direction()` — Determine edge directionality (oneway, access, vehicle)
- `DefaultSpeed()` — Infer speed from highway type
- `HierarchyPenalty()` — Assign routing penalty (truck restrictions increase penalty)
- `Speed()`, `Weight()`, `Num()` — Parse numeric tags (speeds, weights, dimensions)

#### Progress Tracking
- Console output at each major stage: schema, cleanup, version insert, PBF parse, node/way/restriction copy, finalization
- Progress every 10,000 ways and 1,000 restrictions

---

## 5. NAVIGATION PAGE (CRITICAL SYSTEM)

The Navigation page (`Pages/Navigation/Index.cshtml`) is **not a mock**. It is a production feature.

### Required Capabilities
* Start/destination input via geocoding
* Truck profile selection (dimensions, weight, ADR, axles)
* Route calculation (PostGIS primary, OSRM fallback)
* Route alternatives (when available)
* Route geometry (multi-segment with snap-aware extraction)
* Route summary (distance, duration, ETA)
* Truck safety status & warnings
* Maneuver-by-maneuver turn instructions
* Live GPS tracking & telemetry via SignalR
* Off-route detection & automatic rerouting
* PMTiles-based local map (Leaflet + MapLibre GL)
* Layer management (roads, traffic, restrictions, hazmat zones)
* Two-way SignalR comms for dispatch feedback

### Core Files
* **Backend**
  - `Controllers/RoutingController.cs` — Route endpoint
  - `Routing/PostGisRoutingService.cs` — Calculation
  - `Services/OsrmRoutingService.cs` — Fallback

* **Frontend**
  - `Pages/Navigation/Index.cshtml` — Page markup
  - `wwwroot/js/navigation.js` — Page orchestration
  - `wwwroot/js/promap-routing.js` — Routing API client
  - `wwwroot/js/promap-maneuvers.js` — Maneuver rendering
  - `wwwroot/js/promap-gps.js` — GPS & telemetry
  - `wwwroot/js/promap-map-enhancements.js` — Layer management
  - `wwwroot/js/promap-pmtiles-init.js` — PMTiles init

### Do Not
- Reduce Navigation to only drawing two markers
- Use external map providers; stick to local PMTiles + MapLibre
- Avoid overlapping PMTiles initialization paths
- Do not implement SPA-style routing; keep Razor Pages

---

## 6. ROUTING ARCHITECTURE

Routing consists of two engines:

### Primary: PostGIS + OSM Graph

**Service**: `PostGisRoutingService.cs`

**Workflow**:
1. **Edge Snapping** — Snap start/end to nearest road edges (via `EdgeSnapper.cs`)
   - Input: coordinates, truck profile
   - Output: nearest edge IDs, snap fractions (0–1 along edge)
   - Validates truck fit (dimensions, restrictions)

2. **A* Pathfinding** — Find least-cost path (via `PostGisAStarRouter.cs`)
   - Queries graph: edges, turn restrictions, truck constraints
   - Cost eval via `TruckEdgeEvaluator.cs` (speed, restrictions, hierarchy penalty)
   - Turn validation via `TurnRestrictionMatcher.cs`

3. **Route Geometry Assembly** (via `PostGisRoutingService.cs`)
   - Extract from traversed edges
   - Trim first edge at snap fraction (start point)
   - Trim last edge at snap fraction (end point)
   - Concatenate middle edges

4. **Maneuver Building** — Generate turn instructions (via `ManeuverBuilder.cs`)
   - Intersection analysis
   - Direction labels (turn left, turn right, continue)
   - Speeds, distances, ETAs

**Data Layer**: `PostGisRoutingRepository.cs`
- `GetActiveGraphVersionAsync()` — Find `status='ready' AND activated_at NOT NULL`
- `FindNearestEdgesAsync()` — Spatial snapping
- `GetEdgesByIdsAsync()` — Bulk edge retrieval
- All queries use PostGIS geometry operators

**Key Requirement**:
- Always prefer PostGIS when a valid active graph exists
- Return `Code=NoGraph` if graph is `building` or missing
- Let controller handle OSRM fallback

### Fallback: OSRM

**Service**: `OsrmRoutingService.cs`

**When Used**:
- No active PostGIS graph
- Graph snapping fails
- A* routing produces no path
- Recoverable PostGIS errors

**Behavior**:
- Queries local OSRM instance (Docker service)
- Returns compatible route structure
- **Important**: Do NOT claim OSRM is truck-aware; it is standard driving

### Engine Selection Logic (RoutingController.cs)

```csharp
// Pseudo-code
var postgisResult = await _postgisService.CalculateAsync(request, ct);
if (postgisResult.Code == "Success")
	return Ok(postgisResult);  // Use PostGIS result

if (postgisResult.Code == "NoGraph" || postgisResult.Code == "GraphError" || ...)
{
	// Try OSRM fallback
	var osrmResult = await _osrmService.RouteAsync(request, ct);
	return Ok(osrmResult);
}

return BadRequest(postgisResult);
```

**Critical**: Never silently swap engines; always identify which engine produced the result in the response.

---

## 7. RESTRICTION ENGINE

Truck routing must validate:
- **Dimensions**: maxheight, maxwidth, maxlength
- **Weight**: maxweight, maxaxleload
- **Access Tags**: access, vehicle, motor_vehicle, hgv, goods, hazmat
- **Speed Limits**: maxspeed, maxspeed:hgv
- **Turn Restrictions**: no_entry, only_right_turn, no_left_turn, no_u_turn, etc.

### Services
- `IRestrictionEngine.cs` — Public interface
- `PostgresRestrictionEngine.cs` — DB-backed (preferred)
- `RestrictionEngine.cs` — In-memory fallback
- `IRestrictionRepository.cs` — Data access abstraction
- `PostgresRestrictionRepository.cs` — SQL queries
- `JsonRestrictionRepository.cs` — Local JSON fallback (data/restrictions.json)

**Usage in Routing**:
- `TruckEdgeEvaluator.cs` checks edge attributes against truck profile
- Returns infinity cost if truck cannot traverse
- Matching happens during A* expansion

---

## 8. DATABASE SCHEMA (PostGIS)

### Core Tables
- `routing_graph_versions` — (graph_version BIGINT, status TEXT, activated_at TIMESTAMP)
- `osm_nodes` — (id BIGINT, geom GEOMETRY, graph_version BIGINT)
- `osm_ways` — (way_id BIGINT, highway TEXT, tags JSONB, graph_version BIGINT)
- `osm_way_nodes` — (way_id BIGINT, seq INT, node_id BIGINT, graph_version BIGINT)
- `road_edges` — (id BIGINT, source BIGINT, target BIGINT, geom GEOMETRY, length FLOAT, speed_kmh INT, maxheight FLOAT, maxwidth FLOAT, maxweight FLOAT, tags JSONB, graph_version BIGINT)
- `turn_restrictions` — (osm_relation_id BIGINT, restriction TEXT, from_way_id BIGINT, to_way_id BIGINT, via_node_ids BIGINT[], via_way_ids BIGINT[])

### Indexing
- Spatial indexes on `road_edges.geom`, `osm_nodes.geom`
- Primary keys on graph_version, way_id, edge_id
- JSONB operators for tags

### Graph Versions
- Version = Unix timestamp (seconds since epoch)
- Status: `building` → `ready` (set by importer)
- Only `ready` versions with `activated_at NOT NULL` are routable

---

## 9. DOCKER COMPOSE STACK

Local development stack (`docker-compose.yml`):
- `promap-postgres` — PostgreSQL 15 + PostGIS
- `promap-osrm` — OSRM service (pre-built road network)
- `promap-api` — ASP.NET Core API

**Environment Variables** (`.env`):
- `ConnectionStrings__Postgres` — API database connection
- `OSRM_URL` — OSRM service endpoint
- `NOMINATIM_URL` — Nominatim geocoding

**Important**: 
- Container-to-container: use service name (`postgres`), not `localhost`
- Host-to-container: use `localhost` + published port
- Importer runs on host, so use `Host=localhost` in fallback

---

## 10. NAMING CONVENTIONS

### C# Code
- **Classes/Methods**: PascalCase
- **Properties/Fields**: camelCase (private), PascalCase (public)
- **Async Methods**: Suffix `Async` (e.g., `RouteAsync`, `ImportAsync`)
- **Interfaces**: Prefix `I` (e.g., `IRoutingService`, `IRestrictionEngine`)
- **Enums**: PascalCase

### Database
- **Tables**: snake_case (e.g., `road_edges`, `osm_nodes`)
- **Columns**: snake_case
- **Constraints**: {table}_{type}_{column} (e.g., `road_edges_pk_id`)

### File Structure
- **Controllers**: `{Feature}Controller.cs`
- **Services**: `{Feature}Service.cs`, `I{Feature}Service.cs`
- **Repositories**: `{Feature}Repository.cs`, `I{Feature}Repository.cs`
- **Models**: `{Concept}Model.cs` or just `{Concept}.cs`
- **Pages**: `{FeatureName}/Index.cshtml`
- **JavaScript**: kebab-case modules (e.g., `promap-routing.js`)

---

## 11. CODE STANDARDS

### ASP.NET Core / C#
- **Modern C# 14.0** features (file-scoped namespaces, records for DTOs, switch expressions, raw strings)
- **Nullable reference types**: Enabled (`<Nullable>enable</Nullable>`)
- **Async/await**: End-to-end (no sync-over-async); pass `CancellationToken`
- **Dependency injection**: Constructor injection preferred
- **Validation**: Input validation at controller/service boundary
- **Error handling**: Specific exception types; log with context

### Razor Pages
- **Keep pages thin**: Move logic to page models and services
- **Progressive enhancement**: JavaScript enhances server rendering; not required for core functionality

### JavaScript
- **No jQuery**: Use modern ES6+ (fetch, async/await, classes, modules)
- **Module pattern**: Each file exports one logical unit
- **Event delegation**: Minimize global script execution
- **DOM queries**: Cache selectors where possible
- **Async operations**: Always use `await` for promises
- **Naming**: camelCase for variables/functions, CONSTANT_CASE for constants

### SQL
- **Indexes**: Add for frequently queried columns (Graph queries are I/O heavy)
- **Parameterized queries**: Always use query parameters (prevent SQL injection)
- **Window functions**: Use for analytics (ROW_NUMBER, RANK, LAG/LEAD)

### Testing
- **Unit tests**: Xunit framework
- **Mock external dependencies**: Database, HTTP services, file I/O
- **Test naming**: `WhenConditionThenBehavior` pattern
- **No static state**: Each test must be independent

---

## 12. KEY ARCHITECTURAL DECISIONS

1. **PostGIS is the primary map engine** — not OSRM. OSRM is fallback for compatibility.
2. **Local PMTiles** — Do not switch to external tile providers (Google, Mapbox); data is sovereign.
3. **No SPA** — Razor Pages + JavaScript. No React, Vue, Blazor unless explicitly requested.
4. **MAUI, not Forms** — No Xamarin Forms; use MAUI equivalents for mobile.
5. **Graph versioning** — Support multiple routing graphs; only one can be `ready` at a time.
6. **Truck awareness** — Routing must consider dimensions, weight, ADR, restricted roads.
7. **Fallback routing** — Always graceful degradation to OSRM if PostGIS fails.
8. **Live telemetry** — SignalR for driver-to-dispatch communication; not just passive monitoring.

---

## 13. DEBUGGING & DIAGNOSTICS

### Routing Issues
1. **"opet se mapa i ruta ne slazu"** (route/map misalignment)
   - Check `PostGisRoutingService.CalculateAsync()` snap fractions
   - Verify route geometry extraction logic (first/last edge trimming)
   - Ensure `EdgeSnapper` correctly maps coordinates to edges

2. **"NoGraph" response**
   - Check `routing_graph_versions` table: does an active row exist?
   - Must have `status='ready' AND activated_at IS NOT NULL`
   - If not, run importer again

3. **Importer hangs or fails**
   - Check database connection string (host name, port, credentials)
   - Run from Windows host → use `Host=localhost`
   - Run from Docker → use `Host=postgres`
   - Check console output for `[IMPORT]` debug logs
   - Monitor database: `SELECT * FROM routing_graph_versions ORDER BY graph_version DESC;`

### Map Tile Issues
- Missing glyphs/fonts → check `/fonts/{fontstack}/{range}.pbf` endpoint
- Blank map → check PMTiles path and MapLibre initialization
- Layer visibility → check `promap-map-layers.js` and MapLibre style JSON

### Performance
- Slow routing → check `road_edges` indexes, `ST_DWithin()` queries
- Large import time → profile `CopyWays()` binary import, edge creation SQL
- Memory spike → check `DrawDownAsync()` in import (29M nodes loaded)

---

## 14. COMPLETE PROJECT MAP

### ProMapCargo.Api

```
ProMapCargo.Api/
├── Program.cs                           ← Entry point
├── ProMapCargo.Api.csproj
├── appsettings.json
├── NuGet.config
├── docker-compose.yml
├── Dockerfile
│
├── Controllers/                         ← HTTP endpoints
│   ├── RoutingController.cs            ← Route calculation
│   ├── TripRoutingController.cs
│   ├── GeocodingController.cs
│   ├── NavigationTelemetryController.cs
│   ├── RestrictionsController.cs
│   ├── RestrictionAdminController.cs
│   ├── OperationsController.cs
│   ├── BusinessController.cs
│   ├── DriverController.cs
│   ├── AlertsController.cs
│   └── MobileAuthController.cs
│
├── Routing/                             ← PostGIS routing engine
│   ├── PostGisRoutingService.cs         ← Orchestrator
│   ├── PostGisRoutingRepository.cs      ← Graph queries
│   ├── PostGisAStarRouter.cs            ← Pathfinding
│   ├── EdgeSnapper.cs                   ← Coordinate snapping
│   ├── TruckEdgeEvaluator.cs            ← Cost calculation
│   ├── TurnRestrictionMatcher.cs        ← Restriction validation
│   ├── ManeuverBuilder.cs               ← Turn instructions
│   └── RoutingModels.cs                 ← Domain models
│
├── Services/                            ← Business services
│   ├── IRoutingService.cs, OsrmRoutingService.cs    ← OSRM fallback
│   ├── IGeocodingService.cs, NominatimGeocodingService.cs
│   ├── IRestrictionEngine.cs, PostgresRestrictionEngine.cs, RestrictionEngine.cs
│   ├── IRestrictionRepository.cs, PostgresRestrictionRepository.cs, JsonRestrictionRepository.cs
│   ├── MobileTokenService.cs, ApiAuthHandler.cs
│   ├── NavigationHub.cs                 ← SignalR telemetry
│   ├── CurrentUserContext.cs
│   ├── IdentityClaimsFactory.cs
│   └── BusinessServices.cs
│
├── Models/                              ← Data contracts
│   ├── RouteRequest.cs, RouteResponse.cs
│   ├── TruckProfile.cs, VehicleProfile.cs
│   ├── RouteOptions.cs
│   ├── GeoPoint.cs, GeocodingResult.cs
│   ├── Restriction.cs, RoadRestriction.cs
│   ├── BusinessModels.cs
│   ├── MobileAuthModels.cs
│   └── MobileAuthOptions.cs
│
├── Data/                                ← EF Core
│   ├── ProMapCargoDbContext.cs
│   └── restrictions.json
│
├── Sql/                                 ← Database scripts
│   ├── 03-routing-graph.sql
│   ├── 01-indexes.sql
│   ├── 02-useful-queries.sql
│   └── 04-operational-indexes.sql
│
├── Pages/                               ← Razor Pages UI
│   ├── Index.cshtml                     ← Dashboard
│   ├── Navigation/Index.cshtml          ← **Route & live map**
│   ├── Dispatch/Index.cshtml
│   ├── Monitoring/Index.cshtml
│   ├── Orders/Index.cshtml
│   ├── Trips/Index.cshtml
│   ├── Vehicles/Index.cshtml
│   ├── Drivers/Index.cshtml
│   ├── Driver/Index.cshtml
│   ├── Alerts/Index.cshtml
│   ├── Compliance/Index.cshtml
│   ├── Audit/Index.cshtml
│   ├── Finance/Index.cshtml
│   ├── Reports/Index.cshtml
│   ├── Moderation/Index.cshtml
│   ├── Settings/Index.cshtml
│   ├── Profile/Index.cshtml
│   ├── Admin/Index.cshtml
│   ├── Login/Index.cshtml
│   ├── Shared/_Layout.cshtml
│   ├── _ViewImports.cshtml
│   └── _ViewStart.cshtml
│
└── wwwroot/                             ← Static assets
	├── css/
	│   ├── base.css, site.css
	│   ├── animations.css, compat.css
	│   ├── components.css, forms.css, tables.css
	│   ├── tokens.css
	│   └── pages/                       ← Per-page styles
	│
	├── js/
	│   ├── app.js                       ← Init
	│   ├── navigation.js                ← Navigation orchestration
	│   ├── promap-routing.js            ← Routing API client
	│   ├── promap-maneuvers.js          ← Maneuver rendering
	│   ├── promap-gps.js                ← GPS tracking
	│   ├── promap-map-enhancements.js   ← Layer control
	│   ├── promap-map-layers.js         ← Layer definitions
	│   ├── promap-pmtiles-init.js       ← PMTiles init
	│   ├── alerts.js
	│   ├── core/                        ← Utility modules
	│   │   ├── dom.js
	│   │   ├── events.js
	│   │   ├── http.js
	│   │   ├── modal.js
	│   │   ├── toast.js
	│   │   ├── validation.js
	│   │   ├── format.js
	│   │   └── compat.js
	│   ├── services/
	│   │   └── alerts.js
	│   └── pages/                       ← Page-specific JS
	│       ├── index.js
	│       ├── dispatch-index.js
	│       ├── monitoring-index.js
	│       ├── orders-index.js
	│       ├── finance-index.js
	│       ├── drivers-index.js
	│       └── vehicles-index.js
	│
	├── lib/
	│   ├── leaflet/                     ← Leaflet map library
	│   ├── maplibre-gl/                 ← MapLibre GL (vector)
	│   ├── maplibre-gl-leaflet/         ← Bridge layer
	│   ├── pmtiles/                     ← PMTiles protocol
	│   └── signalr/                     ← SignalR WebSocket
	│
	├── maps/
	│   ├── serbia.pmtiles               ← Vector tiles
	│   └── europe.pmtiles
	│
	├── fonts/
	│   └── Noto Sans Regular/
	│       ├── 0-255.pbf                ← MapLibre glyphs
	│       ├── 256-511.pbf
	│       └── 1024-1279.pbf
	│
	└── styles/
		└── promap-dark.json             ← MapLibre style
```

### ProMapCargo.Mobile

```
ProMapCargo.Mobile/
├── ProMapCargo.Mobile.csproj            ← MAUI project
│
├── App.xaml, App.xaml.cs                ← App root
├── AppShell.xaml, AppShell.xaml.cs      ← Navigation
├── MainPage.xaml, MainPage.xaml.cs      ← Landing
├── MauiProgram.cs                       ← DI setup
│
├── Views/                               ← XAML UI
│   ├── LoginPage.xaml, LoginPage.xaml.cs
│   └── DashboardPage.xaml, DashboardPage.xaml.cs
│
├── ViewModels/                          ← MVVM state
│   ├── ViewModelBase.cs
│   ├── LoginViewModel.cs
│   └── DashboardViewModel.cs
│
├── Models/                              ← Data contracts
│   ├── AuthModels.cs
│   ├── BusinessModels.cs
│   └── MobileAppOptions.cs
│
├── Services/                            ← Cross-platform services
│   ├── ApiClient.cs
│   ├── ApiAuthHandler.cs
│   ├── MobileSessionService.cs
│   └── TokenStore.cs
│
├── Converters/                          ← XAML converters
│   └── StringNotEmptyConverter.cs
│
├── Resources/                           ← App resources
│   ├── Styles/
│   │   ├── Colors.xaml
│   │   └── Styles.xaml
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
└── Platforms/                           ← Platform-specific
	├── Android/
	│   ├── MainActivity.cs
	│   ├── MainApplication.cs
	│   ├── AndroidManifest.xml
	│   └── Resources/values/colors.xml
	├── iOS/
	│   ├── AppDelegate.cs
	│   ├── Program.cs
	│   ├── Info.plist
	│   └── Resources/PrivacyInfo.xcprivacy
	├── MacCatalyst/
	│   ├── AppDelegate.cs
	│   ├── Program.cs
	│   ├── Entitlements.plist
	│   └── Info.plist
	└── Windows/
		├── App.xaml, App.xaml.cs
		├── app.manifest
		└── Package.appxmanifest
```

### ProMapCargo.OsmImporter

```
Importer/
├── ProMapCargo.OsmImporter.csproj       ← Console app
│
├── Program.cs                           ← CLI entry
│   ├── Validates PBF file
│   ├── Resolves DB connection
│   ├── Invokes GraphImporter.ImportAsync()
│   └── Prints summary
│
├── GraphImporter.cs                     ← Orchestrator (516 lines)
│   ├── ImportAsync()
│   │   ├── Open connection
│   │   ├── Execute schema (03-routing-graph.sql)
│   │   ├── Cleanup old version
│   │   ├── Insert graph_version (status='building')
│   │   ├── Read PBF
│   │   ├── CopyNodes()
│   │   ├── CopyWays()
│   │   ├── CopyRestrictions()
│   │   └── Update status to 'ready'
│   ├── CopyNodes() — Binary import osm_nodes
│   ├── CopyWays() — Binary import osm_ways, osm_way_nodes; calls WriteEdges
│   ├── WriteEdges() — SQL INSERT each road_edges row
│   ├── CopyRestrictions() — Turn restrictions
│   └── Helpers (Tags(), Direction(), Speed(), Weight(), etc.)
│
├── ImportDbContext.cs                   ← Legacy EF
│
├── OsmRestrictionImporter.cs            ← Restriction parsing
│
└── (Sql/03-routing-graph.sql)           ← Schema (created at runtime)
```

---

## 15. MIGRATION & DEPLOYMENT NOTES

### Development
1. Ensure `.NET 10 SDK` installed
2. Run `dotnet build` (all projects)
3. Start `docker-compose up` (Postgres + OSRM)
4. Run `dotnet run --project Importer -- osm/serbia-latest.osm.pbf` (populates graph)
5. Run `dotnet run --project ProMapCargo.Api` (starts API on `http://localhost:5000`)
6. Navigate to `Pages/Navigation/Index.cshtml`

### Testing
- Unit tests: `xunit` framework
- Integration tests: Real Postgres, PostGIS
- E2E: Selenium or Playwright on Razor Pages

### Production
- Build Docker image: `docker build -t promapcargo .`
- Push to registry
- Deploy via Docker Compose or Kubernetes
- Ensure PostGIS + OSRM availability
- Pre-load graph version before rollout

---

**Last updated**: This document consolidates all three projects (API, Mobile, Importer) with complete file tree, responsibilities, and architecture into one authoritative reference.
