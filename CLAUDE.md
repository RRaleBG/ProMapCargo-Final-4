# ProMap Cargo

Truck navigation and fleet platform: ASP.NET Core API + web UI, PostgreSQL/PostGIS routing graph,
OSRM fallback, and a .NET MAUI Android app with offline truck routing.
The developer works on Windows (PowerShell, `C:\Users\rale_\Desktop\ProMapCargo-Final-4`) and talks in Serbian;
UI text in the app is Serbian.

`.github/copilot-instructions.md` holds the detailed project rules (architecture, routing, maps, UI).
Read the relevant section before larger changes.

## Projects

| Path | What |
|---|---|
| `ProMapCargo.Api.csproj` (root: `Controllers/`, `Services/`, `Routing/`, `Pages/`, `wwwroot/`) | ASP.NET Core 10 API, Razor Pages, SignalR `NavigationHub` |
| `Routing/` | PostGIS truck router (`PostGisAStarRouter`, `TruckEdgeEvaluator`, `RoutingGraphCache`) |
| `Importer/` | OSM PBF -> PostGIS routing graph (versioned `graph_version`) |
| `OfflineRouting/` | Shared offline router library (graph bundle, place index), used by the mobile app |
| `ProMapCargo.Mobile/` | .NET MAUI app, `net10.0-android`, app id `rs.promapcargo.mobile` |
| `Tools/OfflineBundleBuilder/` | Builds offline region packages from Geofabrik PBFs (`regions.json`) |
| `Tools/release/` | Android release signing and build scripts |
| `Sql/` | Schema and maintenance SQL |

## Run

```powershell
docker compose up -d postgres osrm      # PostGIS on localhost:5432 (db promapcargo, user promap), OSRM on :5001
dotnet build ProMapCargo.sln
dotnet run --project ProMapCargo.Api.csproj
```

- Admin login (dev, seeded in `Program.cs` `SeedAdministratorUserAsync`): `admin@promapcargo.com` / `Admin1234!`
- The phone reaches the API via the PC's LAN address (default `http://192.168.1.2:8080/`, changeable in the app's Settings; emulator uses `10.0.2.2`).

## Mobile

```powershell
dotnet build ProMapCargo.Mobile -f net10.0-android
pwsh Tools/release/build-release.ps1 -Install            # signed release APK, installs via adb
pwsh Tools/release/build-release.ps1 -Aab -Version 1.1 -Build 2
```

The release key lives in `%USERPROFILE%\.promap\` (password DPAPI-protected), never in the repo.

## Offline regions

```powershell
pwsh Tools/OfflineBundleBuilder/build-region.ps1 -Region zapadni-balkan   # or: all, srednja-evropa, koridor-10, istocna-evropa
```

Output goes to `wwwroot/offline-bundles` and `wwwroot/maps`, served by the API; the app lists them in "Offline mape".

## Coverage

The server graph (PostGIS + OSRM) covers Serbia only. Routes outside it return 422 `OutOfCoverage`,
and the app then routes with an installed offline region.

## Data not in git

OSM PBFs (`osm/`), PMTiles (`wwwroot/maps/`), OSRM data, offline packages and DB volumes are git-ignored.
They exist locally even though they are missing on GitHub: check `.gitignore` before treating a file as missing.

## Conventions

- Reuse existing services and models. Do not add a parallel implementation or an external map provider.
- Do not change API contracts or the DB schema silently.
- Commit messages: short English summary line, body explaining why.

## MCP servers (`.mcp.json`)

Tools for all of them: `pwsh scripts/install-dev-tools.ps1` (winget).

- `postgres`: read-only SQL queries (runs through npx, needs Node.js). Defaults to the local docker-compose database; override with `PROMAP_DATABASE_URI`, e.g.
  `setx PROMAP_DATABASE_URI "postgresql://promap:<password>@host:5432/promapcargo"`.
- `mobile`: drives the phone or emulator through adb (needs Node.js and `adb` on PATH).
- `context7`: current library docs (MAUI, Npgsql, NetTopologySuite).
- `github-mcp-server`: uses the `GITHUB_TOKEN` environment variable (same token `gh` uses).
