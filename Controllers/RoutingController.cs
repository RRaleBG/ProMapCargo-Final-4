using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Routing;
using ProMapCargo.Api.Services;
using System.Text.Json;

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
    private static readonly TimeSpan PostGisRoutingBudget = TimeSpan.FromSeconds(300);

    private static readonly TimeSpan OsrmFallbackBudget = TimeSpan.FromSeconds(30);

    [HttpPost("route")]
    [AllowAnonymous]
    public async Task<ActionResult<RouteResponse>> Route([FromBody] RouteRequest request, CancellationToken ct)
    {
        var resolvedStart = request.ResolvedStart;
        var resolvedTarget = request.Target;

        if (!IsValidPoint(resolvedStart))
        {
            return BadRequest(new
            {
                code = "InvalidStart",
                message = "Start mora sadržati validne geografske koordinate."
            });
        }

        if (!IsValidPoint(resolvedTarget))
        {
            return BadRequest(new
            {
                code = "InvalidDestination",
                message = "Destination mora sadržati validne geografske koordinate."
            });
        }

        var profile = string.IsNullOrWhiteSpace(request.Profile) ? "truck" : request.Profile.Trim().ToLowerInvariant();

        var normalizedRequest = new RouteRequest
        {
            Start = resolvedStart,
            Destination = resolvedTarget,
            Profile = profile,
            AvoidRestricted = request.AvoidRestricted,
            Truck = NormalizeTruckProfile(profile, request.ResolvedTruck),
            DepartureAt = request.DepartureAt
        };

        logger.LogInformation(
            "Routing request: Profile={Profile} Start={StartLat},{StartLon} Destination={EndLat},{EndLon} AvoidRestricted={AvoidRestricted} TruckWeightT={TruckWeightT} TruckHeightM={TruckHeightM} TruckWidthM={TruckWidthM} TruckLengthM={TruckLengthM} TruckAxleLoadT={TruckAxleLoadT} TruckAxles={TruckAxles} TruckMaxSpeedKmh={TruckMaxSpeedKmh}",
            profile,
            normalizedRequest.Start.Lat,
            normalizedRequest.Start.Lon,
            normalizedRequest.Target.Lat,
            normalizedRequest.Target.Lon,
            normalizedRequest.AvoidRestricted,
            normalizedRequest.Truck?.GrossWeightTons,
            normalizedRequest.Truck?.HeightMeters,
            normalizedRequest.Truck?.WidthMeters,
            normalizedRequest.Truck?.LengthMeters,
            normalizedRequest.Truck?.AxleLoadTons,
            normalizedRequest.Truck?.Axles,
            normalizedRequest.Truck?.MaxSpeedKmh
        );

        /*
         * ============================================================
         * 1. PRIMARY ENGINE: POSTGIS / OSM GRAPH
         * ============================================================
         */

        RouteResponse? postGisResult = null;

        {
            using var postGisCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            postGisCts.CancelAfter(PostGisRoutingBudget);

            try
            {
                postGisResult = await postGis.CalculateAsync(normalizedRequest, postGisCts.Token);

                if (postGisResult is null)
                {
                    postGisResult = new RouteResponse
                    {
                        Code = "GraphError",
                        IsTruckSafe = false,
                        Diagnostics = new RouteDiagnostics
                        {
                            Engine = "PostGIS-AStar",
                            UsedFallback = false,
                            FailureReason = "PostGIS routing service returned null response."
                        }
                    };
                }

                if (postGisResult.Code.Equals("Ok", StringComparison.OrdinalIgnoreCase) && postGisResult.Routes.Count > 0)
                {
                    var postGisRoute = postGisResult.Routes[postGisResult.SelectedRouteIndex];
                    logger.LogInformation(
                        "Routing response: Engine={Engine} UsedFallback={UsedFallback} GraphVersion={GraphVersion} SelectedRouteIndex={SelectedRouteIndex} TraversalCount={TraversalCount} Distance={Distance} Duration={Duration} FailureReason={FailureReason}",
                        postGisResult.Diagnostics.Engine,
                        postGisResult.Diagnostics.UsedFallback,
                        postGisResult.Diagnostics.GraphVersion,
                        postGisResult.SelectedRouteIndex,
                        postGisResult.Diagnostics.TraversalCount,
                        postGisRoute.Distance,
                        postGisRoute.Duration,
                        postGisResult.Diagnostics.FailureReason);
                    return Ok(postGisResult);
                }

                logger.LogWarning("PostGIS routing did not produce a route. Code={Code}. FailureReason={FailureReason}.",
                    postGisResult.Code,
                    postGisResult.Diagnostics.FailureReason
                );
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                postGisResult = new RouteResponse
                {
                    Code = "RoutingError",
                    Message = $"PostGIS routing exceeded the {PostGisRoutingBudget.TotalSeconds:0.#} s budget.",
                    IsTruckSafe = false,
                    Diagnostics = new RouteDiagnostics
                    {
                        Engine = "PostGIS-AStar",
                        UsedFallback = false,
                        FailureReason = $"PostGIS routing exceeded the {PostGisRoutingBudget.TotalSeconds:0.#} s budget."
                    }
                };

                logger.LogWarning("PostGIS routing exceeded the {BudgetSeconds}s budget.",
                    PostGisRoutingBudget.TotalSeconds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PostGIS routing failed.");
                postGisResult = new RouteResponse
                {
                    Code = "RoutingError",
                    Message = ex.Message,
                    IsTruckSafe = false,
                    Diagnostics = new RouteDiagnostics
                    {
                        Engine = "PostGIS-AStar",
                        UsedFallback = false,
                        FailureReason = ex.Message
                    }
                };
            }
        }

        if (postGisResult is null)
        {
            postGisResult = new RouteResponse
            {
                Code = "RoutingError",
                Message = "PostGIS routing service returned null response.",
                IsTruckSafe = false,
                Diagnostics = new RouteDiagnostics
                {
                    Engine = "PostGIS-AStar",
                    UsedFallback = false,
                    FailureReason = "PostGIS routing service returned null response."
                }
            };
        }

        logger.LogWarning(
            "PostGIS routing did not produce a route. Code={Code}. FailureReason={FailureReason}.",
            postGisResult.Code,
            postGisResult.Diagnostics.FailureReason
        );

        /*
         * ============================================================
         * 2. FALLBACK ENGINE: OSRM
         * ============================================================
         */

        OsrmResponse osrm;

        using var osrmCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        osrmCts.CancelAfter(OsrmFallbackBudget);

        try
        {
            osrm =
                await legacy.RouteAsync(
                    normalizedRequest,
                    osrmCts.Token);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "OSRM routing exceeded the {BudgetSeconds}s budget.",
                OsrmFallbackBudget.TotalSeconds
            );

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code = "RoutingTimeout",
                    message = "Routing servis nije odgovorio u zadatom vremenu.",
                    details = $"OSRM routing exceeded the {OsrmFallbackBudget.TotalSeconds:0.#} s budget.",
                    postGisCode = postGisResult?.Code,
                    postGisFailureReason = postGisResult?.Diagnostics.FailureReason,
                    postGisDiagnostics = postGisResult?.Diagnostics
                });
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
                    details = ex.Message,
                    postGisCode = postGisResult?.Code,
                    postGisFailureReason = postGisResult?.Diagnostics.FailureReason,
                    postGisDiagnostics = postGisResult?.Diagnostics
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
                            Violations = violationList,
                            Debug =
                                new RouteDebug
                                {
                                    Summary = restricted
                                        ? $"OSRM candidate with {violationList.Count} truck restriction issue(s)."
                                        : "OSRM candidate without detected truck restriction issues.",
                                    TraversalCount = 0,
                                    Highlights =
                                    [
                                        $"candidate distance {route.Distance:F0} m",
                                        $"candidate duration {route.Duration:F0} s"
                                    ]
                                }
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
                Message = "OSRM fallback route calculated.",

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
                        FailureReason = null,
                        TraversalCount = 0,
                        Highlights =
                        [
                            $"evaluated {routeCandidates.Count} OSRM candidate(s)",
                            $"selected candidate {selectedRouteIndex}",
                            $"selected route restricted={selectedRoute.Analysis.Restricted}"
                        ]
                    },

                Maneuvers = []
            };

        logger.LogInformation(
            "Routing response: Engine={Engine} UsedFallback={UsedFallback} GraphVersion={GraphVersion} SelectedRouteIndex={SelectedRouteIndex} TraversalCount={TraversalCount} Distance={Distance} Duration={Duration} FailureReason={FailureReason}",
            response.Diagnostics.Engine,
            response.Diagnostics.UsedFallback,
            response.Diagnostics.GraphVersion,
            response.SelectedRouteIndex,
            response.Diagnostics.TraversalCount,
            selectedRoute.Distance,
            selectedRoute.Duration,
            response.Diagnostics.FailureReason
        );

        return Ok(response);
    }

    private static RouteResponse BuildEmergencyFallbackResponse(
        RouteRequest request,
        RouteResponse? postGisResult,
        string reason)
    {
        var start = request.Start;
        var target = request.Target;

        var distanceMeters = EstimateHaversineMeters(start.Lat, start.Lon, target.Lat, target.Lon);
        var durationSeconds = Math.Max(60, distanceMeters / 16.6667d);

        var geometry = new Dictionary<string, object?>
        {
            ["type"] = "LineString",
            ["coordinates"] = new[]
            {
                new[] { start.Lon, start.Lat },
                new[] { target.Lon, target.Lat }
            }
        };

        var candidate = new RouteCandidate
        {
            Distance = distanceMeters,
            Duration = durationSeconds,
            Geometry = geometry,
            Legs = null,
            Analysis = new RouteAnalysis
            {
                Restricted = false,
                Score = 40,
                Violations = [],
                Debug = new RouteDebug
                {
                    Summary = "Emergency straight-line fallback route.",
                    StartSnap = null,
                    EndSnap = null,
                    TraversalCount = 0,
                    Highlights =
                    [
                        "PostGIS and OSRM were unavailable or timed out.",
                        reason
                    ]
                }
            }
        };

        var departure = request.DepartureAt ?? DateTimeOffset.UtcNow;

        return new RouteResponse
        {
            Code = "Ok",
            Routes = [candidate],
            SelectedRouteIndex = 0,
            IsTruckSafe = false,
            Violations = [],
            Summary = new RouteSummary(distanceMeters, durationSeconds, departure.AddSeconds(durationSeconds)),
            Diagnostics = new RouteDiagnostics
            {
                Engine = "EmergencyFallback",
                UsedFallback = true,
                ExpandedStates = 0,
                GraphVersion = postGisResult?.Diagnostics?.GraphVersion,
                FailureReason = reason,
                StartSnap = postGisResult?.Diagnostics?.StartSnap,
                EndSnap = postGisResult?.Diagnostics?.EndSnap,
                TraversalCount = 0,
                Highlights =
                [
                    "Returned straight-line fallback geometry.",
                    $"PostGIS code: {postGisResult?.Code ?? "n/a"}",
                    reason
                ]
            },
            Maneuvers = []
        };
    }

    private static double EstimateHaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6_371_000d;

        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);

        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return earthRadiusMeters * c;
    }

    private static double ToRadians(double degrees)
        => degrees * Math.PI / 180d;

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