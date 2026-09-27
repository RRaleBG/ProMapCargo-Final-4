# ProMap Cargo - .NET Development Skill

## Stack

- C#
- ASP.NET Core
- .NET
- Entity Framework Core
- PostgreSQL
- PostGIS
- Dependency Injection
- Options pattern
- Controllers
- Razor Pages
- Background Services
- Docker
- Logging
- Health Checks

## Rules

Before modifying code:

1. Search the existing project.
2. Identify the existing architecture.
3. Reuse existing services and models.
4. Do not invent duplicate classes.
5. Do not introduce unnecessary NuGet packages.
6. Preserve existing API contracts.
7. Check dependency injection registrations.
8. Check configuration in appsettings.json.
9. Check nullable reference warnings.
10. Run dotnet build after changes.

## ProMap routing

Routing architecture:

PostGIS local graph
        |
        v
local truck-aware routing
        |
        | failure / unavailable
        v
OSRM fallback

Important configuration:

Routing:PreferPostGis
Routing:OsrmBaseUrl
Routing:MaxExpandedStates
Routing:SnapRadiusMeters

## Required behavior

When debugging routing:

1. Inspect RouteRequest.
2. Inspect routing controller.
3. Inspect routing service.
4. Inspect PostGIS graph.
5. Inspect OSRM fallback.
6. Inspect dependency injection.
7. Inspect configuration.
8. Run build/tests.
9. Report the actual root cause.
10. Only then propose changes.
