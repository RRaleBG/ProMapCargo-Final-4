# ProMap Cargo: open work

Kept up to date after every review. Each item: what is wrong, where, and the proposed fix.
Nothing here is done without the developer's go-ahead.

Last review: 2026-10-17 evening, full web + mobile pass (previous: 2026-10-15).

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
- [x] Installed on the real phone (Galaxy SM-S928B, Debug build): sign-in, trip and tracking service work. Release build still to do (nothing to lose now: the app was not installed).
- [ ] `ViewModels/NavigationViewModel.cs` (about 2100 lines): split into route guidance, voice and offline routing (GPS already moved to `GpsTracker`).
- [ ] Driver actions (arrive, complete) need a connection; queueing them offline would need a server-side timestamp field (API change).

## Driver, vehicle, trip and order logic: done 2026-10-19 (not committed)

- [x] Driver and trailer are put on a vehicle in one place only: /vehicles, "Dodela vozača i prikolice" (`PUT /api/business/vehicles/{id}/assignment`). Removed from Novo vozilo, Novi vozač and the user admin. Open trips and orders of the driver follow him to the new vehicle.
- [x] A driver profile linked to an account takes name and e-mail from the account (Novi vozač: account picker, `?userId=`); admin edits keep the linked profile in sync.
- [x] Every order creates its trip (and an older order without one gets it on save); the trip follows the order's driver, vehicle and planned times.
- [x] Order: transport price, currency, driver's share (%), cash advance (`Sql/10-order-finance.sql`, adds columns only). Trip details show earning and advance settlement (advance minus the driver's own business expenses, cash or own card); the phone shows them too.
- [x] Click on a trip row opens the details again (`core/modal.js` never added `.open`). A driver sees only his own trips, without price, budget, driver notes or vehicle history.
- [ ] Data: account vozac@vozac.com was linked to the profile "Stefan Ilić" through the old admin form (audit 2026-10-18 23:36 UTC), Stefan's e-mail was overwritten with vozac@vozac.com. Fix only with the developer's go-ahead: unlink, restore stefan.ilic@promapcargo.rs, create the profile from the account on Novi vozač.
- [x] Driver view checked as vozac@vozac.com (seed password): only his trip, no price/percent/budget/vehicle history, assignment refused (409).
- [ ] "Dodaj trošak" from the trip details (Finance expense form preset to the trip) not done.

## P1: security and correctness

0. **User admin (`Services/AccountAdminService.cs`), new 2026-10-17:**
   - `GetDashboardAsync` lists the users of **every company** (no `CompanyId` filter); `ToggleActiveAsync`, `ToggleLockAsync`, `SetRoleAsync` load the target user without the company check, so an administrator of one firm can lock or change users of another.
   - `CreateUserAsync` / `UpdateUserAsync` assign `RoleNames` with only `users.manage`: a user manager can still make someone (or himself) Administrator. Role changes there must need `roles.manage`, and Administrator only from an Administrator.
   - Role permissions are global (roles are shared), so `permissions.manage` in one company changes every company.
1. ~~Handlers without permission checks~~: fixed, each handler now checks its permission.
2. **Pages and APIs without sign-in.** 14 pages (Alerts, Audit, Compliance, Dispatch, Driver, Drivers, Finance, Moderation, Monitoring, Orders, Reports, Trips, Trips/new, Vehicles) and the controllers Alerts, Business, TruckBans have no `[Authorize]`. Fix: global `FallbackPolicy` (authenticated user), `[AllowAnonymous]` on Login/Register/ForgotPassword/ResetPassword/Lockout/AccessDenied, public files, map packages, offline bundles and mobile-auth. The developer asked on 2026-10-13 to leave `[Authorize]` off for now: ask before changing.
3. **`POST /api/routing/route` is anonymous and can run up to 300 s.** Fix: ASP.NET rate limiter on routing (and geocoding).
4. **docker-compose is a dev setup:** `ASPNETCORE_ENVIRONMENT=Development`, `MobileAuth__SigningKey` and `POSTGRES_PASSWORD` in git, `Seed__Enabled=true` (admin@admin.com / Admin123!). Fix: `docker-compose.prod.yml` with secrets from `.env`, no seed.
5. **Refresh tokens stored in plain text** (`Services/MobileTokenService.cs:44`, lookup at :72). Fix: store the SHA-256 hash. Schema unchanged (same column), existing phones sign in again once.

## P2: mobile

6. `usesCleartextTraffic="true"` stays: the server is on the company LAN over HTTP at an address the driver types in. With HTTPS on the server it can go.
7. Remaining `async void`: `SafeUi.Run`, `NavigationPage.OnOpenServerSettingsClicked`, `TripDetailsViewModel.OpenReceipt`.
8. **Voice guidance talks every few seconds:** `NavigationViewModel.SpeakNextPromptAsync` runs on every GPS fix and speaks whenever the distance bucket changes (every 50 m below 1 km, every 250 m above): at 50 km/h that is every 3-4 s. Fix: speak at fixed points (2 km, 1 km, 400 m, 150 m, at the turn) once per maneuver; the voice button becomes on/off (mute), stored in `AppSettings.VoiceGuidance`.
9. **Expense receipt from the camera never works:** `MediaPicker.CapturePhotoAsync` needs `android.permission.CAMERA` and the `IMAGE_CAPTURE` query in the manifest; the exception is swallowed, so the expense goes without a receipt. "Galerija/PDF" only offers images (`FilePickerFileType.Images`). Amount is always EUR. The 7 dialogs in a row should be one form page; expenses could queue offline (they carry their own `IncurredAt`).
10. **Receipt opens in the system browser** (`TripDetailsViewModel.OpenReceipt`): `api/files/{id}` needs the app's token, the browser gets 401. Fix: download with `ApiClient` and open the local file (`Launcher.OpenAsync`).
11. Requested 2026-10-17, not done yet: arrival at a stop automatic at 200 m; `splash.svg` with a 0-100 % bar; one icon set across web and mobile; voice on/off (item 8); all proposals from the 2026-10-17 list (truck profile from the trip vehicle, bans in routing, route through all stops, proof of delivery, push for new routes, driving-time rules, speed limits, night map, error log, update check, HTTPS, battery saving).

## P2: web (browser pass 2026-10-17, admin, 1600 px and 390 px)

- All 30 pages load, no script errors, no text under 12 px.
- `/` (public home) is wider than a phone (429 px at 390 px): the decorative `.pm-bg-glow`, `.pm-routing-orbit`, `.pm-route-river` stick out. Fix: `overflow-x: clip` on the page wrapper.
- Role names in English in user admin ("Dodaj Dispatcher", "Driver", "Moderator") and "Status"/"Loading" labels on Trips, Orders, Vehicles/New, Drivers/New, Compliance.
- `/driver` for a non-driver account logs a 404 in the console (the page handles it; the API could answer 200 with `isDriver:false`).
- Build is broken repeatedly by a second API started from `bin\Debug` (`dotnet run` on :8090 by Copilot): run it with `-c Release`.

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
