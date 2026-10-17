# ProMap Cargo: open work

Kept up to date after every review. Each item: what is wrong, where, and the proposed fix.
Nothing here is done without the developer's go-ahead.

Last review: 2026-10-17 (previous: 2026-10-15). Mobile pass done the same day.

## Mobile: done 2026-10-17 (not committed), tested on the emulator (Pixel 7, API 36)

- [x] One start-up screen: the system splash is only the background colour; `MainPage` shows the web truck-on-route animation, the truck follows the real 0-100 % start-up. Old `Views/StartupPage` removed.
- [x] No page keeps its own `Resources`: shared styles in `Resources/Styles/Styles.xaml`, converters in `App.xaml`.
- [x] Sign-in screen (show/hide password, "Nastavi bez prijave", server address); token refresh before expiry and after a 401 (`ApiAuthHandler`, `MobileSessionService`); logout in Podešavanja.
- [x] Home screen with the driver's trip from `/api/driver/portal`: next stop (navigate, arrived, complete), all stops, start trip, break, incident, route from dispatch (accept/reject), call dispatcher. Saved on the phone for no signal. The demo Belgrade-Novi Sad route request is gone.
- [x] GPS for dispatch: `GpsTracker` (one shared listener), `LocationTrackingService` (foreground service, works with the screen off), `GpsTelemetryUploader` (10 s driving / 60 s standing, queue on the phone without signal). Verified: positions arrive in `vehicle_position_events`, also with the screen off and after the server was down.
- [x] Offline maps: installed part shown without signal, delete, sizes, free space, cancel download; navigation page and map style stored at start-up for dead zones.
- [x] Settings that work (location for dispatch, voice, screen on), server connection test, cache clear, version.
- [x] Manifest: `allowBackup=false`, `POST_NOTIFICATIONS`, dialer query. App icon and web favicon: the truck tile from the web home page.
- [x] Server (needed by the phone): `CookieOrBearer` scheme in `Program.cs`, extra fields in `/api/driver/portal`.
- [ ] Test on the real phone (Galaxy SM-S928B) with the Release build; the installed app there is signed with another key (one uninstall, then download the maps again).
- [ ] `ViewModels/NavigationViewModel.cs` (about 2100 lines): split into route guidance, voice and offline routing (GPS already moved to `GpsTracker`).
- [ ] Driver actions (arrive, complete) need a connection; queueing them offline would need a server-side timestamp field (API change).

## P1: security and correctness

1. **Privilege escalation in user admin.** `Pages/Account/Admin/Index.cshtml.cs:39-63`: `[Authorize(Policy=...)]` on handlers is ignored (build warning MVC1001); only `users.read` applies. Fix: check the policy in each handler through `IAuthorizationService`.
2. **Pages and APIs without sign-in.** 14 pages (Alerts, Audit, Compliance, Dispatch, Driver, Drivers, Finance, Moderation, Monitoring, Orders, Reports, Trips, Trips/new, Vehicles) and the controllers Alerts, Business, TruckBans have no `[Authorize]`. Fix: global `FallbackPolicy` (authenticated user), `[AllowAnonymous]` on Login/Register/ForgotPassword/ResetPassword/Lockout/AccessDenied, public files, map packages, offline bundles and mobile-auth. The developer asked on 2026-10-13 to leave `[Authorize]` off for now: ask before changing.
3. **`POST /api/routing/route` is anonymous and can run up to 300 s.** Fix: ASP.NET rate limiter on routing (and geocoding).
4. **docker-compose is a dev setup:** `ASPNETCORE_ENVIRONMENT=Development`, `MobileAuth__SigningKey` and `POSTGRES_PASSWORD` in git, `Seed__Enabled=true` (admin@admin.com / Admin123!). Fix: `docker-compose.prod.yml` with secrets from `.env`, no seed.
5. **Refresh tokens stored in plain text** (`Services/MobileTokenService.cs:44`, lookup at :72). Fix: store the SHA-256 hash. Schema unchanged (same column), existing phones sign in again once.

## P2: mobile

6. `usesCleartextTraffic="true"` stays: the server is on the company LAN over HTTP at an address the driver types in. With HTTPS on the server it can go.
7. Remaining `async void`: only `SafeUi.Run` itself and navigation page handlers that already catch.

## P3: maintenance

10. **No test project.** Candidates: `PostGisAStarRouter`, `TruckEdgeEvaluator`, `OfflineRouter` with a small test graph (xUnit).
11. **`.git` is 3 GB.** History has `pmtiles.exe`, a Notepad++ installer under `Pages/`, `.github/.tools/.store`. Still tracked: `.github/.tools/dotnet-script.exe` and `.playwright-mcp/*` (already in `.gitignore`, needs `git rm -r --cached`). History rewrite (`git filter-repo`) only with explicit consent.
12. **Local `dotnet run` does not match CLAUDE.md:** `.env` uses `Host=postgres`, `appsettings.json` has `OsrmBaseUrl` `:5090` while OSRM runs on `:5001`. Fix: `appsettings.Development.json` with localhost values.
13. **Build warnings:** `KnownNetworks` obsolete (`Program.cs:35`, use `KnownIPNetworks`), `IndexModel.Page` hides `PageModel.Page()`, CS8602 in `RoutingController.cs:564`, `IpAddressLocationService.cs:58`, `OsrmRoutingService.cs:30`, unused `hub` in `BusinessServices.cs:9`.
14. **npm dev tooling:** 7 advisories (5 high, 2 moderate: `braces`, `postcss-selector-parser`) in the Tailwind build chain. Not shipped to users; update Tailwind/PostCSS. GitHub Dependabot alert #2 not readable with the current token.

## Refactoring candidates (larger, discuss first)

15. `wwwroot/js/navigation.js` (5003 lines) and `wwwroot/js/pages/monitoring-index.js` (2088): split into ES modules (map, route panel, telemetry).
16. `wwwroot/css/promap.css` still 19,400 lines: split per page or component, drop duplicates (`.pm-kpi` variants).
17. `Pages/Account/Manage/Index.cshtml` (1808), `Pages/Alerts/Index.cshtml` (1188), `Pages/Vehicles/New.cshtml` (1151): move repeated markup into partials.
18. `Routing/PostGisAStarRouter.cs` (1012): separate graph loading, search and path assembly; covered by tests from item 10 first.
