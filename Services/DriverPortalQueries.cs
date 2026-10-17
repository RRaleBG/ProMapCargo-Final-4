using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>Portal data, or null when the account is not linked to a driver.</summary>
public sealed record DriverPortal(object? Data);

/// <summary>Data of the /driver portal: active trip, stops and the dispatch phone.</summary>
public sealed class DriverPortalQueries(ProMapCargoDbContext db, ICurrentUserContext current)
{
    /// <summary>Null when the request has no company context.</summary>
    public async Task<DriverPortal?> PortalAsync(CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;
        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == c && x.UserId == current.UserId, ct);
        if (driver is null) return new DriverPortal(null);

        var trip = await ActiveTripAsync(c, driver.Id, ct);
        var stops = trip is null ? [] : await db.TransportStops.AsNoTracking()
            .Where(x => x.CompanyId == c && x.TransportOrderId == trip.TransportOrderId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(ct);
        var next = stops.FirstOrDefault(x => x.Status is TransportStopStatus.Planned or TransportStopStatus.Arrived or TransportStopStatus.InService);
        var vehicle = trip?.VehicleId is { } vehicleId ? await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == vehicleId, ct) : null;
        var phone = await db.Companies.AsNoTracking().Where(x => x.Id == c).Select(x => x.DispatchPhone).FirstOrDefaultAsync(ct);
        var costs = trip is null
            ? []
            : await db.TransportCosts.AsNoTracking()
                .Where(x => x.CompanyId == c && x.TripId == trip.Id)
                .OrderByDescending(x => x.IncurredAt ?? x.CreatedAt)
                .ToListAsync(ct);
        var historyTrips = trip is null
            ? []
            : await db.Trips.AsNoTracking()
                .Where(x => x.CompanyId == c && x.Id != trip.Id && (x.DriverId == driver.Id || (vehicle != null && x.VehicleId == vehicle.Id)))
                .OrderByDescending(x => x.StartedAt ?? x.PlannedStartAt ?? x.CreatedAt)
                .Take(12)
                .ToListAsync(ct);
        var historyOrderIds = historyTrips.Select(x => x.TransportOrderId).Distinct().ToList();
        var historyOrders = historyOrderIds.Count == 0
            ? new Dictionary<Guid, TransportOrder>()
            : await db.TransportOrders.AsNoTracking().Where(x => x.CompanyId == c && historyOrderIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x, ct);
        var historyStops = historyOrderIds.Count == 0
            ? []
            : await db.TransportStops.AsNoTracking().Where(x => x.CompanyId == c && historyOrderIds.Contains(x.TransportOrderId)).OrderBy(x => x.Sequence).ToListAsync(ct);
        var routeId = trip?.ActiveRouteId ?? (trip is null ? null : await db.TripRoutes.AsNoTracking().Where(x => x.CompanyId == c && x.TripId == trip.Id).OrderByDescending(x => x.ActivatedAt ?? x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct));
        var routeCountries = routeId is Guid activeRouteId
            ? await db.RouteCountrySegments.AsNoTracking().Where(x => x.CompanyId == c && x.TripRouteId == activeRouteId).OrderBy(x => x.Sequence).ToListAsync(ct)
            : [];
        var allTrips = await db.Trips.AsNoTracking()
            .Where(x => x.CompanyId == c && x.DriverId == driver.Id)
            .OrderByDescending(x => x.StartedAt ?? x.PlannedStartAt ?? x.CreatedAt)
            .Take(25)
            .ToListAsync(ct);
        var allTripIds = allTrips.Select(x => x.Id).ToList();
        var allTripCosts = allTripIds.Count == 0
            ? []
            : await db.TransportCosts.AsNoTracking().Where(x => x.CompanyId == c && x.TripId != null && allTripIds.Contains(x.TripId.Value)).OrderByDescending(x => x.IncurredAt ?? x.CreatedAt).ToListAsync(ct);
        var repairs = vehicle is null
            ? []
            : await db.VehicleLogEntries.AsNoTracking().Where(x => x.CompanyId == c && x.VehicleId == vehicle.Id).OrderByDescending(x => x.OccurredAt).Take(12).ToListAsync(ct);
        // A route sent by dispatch that the driver has not answered yet (accept / reject in the app).
        var pendingRoute = await db.RouteDispatches.AsNoTracking()
            .Where(x => x.CompanyId == c && x.DriverId == driver.Id && (x.Status == RouteDispatchStatus.Pending || x.Status == RouteDispatchStatus.Sent))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.TripId, Status = x.Status.ToString(), x.CreatedAt, x.SentAt })
            .FirstOrDefaultAsync(ct);

        var rolling = BuildRollingEuStay(routeCountries, stops);
        var spent = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment)).Sum(x => x.Amount);
        var driverPaid = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.PaymentMethod is "cash" or "driver-card").Sum(x => x.Amount);
        var allowances = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.Category == TransportCostCategory.Allowance).Sum(x => x.Amount);
        var paidOut = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.Category == TransportCostCategory.Payment).Sum(x => x.Amount);

        return new DriverPortal(new
        {
            Driver = new { driver.Id, driver.FullName, driver.EmployeeNumber, Status = driver.Status.ToString(), driver.CurrentVehicleId },
            Trip = trip is null ? null : new
            {
                trip.Id,
                trip.TripNumber,
                Status = trip.Status.ToString(),
                State = trip.ExecutionState.ToString(),
                trip.RouteProgressPercent,
                trip.PlannedArrivalAt,
                Route = stops.Count == 0 ? "" : (stops[0].City ?? stops[0].Name) + " → " + (stops[^1].City ?? stops[^1].Name),
                Vehicle = vehicle?.Registration,
                trip.VehicleId,
            },
            Stops = stops.Select(x => new
            {
                x.Id, x.Sequence, Type = x.Type.ToString(), Status = x.Status.ToString(), x.Name, x.City, x.CountryCode,
                x.PlannedAt, x.ArrivedAt, x.CompletedAt, x.Notes,
                x.Address, x.Latitude, x.Longitude,
            }),
            NextStopId = next?.Id,
            DispatchPhone = phone,
            PendingRoute = pendingRoute,
            Notes = ExtractTripNotes(stops),
            Financial = trip is null ? null : new
            {
                Currency = trip.Currency,
                trip.BudgetAmount,
                SpentAmount = spent,
                DriverPaidAmount = driverPaid,
                AllowanceAmount = allowances,
                PaidOutAmount = paidOut,
                PendingAmount = costs.Where(x => x.Status == TransportCostStatus.Pending).Sum(x => x.Amount),
                Costs = costs.Select(x => new { x.Id, Category = x.Category.ToString(), Status = x.Status.ToString(), x.Amount, x.Currency, x.PaymentMethod, x.CardLabel, x.Location, x.CountryCode, At = x.IncurredAt ?? x.CreatedAt, x.Notes })
            },
            EuStay = rolling,
            DriverHistory = historyTrips.Where(x => x.DriverId == driver.Id).Select(x => new
            {
                x.Id,
                x.TripNumber,
                OrderNumber = historyOrders.TryGetValue(x.TransportOrderId, out var order) ? order.OrderNumber : null,
                Route = BuildRouteLabel(historyStops, x, historyOrders),
                x.StartedAt,
                x.CompletedAt,
                Status = x.Status.ToString(),
            }),
            VehicleHistory = vehicle is null ? [] : historyTrips.Where(x => x.VehicleId == vehicle.Id).Select(x => new
            {
                x.Id,
                x.TripNumber,
                OrderNumber = historyOrders.TryGetValue(x.TransportOrderId, out var order) ? order.OrderNumber : null,
                Route = BuildRouteLabel(historyStops, x, historyOrders),
                x.StartedAt,
                x.CompletedAt,
                Status = x.Status.ToString(),
            }),
            Trips = allTrips.Select(x => new
            {
                x.Id,
                x.TripNumber,
                Status = x.Status.ToString(),
                State = x.ExecutionState.ToString(),
                Route = BuildRouteLabel(historyStops.Concat(stops).ToList(), x, historyOrders),
                x.PlannedStartAt,
                x.PlannedArrivalAt,
                x.StartedAt,
                x.CompletedAt,
                x.RouteProgressPercent,
                Budget = x.BudgetAmount,
                Spent = allTripCosts.Where(cost => cost.TripId == x.Id && cost.Status != TransportCostStatus.Rejected && cost.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment)).Sum(cost => cost.Amount),
                Costs = allTripCosts.Where(cost => cost.TripId == x.Id).Select(cost => new { cost.Id, Category = cost.Category.ToString(), Status = cost.Status.ToString(), cost.Amount, cost.Currency, cost.PaymentMethod, cost.CardLabel, cost.Location, cost.CountryCode, At = cost.IncurredAt ?? cost.CreatedAt, cost.Notes })
            }),
            VehicleRepairs = repairs.Select(x => new { x.Id, Kind = x.Kind.ToString(), x.Title, x.Description, x.OccurredAt, x.OdometerKm, x.Cost, x.Currency, Status = x.Status.ToString(), x.Severity, x.Workshop, x.ResolvedAt }),
        });
    }

    /// <summary>The driver's running trip, else the next planned one.</summary>
    public Task<Trip?> ActiveTripAsync(Guid c, Guid driverId, CancellationToken ct, bool tracked = false)
    {
        var trips = tracked ? db.Trips : db.Trips.AsNoTracking();
        return trips.Where(x => x.CompanyId == c && x.DriverId == driverId && (x.Status == TripStatus.Active || x.Status == TripStatus.Paused || x.Status == TripStatus.Planned))
            .OrderByDescending(x => x.Status == TripStatus.Active || x.Status == TripStatus.Paused)
            .ThenBy(x => x.PlannedStartAt)
            .FirstOrDefaultAsync(ct);
    }

    private static string? ExtractTripNotes(IEnumerable<TransportStop> stops)
    {
        var notes = stops.Where(x => !string.IsNullOrWhiteSpace(x.Notes)).Select(x => $"{x.Sequence}. {x.Name}: {x.Notes}").ToList();
        return notes.Count == 0 ? null : string.Join(Environment.NewLine, notes);
    }

    private static string BuildRouteLabel(IReadOnlyList<TransportStop> allStops, Trip trip, IReadOnlyDictionary<Guid, TransportOrder> orders)
    {
        var orderStops = allStops.Where(x => x.TransportOrderId == trip.TransportOrderId).ToList();
        if (orderStops.Count > 0)
        {
            return (orderStops[0].City ?? orderStops[0].Name) + " → " + (orderStops[^1].City ?? orderStops[^1].Name);
        }

        return orders.TryGetValue(trip.TransportOrderId, out var order)
            ? order.OrderNumber
            : "Ruta nije dostupna";
    }

    private static object BuildRollingEuStay(IReadOnlyList<RouteCountrySegment> segments, IReadOnlyList<TransportStop> stops)
    {
        var euCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT",
            "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE"
        };
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = today.AddDays(-179);
        var euDays = new HashSet<DateOnly>();

        foreach (var segment in segments.Where(x => euCountries.Contains(x.CountryCode)))
        {
            var entryAt = segment.EstimatedEntry ?? segment.EstimatedExit ?? DateTimeOffset.UtcNow;
            var exitAt = segment.EstimatedExit ?? segment.EstimatedEntry ?? DateTimeOffset.UtcNow;
            var entry = DateOnly.FromDateTime(entryAt.UtcDateTime.Date);
            var exit = DateOnly.FromDateTime(exitAt.UtcDateTime.Date);
            for (var day = entry; day <= exit; day = day.AddDays(1))
            {
                if (day >= start && day <= today)
                {
                    euDays.Add(day);
                }
            }
        }

        foreach (var stop in stops.Where(x => x.CountryCode is { Length: > 0 } code && euCountries.Contains(code)))
        {
            var moment = stop.CompletedAt ?? stop.ArrivedAt ?? stop.PlannedAt;
            if (moment is null)
            {
                continue;
            }

            var day = DateOnly.FromDateTime(moment.Value.UtcDateTime.Date);
            if (day >= start && day <= today)
            {
                euDays.Add(day);
            }
        }

        var daysUsed = euDays.Count;
        var remaining = Math.Max(0, 90 - daysUsed);
        var status = remaining switch
        {
            <= 0 => "Limit je dostignut",
            <= 10 => $"Preostaje još {remaining} dana u EU",
            _ => $"Iskorišćeno {daysUsed} od 90 dana u poslednjih 180"
        };

        return new
        {
            DaysUsed = daysUsed,
            DaysRemaining = remaining,
            WindowStart = start,
            WindowEnd = today,
            StatusText = status,
            Countries = segments.Select(x => x.CountryCode).Where(euCountries.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }
}
