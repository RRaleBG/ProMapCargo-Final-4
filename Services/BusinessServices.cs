using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class BusinessService(ProMapCargoDbContext db, ICurrentUserContext current, IHubContext<NavigationHub> hub, AuditWriter audit)
{
    private Guid GetRequiredCompanyId() => current.CompanyId ?? throw new UnauthorizedAccessException("Company context is required.");

    public Task<List<Vehicle>> VehiclesAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.Vehicles
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Registration)
            .ToListAsync(ct);
    }

    public Task<List<Driver>> DriversAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.Drivers
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.FullName)
            .ToListAsync(ct);
    }

    public Task<List<TransportOrder>> OrdersAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return db.TransportOrders
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<TransportOrder> CreateOrderAsync(CreateTransportOrderRequest request, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        ValidateCreateOrderRequest(request);

        var orderNumber = request.OrderNumber.Trim();

        var exists = await db.TransportOrders
            .AnyAsync(
                x => x.CompanyId == companyId &&
                     x.OrderNumber == orderNumber,
                ct);

        if (exists)
        {
            throw new InvalidOperationException(
                $"Transportni nalog sa brojem '{orderNumber}' već postoji.");
        }

        var now = DateTimeOffset.UtcNow;

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var order = new TransportOrder
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,

            OrderNumber = orderNumber,
            CustomerName = request.CustomerName.Trim(),

            Status = TransportOrderStatus.Draft,
            Priority = request.Priority,

            CargoDescription = Clean(request.CargoDescription),
            CargoWeightTons = request.CargoWeightTons,
            CargoVolumeM3 = request.CargoVolumeM3,
            Pallets = request.Pallets,
            Hazmat = request.Hazmat,

            CreatedAt = now,
            UpdatedAt = now
        };

        db.TransportOrders.Add(order);

        var stops = request.Stops.Select((requestStop, index) => new TransportStop {
            Id = Guid.NewGuid(),

            CompanyId = companyId,
            TransportOrderId = order.Id,

            Sequence = index + 1,

            Type = requestStop.Type,
            Status = TransportStopStatus.Planned,

            Name = requestStop.Name.Trim(),

            Address = Clean(requestStop.Address),
            City = Clean(requestStop.City),

            CountryCode = NormalizeCountryCode(requestStop.CountryCode),

            Latitude = requestStop.Latitude,
            Longitude = requestStop.Longitude,

            PlannedAt = requestStop.PlannedAt,

            WaitingMinutes = 0,
            ServiceMinutes = requestStop.ServiceMinutes,

            Notes = Clean(requestStop.Notes)
        
        }).ToList();

        db.TransportStops.AddRange(stops);
        audit.Add("Order.Create", nameof(TransportOrder), order.Id, new { order.OrderNumber, order.CustomerName, Stops = stops.Count });

        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return order;
    }

    public async Task<TransportOrder?> OrderAsync(Guid orderId, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return await db.TransportOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.Id == orderId,
                ct);
    }


    private static void ValidateCreateOrderRequest(CreateTransportOrderRequest request)
    {
        if (request is null)
        {
            throw new ArgumentException("Podaci transportnog naloga nisu poslati.");
        }

        if (string.IsNullOrWhiteSpace(request.OrderNumber))
        {
            throw new ArgumentException("Broj naloga je obavezan.");
        }

        if (request.OrderNumber.Trim().Length > 100)
        {
            throw new ArgumentException("Broj naloga može imati najviše 100 karaktera.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            throw new ArgumentException("Kupac je obavezan.");
        }

        if (request.CustomerName.Trim().Length > 200)
        {         
            throw new ArgumentException("Naziv kupca može imati najviše 200 karaktera.");
        }

        if (request.CargoWeightTons is < 0)
        {
            throw new ArgumentException("Težina tereta ne može biti negativna.");
        }

        if (request.CargoVolumeM3 is < 0)
        {
            throw new ArgumentException("Zapremina tereta ne može biti negativna.");
        }

        if (request.Pallets is < 0)
        {
            throw new ArgumentException("Broj paleta ne može biti negativan.");
        }

        if (request.Stops is null || request.Stops.Count < 2)
        {
            throw new ArgumentException("Nalog mora imati najmanje dve transportne tačke.");
        }

        var hasLoading = request.Stops.Any(
            x => x.Type is
                TransportStopType.Loading or
                TransportStopType.LoadingAndUnloading);

        if (!hasLoading)
        {
            throw new ArgumentException("Nalog mora imati najmanje jednu utovarnu tačku.");
        }

        var hasUnloading = request.Stops.Any(
            x => x.Type is
                TransportStopType.Unloading or
                TransportStopType.LoadingAndUnloading);

        if (!hasUnloading)
        {
            throw new ArgumentException("Nalog mora imati najmanje jednu istovarnu tačku.");
        }

        for (var i = 0; i < request.Stops.Count; i++)
        {
            var stop = request.Stops[i];

            var stopNumber = i + 1;

            if (string.IsNullOrWhiteSpace(stop.Name))
            {
                throw new ArgumentException($"Transportna tačka {stopNumber}: naziv je obavezan.");
            }

            if (stop.Name.Trim().Length > 200)
            {
                throw new ArgumentException($"Transportna tačka {stopNumber}: naziv može imati najviše 200 karaktera.");
            }

            if (stop.Latitude is < -90 or > 90)
            {
                throw new ArgumentException($"Transportna tačka {stopNumber}: neispravna geografska širina.");
            }

            if (stop.Longitude is < -180 or > 180)
            {
                throw new ArgumentException($"Transportna tačka {stopNumber}: neispravna geografska dužina.");
            }

            if (stop.ServiceMinutes is < 0 or > 1440)
            {
                throw new ArgumentException($"Transportna tačka {stopNumber}: " + "vreme servisa mora biti između 0 i 1440 minuta.");
            }

            if (!string.IsNullOrWhiteSpace(stop.CountryCode) &&  stop.CountryCode.Trim().Length != 2)
            {
                throw new ArgumentException( $"Transportna tačka {stopNumber}: " + "CountryCode mora imati 2 karaktera.");
            }
        }
    }


    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }


    private static string? NormalizeCountryCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    public async Task<List<Trip>> TripsAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return await db.Trips
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Id)
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<TripDetails?> TripDetailsAsync(Guid tripId, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();
        var trip = await db.Trips.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == tripId, ct);
        if (trip is null)
        {
            return null;
        }

        var order = await db.TransportOrders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == trip.TransportOrderId, ct);
        var stops = await db.TransportStops.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransportOrderId == trip.TransportOrderId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(ct);
        var driver = trip.DriverId is { } driverId
            ? await db.Drivers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == driverId, ct)
            : null;
        var vehicle = trip.VehicleId is { } vehicleId
            ? await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == vehicleId, ct)
            : null;
        var dispatcher = await ResolveDispatchUserAsync(companyId, trip, ct);
        var route = await db.TripRoutes.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TripId == trip.Id)
            .OrderByDescending(x => x.ActivatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var routeId = route?.Id ?? trip.ActiveRouteId;

        var borderCrossings = await db.BorderCrossings.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TripId == trip.Id)
            .OrderBy(x => x.EnteredAt ?? x.ClearedAt)
            .ToListAsync(ct);
        var waitingEvents = await db.WaitingEvents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TripId == trip.Id)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(ct);
        var countrySegments = routeId is Guid segmentRouteId
            ? await db.RouteCountrySegments.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.TripRouteId == segmentRouteId)
                .OrderBy(x => x.Sequence)
                .ToListAsync(ct)
            : [];
        var costs = await db.TransportCosts.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TripId == trip.Id)
            .OrderByDescending(x => x.IncurredAt ?? x.CreatedAt)
            .ToListAsync(ct);
        var historyVehicleId = trip.VehicleId;
        var historyDriverId = trip.DriverId;
        var historySource = await db.Trips.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Id != trip.Id &&
                        ((historyVehicleId != null && x.VehicleId == historyVehicleId) ||
                         (historyDriverId != null && x.DriverId == historyDriverId)))
            .OrderByDescending(x => x.StartedAt ?? x.PlannedStartAt ?? x.CreatedAt)
            .Take(30)
            .ToListAsync(ct);
        var historyOrderIds = historySource.Select(x => x.TransportOrderId).Distinct().ToList();
        var historyOrders = historyOrderIds.Count == 0
            ? new Dictionary<Guid, TransportOrder>()
            : await db.TransportOrders.AsNoTracking()
                .Where(x => x.CompanyId == companyId && historyOrderIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x, ct);
        var historyStops = historyOrderIds.Count == 0
            ? []
            : await db.TransportStops.AsNoTracking()
                .Where(x => x.CompanyId == companyId && historyOrderIds.Contains(x.TransportOrderId))
                .OrderBy(x => x.Sequence)
                .ToListAsync(ct);

        var spent = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment)).Sum(x => x.Amount);
        var allowance = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.Category == TransportCostCategory.Allowance).Sum(x => x.Amount);
        var payments = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.Category == TransportCostCategory.Payment).Sum(x => x.Amount);
        var driverPaid = costs.Where(x => x.Status != TransportCostStatus.Rejected && x.PaymentMethod is "cash" or "driver-card").Sum(x => x.Amount);

        return new TripDetails(
            new TripDetailsSummary(
                trip.Id,
                trip.TripNumber,
                trip.Status.ToString(),
                trip.ExecutionState.ToString(),
                trip.StartedAt,
                trip.CompletedAt,
                trip.PlannedStartAt,
                trip.PlannedArrivalAt,
                trip.PlannedDistanceMeters,
                trip.PlannedDurationSeconds,
                trip.RouteCoveredMeters,
                trip.RouteRemainingMeters,
                trip.RouteProgressPercent,
                trip.DistanceToRouteMeters,
                trip.IsOffRoute,
                trip.CurrentLatitude,
                trip.CurrentLongitude,
                trip.CurrentSpeedKmh,
                trip.LastGpsAt,
                trip.BudgetAmount,
                trip.Currency,
                trip.BudgetAmount is null ? null : trip.BudgetAmount.Value - spent),
            order is null ? null : new TripOrderDetails(
                order.Id,
                order.OrderNumber,
                order.CustomerName,
                order.Status.ToString(),
                order.Priority.ToString(),
                order.CargoDescription,
                order.CargoWeightTons,
                order.CargoVolumeM3,
                order.Pallets,
                order.Hazmat,
                ExtractTripNotes(stops),
                stops.Select(MapStop).ToList()),
            driver is null ? null : new TripDriverDetails(
                driver.Id,
                driver.FullName,
                driver.EmployeeNumber,
                driver.Phone,
                driver.Email,
                driver.Status.ToString(),
                driver.LicenseNumber,
                driver.LicenseCategories,
                driver.CurrentVehicleId,
                driver.LastGpsAt,
                driver.Notes,
                historySource.Where(x => x.DriverId == driver.Id).Select(x => MapHistoryTrip(x, historyOrders, historyStops)).ToList()),
            vehicle is null ? null : new TripVehicleDetails(
                vehicle.Id,
                vehicle.Registration,
                vehicle.Name,
                vehicle.Make,
                vehicle.Model,
                vehicle.Type.ToString(),
                vehicle.Status.ToString(),
                vehicle.FleetNumber,
                vehicle.FuelCard,
                vehicle.TrailerId,
                vehicle.OdometerKm,
                vehicle.GrossWeightTons,
                vehicle.AxleLoadTons,
                vehicle.LengthMeters,
                vehicle.WidthMeters,
                vehicle.HeightMeters,
                vehicle.Axles,
                vehicle.MaxSpeedKmh,
                vehicle.Hazmat,
                vehicle.Notes,
                historySource.Where(x => x.VehicleId == vehicle.Id).Select(x => MapHistoryTrip(x, historyOrders, historyStops)).ToList()),
            dispatcher,
            new TripFinancialDetails(
                trip.Currency,
                trip.BudgetAmount,
                spent,
                allowance,
                payments,
                driverPaid,
                costs.Where(x => x.Status == TransportCostStatus.Pending).Sum(x => x.Amount),
                costs.Select(MapCost).ToList()),
            new TripComplianceDetails(
                countrySegments.Select(x => new TripCountrySegmentDetails(x.Sequence, x.CountryCode, x.DistanceMeters, x.DurationSeconds, x.EstimatedEntry, x.EstimatedExit)).ToList(),
                borderCrossings.Select(x => new TripBorderCrossingDetails(x.Id, x.CountryFrom, x.CountryTo, x.BorderPoint, x.Status.ToString(), x.EnteredAt, x.ClearedAt, x.WaitingMinutes, x.Inspection, x.CostAmount, x.Currency, x.Notes)).ToList(),
                waitingEvents.Select(x => new TripWaitingEventDetails(x.Id, x.Reason.ToString(), x.StartedAt, x.EndedAt, x.DurationMinutes, x.CostAmount, x.Currency, x.Notes)).ToList()),
            route is null ? null : new TripRouteInfo(route.Id, route.Status.ToString(), route.Engine, route.UsedFallback, route.DistanceMeters, route.DurationSeconds, route.EstimatedArrival, route.ActivatedAt, route.CreatedAt));
    }

    public async Task<TripRoute?> ActiveRouteAsync(Guid tripId, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        return await db.TripRoutes
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.TripId == tripId &&
                x.Status == TripRouteStatus.Active)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RouteDispatch?> DispatchAsync(Guid tripId, Guid routeId, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        var trip = await db.Trips.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == tripId, ct);


        if (trip is null)
        {
            throw new InvalidOperationException("Tura nije pronađena.");
        }

        var route = await db.TripRoutes
            .SingleOrDefaultAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.Id == routeId &&
                    x.TripId == tripId,
                ct);

        if (route is null)
        {
            throw new InvalidOperationException("Ruta nije pronađena za izabranu turu.");
        }

        if (trip.DriverId is null)
        {
            throw new InvalidOperationException("Tura nema dodeljenog vozača.");
        }

        if (trip.VehicleId is null)
        {
            throw new InvalidOperationException("Tura nema dodeljeno vozilo.");
        }

        var dispatch = new RouteDispatch
        {
            Id = Guid.NewGuid(),

            CompanyId = companyId,

            TripId = trip.Id,
            TripRouteId = route.Id,

            DriverId = trip.DriverId.Value,
            VehicleId = trip.VehicleId.Value,

            Status = RouteDispatchStatus.Pending,

            CreatedAt = DateTimeOffset.UtcNow
        };

        db.RouteDispatches.Add(dispatch);

        await db.SaveChangesAsync(ct);

        return dispatch;
    }

    // ---- Orders: list with stops (/orders) and edit ("Izmeni") ----

    public async Task<List<OrderOverview>> OrdersOverviewAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        var orders = await db.TransportOrders.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
        var ids = orders.Select(x => x.Id).ToList();
        var stops = await db.TransportStops.AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.TransportOrderId))
            .OrderBy(x => x.Sequence)
            .ToListAsync(ct);
        var drivers = await db.Drivers.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == companyId).ToDictionaryAsync(x => x.Id, x => x.Registration, ct);
        var trips = await db.Trips.AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.TransportOrderId))
            .ToListAsync(ct);
        var costs = await db.TransportCosts.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransportOrderId != null && ids.Contains(x.TransportOrderId.Value) && x.Status != TransportCostStatus.Rejected)
            .GroupBy(x => x.TransportOrderId!.Value)
            .Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Amount, ct);

        return orders.Select(order =>
        {
            var trip = trips.Where(t => t.TransportOrderId == order.Id).OrderByDescending(t => t.CreatedAt).FirstOrDefault();
            return new OrderOverview(
                order,
                stops.Where(x => x.TransportOrderId == order.Id).ToList(),
                order.DriverId is { } d && drivers.TryGetValue(d, out var driverName) ? driverName : null,
                order.VehicleId is { } v && vehicles.TryGetValue(v, out var registration) ? registration : null,
                trip?.Id,
                trip?.TripNumber,
                trip?.RouteProgressPercent,
                costs.TryGetValue(order.Id, out var amount) ? amount : 0);
        }).ToList();
    }

    public async Task<OrderDetails?> OrderDetailsAsync(Guid orderId, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();
        var order = await db.TransportOrders.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == orderId, ct);
        if (order is null)
        {
            return null;
        }

        var stops = await db.TransportStops.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransportOrderId == orderId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(ct);
        return new OrderDetails(order, stops);
    }

    private async Task<TripDispatcherDetails?> ResolveDispatchUserAsync(Guid companyId, Trip trip, CancellationToken ct)
    {
        var dispatchUserId = await db.RouteDispatches.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TripId == trip.Id && x.SentByUserId != null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.SentByUserId)
            .FirstOrDefaultAsync(ct);

        Guid? userId = dispatchUserId;
        if (userId is null && trip.TransportOrderId != Guid.Empty)
        {
            userId = await db.AuditLogs.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.EntityType == nameof(TransportOrder) && x.EntityId == trip.TransportOrderId.ToString() && x.UserId != null)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.UserId)
                .FirstOrDefaultAsync(ct);
        }

        if (userId is not Guid dispatcherId)
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == dispatcherId, ct);
        return user is null ? null : new TripDispatcherDetails(user.Id, user.DisplayName, user.Email, user.PhoneNumber);
    }

    private static string? ExtractTripNotes(IEnumerable<TransportStop> stops)
    {
        var notes = stops
            .Where(x => !string.IsNullOrWhiteSpace(x.Notes))
            .Select(x => $"{x.Sequence}. {x.Name}: {x.Notes}")
            .ToList();
        return notes.Count == 0 ? null : string.Join(Environment.NewLine, notes);
    }

    private static TripStopDetails MapStop(TransportStop stop)
        => new(stop.Id, stop.Sequence, stop.Type.ToString(), stop.Status.ToString(), stop.Name, stop.Address, stop.City, stop.CountryCode, stop.Latitude, stop.Longitude, stop.PlannedAt, stop.ArrivedAt, stop.CompletedAt, stop.WaitingMinutes, stop.ServiceMinutes, stop.Notes);

    private static TripHistoryItem MapHistoryTrip(Trip trip, IReadOnlyDictionary<Guid, TransportOrder> orders, IReadOnlyList<TransportStop> stops)
    {
        orders.TryGetValue(trip.TransportOrderId, out var order);
        var route = string.Join(" → ", stops.Where(x => x.TransportOrderId == trip.TransportOrderId).Select(x => x.City ?? x.Name));
        return new TripHistoryItem(trip.Id, trip.TripNumber, trip.Status.ToString(), trip.ExecutionState.ToString(), order?.OrderNumber, route, trip.StartedAt ?? trip.PlannedStartAt, trip.CompletedAt ?? trip.PlannedArrivalAt, trip.RouteProgressPercent);
    }

    private static TripCostDetails MapCost(TransportCost cost)
        => new(cost.Id, cost.Category.ToString(), cost.Status.ToString(), cost.Source.ToString(), cost.Amount, cost.Currency, cost.Quantity, cost.Unit, cost.UnitPrice, cost.IncurredAt ?? cost.CreatedAt, cost.Location, cost.CountryCode, cost.PaymentMethod, cost.CardLabel, cost.OdometerKm, cost.Notes, cost.ReceiptFileId, cost.CreatedByUserId);

    public async Task<TransportOrder> UpdateOrderAsync(Guid orderId, CreateTransportOrderRequest request, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();
        ValidateCreateOrderRequest(request);

        var order = await db.TransportOrders.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == orderId, ct)
                    ?? throw new KeyNotFoundException("Transportni nalog nije pronađen.");

        var orderNumber = request.OrderNumber.Trim();
        if (await db.TransportOrders.AnyAsync(x => x.CompanyId == companyId && x.Id != orderId && x.OrderNumber == orderNumber, ct))
        {
            throw new InvalidOperationException($"Transportni nalog sa brojem '{orderNumber}' već postoji.");
        }

        var old = new { order.OrderNumber, order.CustomerName, order.Priority, order.CargoWeightTons };
        order.OrderNumber = orderNumber;
        order.CustomerName = request.CustomerName.Trim();
        order.Priority = request.Priority;
        order.CargoDescription = Clean(request.CargoDescription);
        order.CargoWeightTons = request.CargoWeightTons;
        order.CargoVolumeM3 = request.CargoVolumeM3;
        order.Pallets = request.Pallets;
        order.Hazmat = request.Hazmat;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        // Stops are replaced by the edited list; progress already recorded on a stop that
        // keeps its place in the sequence is kept.
        var existing = await db.TransportStops.Where(x => x.CompanyId == companyId && x.TransportOrderId == orderId).ToListAsync(ct);
        db.TransportStops.RemoveRange(existing);
        db.TransportStops.AddRange(request.Stops.Select((requestStop, index) =>
        {
            var previous = existing.FirstOrDefault(x => x.Sequence == index + 1);
            return new TransportStop
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                TransportOrderId = order.Id,
                Sequence = index + 1,
                Type = requestStop.Type,
                Status = previous?.Status ?? TransportStopStatus.Planned,
                Name = requestStop.Name.Trim(),
                Address = Clean(requestStop.Address),
                City = Clean(requestStop.City),
                CountryCode = NormalizeCountryCode(requestStop.CountryCode),
                Latitude = requestStop.Latitude,
                Longitude = requestStop.Longitude,
                PlannedAt = requestStop.PlannedAt,
                ArrivedAt = previous?.ArrivedAt,
                CompletedAt = previous?.CompletedAt,
                WaitingMinutes = previous?.WaitingMinutes ?? 0,
                ServiceMinutes = requestStop.ServiceMinutes,
                Notes = Clean(requestStop.Notes),
            };
        }));

        audit.Add("Order.Update", nameof(TransportOrder), order.Id, new { order.OrderNumber, order.CustomerName, order.Priority, order.CargoWeightTons }, old);
        await db.SaveChangesAsync(ct);
        return order;
    }

    // ---- Vehicles ("Novo vozilo") ----

    public async Task<Vehicle> CreateVehicleAsync(VehicleInput input, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Naziv vozila je obavezan.");
        if (string.IsNullOrWhiteSpace(input.Registration)) throw new ArgumentException("Registarski broj je obavezan.");
        var vin = (input.Vin ?? "").Trim().ToUpperInvariant();
        if (vin.Length != 17) throw new ArgumentException("VIN mora sadržati tačno 17 karaktera.");
        if (input.Type is null) throw new ArgumentException("Tip vozila je obavezan.");
        if (input.RegistrationExpiry is null) throw new ArgumentException("Datum isteka registracije je obavezan.");
        if (input.Year is < 1950 or > 2100) throw new ArgumentException("Godina proizvodnje nije ispravna.");

        var registration = input.Registration.Trim().ToUpperInvariant();
        if (await db.Vehicles.AnyAsync(x => x.CompanyId == companyId && x.Registration == registration, ct))
        {
            throw new InvalidOperationException($"Vozilo sa registracijom '{registration}' već postoji.");
        }

        var driver = input.DriverId is { } driverId
            ? await db.Drivers.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == driverId, ct) ?? throw new ArgumentException("Izabrani vozač ne postoji.")
            : null;
        if (input.TrailerId is { } trailerId && !await db.Vehicles.AnyAsync(x => x.CompanyId == companyId && x.Id == trailerId && x.Type == VehicleType.Trailer, ct))
        {
            throw new ArgumentException("Izabrana prikolica ne postoji.");
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Name = input.Name.Trim(),
            Registration = registration,
            FleetNumber = Clean(input.FleetNumber),
            VIN = vin,
            Type = input.Type.Value,
            Make = Clean(input.Make),
            Model = Clean(input.Model),
            Year = input.Year,
            RegistrationDate = input.RegistrationDate,
            RegistrationExpiry = input.RegistrationExpiry,
            RegistrationOffice = Clean(input.RegistrationOffice),
            RegistrationDocument = Clean(input.RegistrationDocument),
            Engine = Clean(input.Engine),
            EnginePowerKw = input.EnginePowerKw,
            EmissionClass = Clean(input.EmissionClass),
            Fuel = input.Fuel ?? FuelType.Diesel,
            GrossWeightTons = input.GrossWeightTons ?? 40,
            EmptyWeightTons = input.EmptyWeightTons,
            HeightMeters = input.HeightMeters ?? 4m,
            WidthMeters = input.WidthMeters ?? 2.55m,
            LengthMeters = input.LengthMeters ?? 16.5m,
            AxleLoadTons = input.AxleLoadTons ?? 10,
            Axles = input.Axles ?? 5,
            MaxSpeedKmh = input.MaxSpeedKmh ?? 90,
            Status = input.Status ?? VehicleStatus.Active,
            Department = Clean(input.Department),
            CurrentDriverId = driver?.Id,
            FuelCard = Clean(input.FuelCard),
            Notes = Clean(input.Notes),
            TrailerId = input.TrailerId,
            TechnicalInspectionExpiry = input.TechnicalInspectionExpiry,
            TirExpiry = input.TirExpiry,
            EuroConfirmationExpiry = input.EuroConfirmationExpiry,
            TachographCalibrationExpiry = input.TachographCalibrationExpiry,
            OdometerKm = input.OdometerKm,
            Commercial = true,
            Hazmat = input.Hazmat,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Vehicles.Add(vehicle);

        if (driver is not null)
        {
            driver.CurrentVehicleId = vehicle.Id;
        }

        if (vehicle.TrailerId is not null)
        {
            db.TrailerAssignments.Add(new TrailerAssignment
            {
                Id = Guid.NewGuid(), CompanyId = companyId, VehicleId = vehicle.Id, TrailerId = vehicle.TrailerId,
                AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = current.UserId,
            });
        }

        audit.Add("Vehicle.Create", nameof(Vehicle), vehicle.Id, new { vehicle.Registration, vehicle.Name, vehicle.Type });
        await db.SaveChangesAsync(ct);
        return vehicle;
    }

    // ---- Drivers ("Novi vozač") ----

    public async Task<Driver> CreateDriverAsync(DriverInput input, CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        if (string.IsNullOrWhiteSpace(input.FullName)) throw new ArgumentException("Ime i prezime vozača je obavezno.");
        if (string.IsNullOrWhiteSpace(input.Phone)) throw new ArgumentException("Telefon vozača je obavezan.");
        if (string.IsNullOrWhiteSpace(input.LicenseNumber)) throw new ArgumentException("Broj vozačke dozvole je obavezan.");
        if (input.LicenseExpiry is null) throw new ArgumentException("Datum isteka vozačke dozvole je obavezan.");

        var employeeNumber = Clean(input.EmployeeNumber);
        if (employeeNumber is not null && await db.Drivers.AnyAsync(x => x.CompanyId == companyId && x.EmployeeNumber == employeeNumber, ct))
        {
            throw new InvalidOperationException($"Vozač sa brojem '{employeeNumber}' već postoji.");
        }

        var vehicle = input.VehicleId is { } vehicleId
            ? await db.Vehicles.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == vehicleId, ct) ?? throw new ArgumentException("Izabrano vozilo ne postoji.")
            : null;

        // The driver portal and the phone find the driver by the account with the same e-mail.
        var email = Clean(input.Email)?.ToLowerInvariant();
        Guid? userId = null;
        if (email is not null)
        {
            var normalized = email.ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized && (x.CompanyId == companyId || x.CompanyId == null), ct);
            if (user is not null && !await db.Drivers.AnyAsync(x => x.UserId == user.Id, ct))
            {
                userId = user.Id;
                user.CompanyId ??= companyId;
            }
        }

        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = userId,
            FullName = input.FullName.Trim(),
            EmployeeNumber = employeeNumber,
            Phone = input.Phone.Trim(),
            Email = email,
            Address = Clean(input.Address),
            LicenseNumber = input.LicenseNumber.Trim().ToUpperInvariant(),
            LicenseCategories = Clean(input.LicenseCategories)?.ToUpperInvariant(),
            LicenseExpiry = input.LicenseExpiry,
            Code95Expiry = input.Code95Expiry,
            AdrExpiry = input.AdrExpiry,
            TachographCardNumber = Clean(input.TachographCardNumber)?.ToUpperInvariant(),
            TachographCardExpiry = input.TachographCardExpiry,
            MedicalExpiry = input.MedicalExpiry,
            HireDate = input.HireDate,
            Status = input.Status ?? DriverStatus.Active,
            CurrentVehicleId = vehicle?.Id,
            Notes = Clean(input.Notes),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Drivers.Add(driver);

        if (vehicle is not null)
        {
            vehicle.CurrentDriverId = driver.Id;
        }

        audit.Add("Driver.Create", nameof(Driver), driver.Id, new { driver.FullName, driver.EmployeeNumber, LinkedAccount = userId is not null });
        await db.SaveChangesAsync(ct);
        return driver;
    }

    public async Task<object> DashboardAsync(CancellationToken ct)
    {
        var companyId = GetRequiredCompanyId();

        var vehicles = await db.Vehicles.CountAsync(x => x.CompanyId == companyId, ct);

        var drivers = await db.Drivers.CountAsync(x => x.CompanyId == companyId, ct);           

        var orders = await db.TransportOrders.CountAsync(x => x.CompanyId == companyId, ct);

        var activeTrips = await db.Trips.CountAsync(x => x.CompanyId == companyId && x.Status == TripStatus.Active, ct);    


        return new
        {
            Vehicles = vehicles,
            Drivers = drivers,
            Orders = orders,
            ActiveTrips = activeTrips
        };
    }
}

public sealed record CreateTransportOrderRequest
{
    public string OrderNumber { get; init; } = "";
    public string CustomerName { get; init; } = "";

    // The order form sends enum names ("Normal", "Loading"); numbers are accepted too.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransportOrderPriority Priority { get; init; } = TransportOrderPriority.Normal;
    public string? CargoDescription { get; init; }
    public decimal? CargoWeightTons { get; init; }
    public decimal? CargoVolumeM3 { get; init; }
    public int? Pallets { get; init; }
    public bool Hazmat { get; init; }
    public List<CreateTransportStopRequest> Stops { get; init; } = [];
}

public sealed record CreateTransportStopRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransportStopType Type { get; init; }
    public string Name { get; init; } = "";
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? CountryCode { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public DateTimeOffset? PlannedAt { get; init; }
    public int ServiceMinutes { get; init; }
    public string? Notes { get; init; }
}
public sealed record OrderOverview(
    TransportOrder Order,
    List<TransportStop> Stops,
    string? DriverName,
    string? VehicleRegistration,
    Guid? TripId,
    string? TripNumber,
    double? TripProgressPercent,
    decimal CostsAmount);

public sealed record OrderDetails(TransportOrder Order, List<TransportStop> Stops);

public sealed record VehicleInput
{
    public string? Name { get; init; }
    public string? Registration { get; init; }
    public string? FleetNumber { get; init; }
    public string? Vin { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VehicleType? Type { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? Year { get; init; }
    public DateOnly? RegistrationDate { get; init; }
    public DateOnly? RegistrationExpiry { get; init; }
    public string? RegistrationOffice { get; init; }
    public string? RegistrationDocument { get; init; }
    public string? Engine { get; init; }
    public int? EnginePowerKw { get; init; }
    public string? EmissionClass { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FuelType? Fuel { get; init; }
    public decimal? GrossWeightTons { get; init; }
    public decimal? EmptyWeightTons { get; init; }
    public decimal? HeightMeters { get; init; }
    public decimal? WidthMeters { get; init; }
    public decimal? LengthMeters { get; init; }
    public decimal? AxleLoadTons { get; init; }
    public int? Axles { get; init; }
    public decimal? MaxSpeedKmh { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VehicleStatus? Status { get; init; }
    public string? Department { get; init; }
    public Guid? DriverId { get; init; }
    public string? FuelCard { get; init; }
    public string? Notes { get; init; }
    public Guid? TrailerId { get; init; }
    public DateOnly? TechnicalInspectionExpiry { get; init; }
    public DateOnly? TirExpiry { get; init; }
    public DateOnly? EuroConfirmationExpiry { get; init; }
    public DateOnly? TachographCalibrationExpiry { get; init; }
    public int? OdometerKm { get; init; }
    public bool Hazmat { get; init; }
}

public sealed record DriverInput
{
    public string? FullName { get; init; }
    public string? EmployeeNumber { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? LicenseNumber { get; init; }
    public string? LicenseCategories { get; init; }
    public DateOnly? LicenseExpiry { get; init; }
    public DateOnly? Code95Expiry { get; init; }
    public DateOnly? AdrExpiry { get; init; }
    public string? TachographCardNumber { get; init; }
    public DateOnly? TachographCardExpiry { get; init; }
    public DateOnly? MedicalExpiry { get; init; }
    public DateOnly? HireDate { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DriverStatus? Status { get; init; }
    public Guid? VehicleId { get; init; }
    public string? Notes { get; init; }
}

public sealed record TripDetails(
    TripDetailsSummary Trip,
    TripOrderDetails? Order,
    TripDriverDetails? Driver,
    TripVehicleDetails? Vehicle,
    TripDispatcherDetails? Dispatcher,
    TripFinancialDetails Financial,
    TripComplianceDetails Compliance,
    TripRouteInfo? Route);

public sealed record TripDetailsSummary(
    Guid Id,
    string? TripNumber,
    string Status,
    string ExecutionState,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedArrivalAt,
    double PlannedDistanceMeters,
    double PlannedDurationSeconds,
    double RouteCoveredMeters,
    double RouteRemainingMeters,
    double RouteProgressPercent,
    double DistanceToRouteMeters,
    bool IsOffRoute,
    double? CurrentLatitude,
    double? CurrentLongitude,
    double? CurrentSpeedKmh,
    DateTimeOffset? LastGpsAt,
    decimal? BudgetAmount,
    string Currency,
    decimal? RemainingBudget);

public sealed record TripOrderDetails(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    string Status,
    string Priority,
    string? CargoDescription,
    decimal? CargoWeightTons,
    decimal? CargoVolumeM3,
    int? Pallets,
    bool Hazmat,
    string? Notes,
    List<TripStopDetails> Stops);

public sealed record TripStopDetails(
    Guid Id,
    int Sequence,
    string Type,
    string Status,
    string Name,
    string? Address,
    string? City,
    string? CountryCode,
    double Latitude,
    double Longitude,
    DateTimeOffset? PlannedAt,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? CompletedAt,
    int WaitingMinutes,
    int ServiceMinutes,
    string? Notes);

public sealed record TripDriverDetails(
    Guid Id,
    string FullName,
    string? EmployeeNumber,
    string? Phone,
    string? Email,
    string Status,
    string? LicenseNumber,
    string? LicenseCategories,
    Guid? CurrentVehicleId,
    DateTimeOffset? LastGpsAt,
    string? Notes,
    List<TripHistoryItem> TripHistory);

public sealed record TripVehicleDetails(
    Guid Id,
    string Registration,
    string? Name,
    string? Make,
    string? Model,
    string Type,
    string Status,
    string? FleetNumber,
    string? FuelCard,
    Guid? TrailerId,
    int? OdometerKm,
    decimal GrossWeightTons,
    decimal AxleLoadTons,
    decimal LengthMeters,
    decimal WidthMeters,
    decimal HeightMeters,
    int Axles,
    decimal MaxSpeedKmh,
    bool Hazmat,
    string? Notes,
    List<TripHistoryItem> TripHistory);

public sealed record TripHistoryItem(
    Guid Id,
    string? TripNumber,
    string Status,
    string ExecutionState,
    string? OrderNumber,
    string Route,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    double RouteProgressPercent);

public sealed record TripDispatcherDetails(Guid Id, string DisplayName, string? Email, string? PhoneNumber);

public sealed record TripFinancialDetails(
    string Currency,
    decimal? BudgetAmount,
    decimal SpentAmount,
    decimal AllowanceAmount,
    decimal PaidOutAmount,
    decimal DriverPaidAmount,
    decimal PendingAmount,
    List<TripCostDetails> Costs);

public sealed record TripCostDetails(
    Guid Id,
    string Category,
    string Status,
    string Source,
    decimal Amount,
    string Currency,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    DateTimeOffset At,
    string? Location,
    string? CountryCode,
    string? PaymentMethod,
    string? CardLabel,
    int? OdometerKm,
    string? Notes,
    Guid? ReceiptFileId,
    Guid? CreatedByUserId);

public sealed record TripComplianceDetails(
    List<TripCountrySegmentDetails> CountrySegments,
    List<TripBorderCrossingDetails> BorderCrossings,
    List<TripWaitingEventDetails> WaitingEvents);

public sealed record TripCountrySegmentDetails(int Sequence, string CountryCode, double DistanceMeters, double DurationSeconds, DateTimeOffset? EstimatedEntry, DateTimeOffset? EstimatedExit);
public sealed record TripBorderCrossingDetails(Guid Id, string CountryFrom, string CountryTo, string BorderPoint, string Status, DateTimeOffset? EnteredAt, DateTimeOffset? ClearedAt, int WaitingMinutes, bool Inspection, decimal CostAmount, string Currency, string? Notes);
public sealed record TripWaitingEventDetails(Guid Id, string Reason, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int DurationMinutes, decimal CostAmount, string Currency, string? Notes);
public sealed record TripRouteInfo(Guid Id, string Status, string Engine, bool UsedFallback, double DistanceMeters, double DurationSeconds, DateTimeOffset? EstimatedArrival, DateTimeOffset? ActivatedAt, DateTimeOffset CreatedAt);
