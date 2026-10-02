# ProMap Cargo — .NET Engineering

## Rules
- Preserve project boundaries: Web → Contracts; Api → Contracts/Core/Infrastructure; Core and Contracts remain dependency-light.
- Prefer existing abstractions over duplicating services.
- Keep configuration in `appsettings*.json` and environment variables.
- Do not hard-code production secrets.
- After edits run `dotnet restore`, `dotnet build`, and relevant tests.
- For routing changes run integration tests and inspect API logs.
- For JS/map changes also validate browser-console errors.

## Common regression
If `RouteRequest` reports missing `Truck`, `Target`, `DepartureAt`, or `Profile`, inspect the contract and all callers together rather than patching a single compile site.
