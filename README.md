# ProMap Cargo – Final

Truck-aware transport logistics and navigation platform built with ASP.NET Core 9, PostgreSQL/PostGIS, OSM/PBF import, Leaflet and SignalR.

## Final architecture

There is **one PostgreSQL/PostGIS database**: `promapcargo`.

- `ProMapCargo.Api` uses `ProMapCargoDbContext` for application/Identity/business data.
- `ProMapCargo.OsmImporter` uses Npgsql/PostGIS for bulk OSM graph import.
- Both use the same `ConnectionStrings:Postgres` database connection.
- `ConnectionStrings__Postgres` is the preferred environment-variable override.
- `PROMAP_POSTGRES` remains supported by the importer for backward compatibility.

## Included

- ASP.NET Core 10 Web API + Razor Pages.
- ASP.NET Identity with company/tenant context.
- PostgreSQL/PostGIS database initialization at startup.
- Idempotent demo seed: company, administrator, roles, vehicles, drivers, order, stops, trip and demo restrictions.
- Versioned PostGIS routing graph.
- OSM PBF importer for nodes, ways, way-nodes, road edges and turn-restriction relations.
- Truck-aware edge evaluation for dimensions, weight, axle load, HGV, goods, hazmat and access tags.
- PostGIS routing with a truck-aware Dijkstra state search and OSRM compatibility fallback.
- Route persistence and driver dispatch workflow.
- SignalR navigation telemetry hub.
- Leaflet-based command-center UI.
- Dockerfile and Docker Compose for API + PostGIS.
- GitHub Actions build workflow.

## Packages

The solution explicitly references the required spatial Npgsql plugins. In particular, `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` is required for `UseNetTopologySuite`, and `Npgsql.NetTopologySuite` provides the Npgsql spatial integration. These packages are compatible with the selected Npgsql 9.0.x provider line.

## Upgrading from the previous archive

The previous archive could have created tables with the old PostgreSQL naming convention. For a clean final installation, reset the development volume once:

`docker compose down -v`

Then run `docker compose up --build`. The final version uses PostgreSQL snake_case naming consistently.

## Run locally

1. Start PostGIS:

   `docker compose up -d postgres`

2. Restore and build:

   `dotnet restore ProMapCargo.sln`
   `dotnet build ProMapCargo.sln`

3. Start the API:

   `dotnet run --project ProMapCargo.Api.csproj`

4. Open the application and check:

   `http://localhost:5090`

   Health endpoint:

   `http://localhost:5000/health`

The API automatically creates the EF Core database schema, enables PostGIS, creates the routing schema/indexes and performs the demo seed on first startup.

## Docker

Run the complete application stack:

`docker compose up --build`

API:

`http://localhost:8080`

PostGIS:

`localhost:5432`, database `promapcargo`, user `promap`.

## OSM routing graph import

Place an `.osm.pbf` file in `./osm` and run:

`dotnet run --project Importer -- ./osm/europe-latest.osm.pbf`

Optionally provide a graph version:

`dotnet run --project Importer -- ./osm/europe-latest.osm.pbf 20260912`

The importer creates a versioned graph, imports OSM nodes/ways/edges/restrictions and activates the completed graph only after the import succeeds.

## Important routing note

The PostGIS graph must contain an imported OSM PBF before the truck-aware router can calculate a real graph route. If no active PostGIS graph exists, the API attempts the configured OSRM compatibility fallback.

## Seed administrator

Development defaults:

- Email: `admin@promapcargo.local`
- Password: `Admin123!`

Override these with:

- `Seed__AdminEmail`
- `Seed__AdminPassword`
- `Seed__Enabled`

Do not keep the development password in a production deployment.

## Database connection

Local default:

`Host=localhost;Port=5432;Database=promapcargo;Username=promap;Password=promap_dev_change_me`

Docker overrides it automatically with:

`ConnectionStrings__Postgres=Host=postgres;Port=5432;Database=promapcargo;Username=promap;Password=promap_dev_change_me`

This is intentionally a **single database** design. The API and OSM importer do not use separate application/routing databases.
