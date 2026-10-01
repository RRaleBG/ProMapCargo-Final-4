using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

public sealed class BusinessService(ProMapCargoDbContext db, ICurrentUserContext current, IHubContext<NavigationHub> hub)
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