using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Routing;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Controllers;

[ApiController]
[Route("api/routing")]
public sealed class RoutingController(
    IPostGisRoutingService postGis,
    IRoutingService legacy,
    IRestrictionEngine restrictions,
    ILogger<RoutingController> logger)
    : ControllerBase
{
    [HttpPost("route")]
    [AllowAnonymous]
    public async Task<ActionResult<RouteResponse>> Route(
        [FromBody] RouteRequest request,
        CancellationToken ct)
    {
        if (!IsValidPoint(request.Start))
        {
            return BadRequest(new
            {
                code = "InvalidStart",
                message = "Start mora sadržati validne geografske koordinate."
            });
        }

        if (!IsValidPoint(request.Target))
        {
            return BadRequest(new
            {
                code = "InvalidDestination",
                message = "Destination mora sadržati validne geografske koordinate."
            });
        }

        var profile =
            string.IsNullOrWhiteSpace(request.Profile)
                ? "truck"
                : request.Profile.Trim().ToLowerInvariant();

        var normalizedRequest = new RouteRequest
        {
            Start = request.Start,
            Destination = request.Target,
            Profile = profile,
            AvoidRestricted = request.AvoidRestricted,
            Truck = NormalizeTruckProfile(
                profile,
                request.Truck),
            DepartureAt = request.DepartureAt
        };

        logger.LogInformation(
            "Routing request: {Profile} {StartLat},{StartLon} -> {EndLat},{EndLon}",
            profile,
            normalizedRequest.Start.Lat,
            normalizedRequest.Start.Lon,
            normalizedRequest.Target.Lat,
            normalizedRequest.Target.Lon
        );

        /*
         * ============================================================
         * 1. PRIMARY ENGINE: POSTGIS / OSM GRAPH
         * ============================================================
         */

        if (profile.Equals(
                "truck",
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var postGisResult =
                    await postGis.CalculateAsync(
                        normalizedRequest,
                        ct);

                if (postGisResult.Code.Equals(
                        "Ok",
                        StringComparison.OrdinalIgnoreCase) &&
                    postGisResult.Routes.Count > 0)
                {
                    logger.LogInformation(
                        "PostGIS routing succeeded. Distance={Distance}m Duration={Duration}s",
                        postGisResult.Routes[0].Distance,
                        postGisResult.Routes[0].Duration
                    );

                    return Ok(postGisResult);
                }

                logger.LogWarning(
                    "PostGIS routing did not produce a route. Code={Code}. Falling back to OSRM.",
                    postGisResult.Code
                );
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "PostGIS routing failed. Falling back to OSRM."
                );
            }
        }

        /*
         * ============================================================
         * 2. FALLBACK ENGINE: OSRM
         * ============================================================
         */

        OsrmResponse osrm;

        try
        {
            osrm =
                await legacy.RouteAsync(
                    normalizedRequest,
                    ct);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "OSRM routing failed."
            );

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code = "RoutingUnavailable",
                    message = "Routing servis trenutno nije dostupan.",
                    details = ex.Message
                });
        }

        if (osrm is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code = "RoutingUnavailable",
                    message = "Routing servis nije vratio odgovor."
                });
        }

        if (!string.Equals(
                osrm.Code,
                "Ok",
                StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    code = "OsrmError",
                    message = "OSRM nije uspešno izračunao rutu.",
                    osrmCode = osrm.Code
                });
        }

        if (osrm.Routes is null ||
            osrm.Routes.Count == 0)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    code = "NoRoute",
                    message = "OSRM nije pronašao rutu."
                });
        }

        /*
         * ============================================================
         * 3. OSRM -> RouteCandidate
         * ============================================================
         */

        var routeCandidates =
            new List<RouteCandidate>();

        foreach (var route in osrm.Routes)
        {
            var points =
                ExtractPoints(route.Geometry);

            IReadOnlyList<RestrictionViolation> violations;

            if (profile.Equals(
                    "truck",
                    StringComparison.OrdinalIgnoreCase))
            {
                violations =
                    await restrictions.AnalyzeAsync(
                        points,
                        normalizedRequest.Truck,
                        normalizedRequest.DepartureAt,
                        ct);
            }
            else
            {
                violations = [];
            }

            var violationList =
                violations.ToList();

            var restricted =
                violationList.Count > 0;

            var score =
                restricted
                    ? Math.Max(
                        0,
                        100 - violationList.Count * 20)
                    : 100;

            routeCandidates.Add(
                new RouteCandidate
                {
                    Distance = route.Distance,
                    Duration = route.Duration,
                    Geometry = route.Geometry,
                    Legs = route.Legs,

                    Analysis =
                        new RouteAnalysis
                        {
                            Restricted = restricted,
                            Score = score,
                            Violations = violationList
                        }
                });
        }

        if (routeCandidates.Count == 0)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    code = "NoRoute",
                    message = "Routing engine nije vratio rutu."
                });
        }

        /*
         * ============================================================
         * 4. SELECT BEST ROUTE
         * ============================================================
         */

        var selectedRouteIndex =
            routeCandidates
                .Select(
                    (route, index) => new
                    {
                        Route = route,
                        Index = index
                    })
                .OrderByDescending(
                    x =>
                        request.AvoidRestricted
                            ? x.Route.Analysis.Score
                            : 0)
                .ThenBy(
                    x => x.Route.Duration)
                .Select(
                    x => x.Index)
                .First();

        var selectedRoute =
            routeCandidates[selectedRouteIndex];

        var selectedViolations =
            selectedRoute.Analysis.Violations;

        var isTruckSafe =
            !profile.Equals(
                "truck",
                StringComparison.OrdinalIgnoreCase)
            || selectedViolations.Count == 0;

        /*
         * ============================================================
         * 5. RESPONSE
         * ============================================================
         */

        var departure =
            normalizedRequest.DepartureAt
            ?? DateTimeOffset.UtcNow;

        var summary =
            new RouteSummary(
                selectedRoute.Distance,
                selectedRoute.Duration,
                departure.AddSeconds(
                    selectedRoute.Duration));

        var response =
            new RouteResponse
            {
                Code = "Ok",

                Routes =
                    routeCandidates,

                SelectedRouteIndex =
                    selectedRouteIndex,

                IsTruckSafe =
                    isTruckSafe,

                Violations =
                    selectedViolations,

                Summary =
                    summary,

                Diagnostics =
                    new RouteDiagnostics
                    {
                        Engine = "OSRM",
                        UsedFallback = true,
                        ExpandedStates = 0,
                        GraphVersion = null,
                        FailureReason = null
                    },

                Maneuvers = []
            };

        logger.LogInformation(
            "OSRM fallback succeeded. Routes={Routes}, Selected={Selected}, Distance={Distance}m, Duration={Duration}s, TruckSafe={TruckSafe}",
            routeCandidates.Count,
            selectedRouteIndex,
            selectedRoute.Distance,
            selectedRoute.Duration,
            isTruckSafe
        );

        return Ok(response);
    }

    private static bool IsValidPoint(
        GeoPoint? point)
    {
        if (point is null)
        {
            return false;
        }

        return
            double.IsFinite(point.Lat) &&
            double.IsFinite(point.Lon) &&
            point.Lat >= -90 &&
            point.Lat <= 90 &&
            point.Lon >= -180 &&
            point.Lon <= 180;
    }

    private static TruckProfile? NormalizeTruckProfile(
        string profile,
        TruckProfile? truck)
    {
        if (!string.Equals(profile, "truck", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        truck ??= new TruckProfile();

        var grossWeight = truck.GrossWeightTons > 0 ? truck.GrossWeightTons : 40m;
        var height = truck.HeightMeters > 0 ? truck.HeightMeters : 4m;
        var width = truck.WidthMeters > 0 ? truck.WidthMeters : 2.55m;
        var length = truck.LengthMeters > 0 ? truck.LengthMeters : 16.5m;
        var axleLoad = truck.AxleLoadTons is > 0 ? truck.AxleLoadTons : 10m;
        var axles = truck.Axles > 0 ? truck.Axles : 5;
        var maxSpeed = truck.MaxSpeedKmh > 0 ? truck.MaxSpeedKmh : 90m;

        return new TruckProfile
        {
            GrossWeightTons = grossWeight,
            HeightMeters = height,
            WidthMeters = width,
            LengthMeters = length,
            AxleLoadTons = axleLoad,
            Axles = axles,
            IsHgv = truck.IsHgv,
            Commercial = truck.Commercial,
            Hazmat = truck.Hazmat,
            Goods = truck.Goods,
            AdrClass = truck.AdrClass,
            VehicleClass = string.IsNullOrWhiteSpace(truck.VehicleClass)
                ? "HeavyGoods"
                : truck.VehicleClass,
            MaxSpeedKmh = maxSpeed,
        };
    }

    private static List<GeoPoint> ExtractPoints(
        object? geometry)
    {
        if (geometry is null)
        {
            return [];
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    JsonSerializer.Serialize(
                        geometry));

            return ExtractPointsFromJson(
                document.RootElement);
        }
        catch
        {
            return [];
        }
    }

    private static List<GeoPoint> ExtractPointsFromJson(
        JsonElement root)
    {
        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            return [];
        }

        if (root.TryGetProperty(
                "geometry",
                out var featureGeometry) &&
            featureGeometry.ValueKind ==
                JsonValueKind.Object)
        {
            root = featureGeometry;
        }

        if (!root.TryGetProperty(
                "coordinates",
                out var coordinates))
        {
            return [];
        }

        if (coordinates.ValueKind !=
            JsonValueKind.Array)
        {
            return [];
        }

        if (root.TryGetProperty(
                "type",
                out var typeElement) &&
            typeElement.ValueKind ==
                JsonValueKind.String)
        {
            var type =
                typeElement.GetString();

            if (string.Equals(
                    type,
                    "MultiLineString",
                    StringComparison.OrdinalIgnoreCase))
            {
                var result =
                    new List<GeoPoint>();

                foreach (
                    var line
                    in coordinates.EnumerateArray())
                {
                    result.AddRange(
                        ParseLineString(line));
                }

                return result;
            }
        }

        return ParseLineString(
            coordinates);
    }

    private static List<GeoPoint> ParseLineString(
        JsonElement coordinates)
    {
        var result =
            new List<GeoPoint>();

        foreach (
            var coordinate
            in coordinates.EnumerateArray())
        {
            if (coordinate.ValueKind !=
                    JsonValueKind.Array ||
                coordinate.GetArrayLength() < 2)
            {
                continue;
            }

            var lon =
                coordinate[0].GetDouble();

            var lat =
                coordinate[1].GetDouble();

            if (!double.IsFinite(lat) ||
                !double.IsFinite(lon))
            {
                continue;
            }

            result.Add(
                new GeoPoint(
                    lat,
                    lon));
        }

        return result;
    }
}