using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>
/// Data of /finance: a driver's balance, the budget of the active trip, fuel card and the
/// cost ledger, or all drivers' costs in dispatch mode.
/// </summary>
public sealed class FinanceQueries(ProMapCargoDbContext db, ICurrentUserContext current)
{
    // Paid by the driver: the company owes it back (with allowances, minus payouts).
    private static readonly string[] DriverPaid = ["cash", "driver-card"];

    /// <summary>Null when the request has no company context.</summary>
    public async Task<object?> OverviewAsync(Guid? driverId, string mode, CancellationToken ct)
    {
        if (current.CompanyId is not Guid c) return null;

        var dispatcherDriverIds = current.IsAdministrator ? null : await ResolveDispatcherDriverIdsAsync(c, ct).ConfigureAwait(false);
        var driversQuery = db.Drivers.AsNoTracking().Where(x => x.CompanyId == c);
        if (dispatcherDriverIds is { Count: > 0 })
        {
            driversQuery = driversQuery.Where(x => dispatcherDriverIds.Contains(x.Id));
        }

        var drivers = await driversQuery.OrderBy(x => x.FullName).ToListAsync(ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.CompanyId == c).ToListAsync(ct);
        var tripsQuery = db.Trips.AsNoTracking().Where(x => x.CompanyId == c);
        if (dispatcherDriverIds is { Count: > 0 })
        {
            tripsQuery = tripsQuery.Where(x => x.DriverId != null && dispatcherDriverIds.Contains(x.DriverId.Value));
        }

        var trips = await tripsQuery.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var costsQuery = db.TransportCosts.AsNoTracking().Where(x => x.CompanyId == c);
        if (dispatcherDriverIds is { Count: > 0 })
        {
            costsQuery = costsQuery.Where(x => x.DriverId != null && dispatcherDriverIds.Contains(x.DriverId.Value));
        }

        var costs = await costsQuery.OrderByDescending(x => x.IncurredAt ?? x.CreatedAt).Take(1000).ToListAsync(ct);
        var orderIds = trips.Select(x => x.TransportOrderId).Distinct().ToList();
        var stops = await db.TransportStops.AsNoTracking().Where(x => x.CompanyId == c && orderIds.Contains(x.TransportOrderId)).OrderBy(x => x.Sequence).ToListAsync(ct);

        // Own view of a driver account; otherwise the chosen driver or the one on an active trip.
        var me = drivers.FirstOrDefault(x => x.UserId == current.UserId);
        var driver = me
                     ?? drivers.FirstOrDefault(x => x.Id == driverId)
                     ?? drivers.FirstOrDefault(x => trips.Any(t => t.DriverId == x.Id && t.Status == TripStatus.Active))
                     ?? drivers.FirstOrDefault();
        var dispatchMode = me is null && string.Equals(mode, "dispatch", StringComparison.OrdinalIgnoreCase);

        string Route(Trip trip) => string.Join(" → ", stops.Where(s => s.TransportOrderId == trip.TransportOrderId).Select(s => s.City ?? s.Name));
        string? Registration(Guid? id) => vehicles.FirstOrDefault(v => v.Id == id)?.Registration;
        string? TripNumber(Guid? id) => trips.FirstOrDefault(t => t.Id == id)?.TripNumber;

        var driverTrips = trips.Where(t => driver is not null && t.DriverId == driver.Id).ToList();
        var activeTrip = driverTrips.FirstOrDefault(t => t.Status is TripStatus.Active or TripStatus.Paused)
                         ?? driverTrips.FirstOrDefault(t => t.Status == TripStatus.Planned);
        var previousTrip = driverTrips.Where(t => t.Status == TripStatus.Completed).OrderByDescending(t => t.CompletedAt).FirstOrDefault();

        var scoped = dispatchMode ? costs : costs.Where(x => driver is not null && x.DriverId == driver.Id).ToList();
        var counted = scoped.Where(x => x.Status != TransportCostStatus.Rejected).ToList();

        decimal Spent(Trip? trip) => trip is null ? 0 : costs
            .Where(x => x.TripId == trip.Id && x.Status != TransportCostStatus.Rejected && x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment))
            .Sum(x => x.Amount);

        decimal Balance(IEnumerable<TransportCost> items) => items.Sum(x =>
            x.Category == TransportCostCategory.Payment ? -x.Amount
            : x.Category == TransportCostCategory.Allowance || DriverPaid.Contains(x.PaymentMethod) ? x.Amount
            : 0);

        var settled = counted.Where(x => x.Status is TransportCostStatus.Approved or TransportCostStatus.Paid).ToList();
        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var activeCosts = activeTrip is null ? [] : counted.Where(x => x.TripId == activeTrip.Id).ToList();

        object? TripCard(Trip? trip) => trip is null ? null : new
        {
            trip.Id,
            Number = trip.TripNumber,
            Route = Route(trip),
            Status = trip.Status.ToString(),
            Vehicle = Registration(trip.VehicleId),
            Driver = drivers.FirstOrDefault(d => d.Id == trip.DriverId)?.FullName,
            StartedAt = trip.StartedAt ?? trip.PlannedStartAt,
            trip.PlannedArrivalAt,
            trip.CompletedAt,
            Budget = trip.BudgetAmount,
            trip.Currency,
            Spent = Spent(trip),
            Remaining = (trip.BudgetAmount ?? 0) - Spent(trip),
            ByCategory = costs
                .Where(x => x.TripId == trip.Id && x.Status != TransportCostStatus.Rejected && x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment))
                .GroupBy(x => x.Category switch
                {
                    TransportCostCategory.Fuel or TransportCostCategory.AdBlue => "fuel",
                    TransportCostCategory.Toll => "toll",
                    TransportCostCategory.Parking => "parking",
                    _ => "other",
                })
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)),
        };

        var vehicle = vehicles.FirstOrDefault(v => v.Id == (activeTrip?.VehicleId ?? driver?.CurrentVehicleId));
        var fuel = activeCosts.Where(x => x.Category == TransportCostCategory.Fuel).ToList();

        return (new
        {
            Mode = dispatchMode ? "dispatch" : "driver",
            IsDriverAccount = me is not null,
            CanReview = current.IsInRole("Administrator") || current.IsInRole("Dispatcher"),
            Driver = driver is null ? null : new { driver.Id, driver.FullName, driver.EmployeeNumber },
            Drivers = drivers.Select(d => new { d.Id, d.FullName }),
            Kpi = new
            {
                Balance = Balance(counted.Where(x => x.Status is TransportCostStatus.Approved or TransportCostStatus.Paid)),
                MonthDelta = Balance(counted.Where(x => x.Status is TransportCostStatus.Approved or TransportCostStatus.Paid && (x.IncurredAt ?? x.CreatedAt) >= monthStart)),
                ActiveRemaining = activeTrip is null ? (decimal?)null : (activeTrip.BudgetAmount ?? 0) - Spent(activeTrip),
                ActiveTripNumber = activeTrip?.TripNumber,
                PreviousNet = previousTrip is null ? (decimal?)null : (previousTrip.BudgetAmount ?? 0) - Spent(previousTrip),
                PreviousTripNumber = previousTrip?.TripNumber,
                TripCosts = Spent(activeTrip),
                TripCostCount = activeCosts.Count(x => x.Category is not (TransportCostCategory.Allowance or TransportCostCategory.Payment)),
                PendingAmount = scoped.Where(x => x.Status == TransportCostStatus.Pending).Sum(x => x.Amount),
                PendingCount = scoped.Count(x => x.Status == TransportCostStatus.Pending),
            },
            // Receivables: costs the driver paid and allowances; payouts reduce the balance.
            Balance = new
            {
                Receivables = settled.Where(x => x.Category == TransportCostCategory.Allowance || DriverPaid.Contains(x.PaymentMethod)).Sum(x => x.Amount),
                CashAdvances = 0m,
                Payments = settled.Where(x => x.Category == TransportCostCategory.Payment).Sum(x => x.Amount),
            },
            ActiveTrip = TripCard(activeTrip),
            PreviousTrip = TripCard(previousTrip),
            FuelCard = vehicle is null ? null : new
            {
                Label = vehicle.FuelCard,
                Vehicle = vehicle.Registration,
                Driver = driver?.FullName,
                Active = vehicle.Status != VehicleStatus.OutOfService,
                Liters = fuel.Sum(x => x.Quantity),
                Amount = fuel.Sum(x => x.Amount),
                AveragePrice = fuel.Sum(x => x.Quantity) > 0 ? fuel.Sum(x => x.Amount) / fuel.Sum(x => x.Quantity) : 0,
                Transactions = fuel.Select(x => new { x.Id, x.Location, x.CountryCode, At = x.IncurredAt ?? x.CreatedAt, x.CardLabel, x.Quantity, x.UnitPrice, x.Amount, Status = x.Status.ToString() }),
            },
            Transactions = scoped.Select(x => new
            {
                x.Id,
                Category = x.Category.ToString(),
                Status = x.Status.ToString(),
                x.Amount,
                x.Currency,
                At = x.IncurredAt ?? x.CreatedAt,
                x.Location,
                x.CountryCode,
                x.PaymentMethod,
                x.CardLabel,
                x.Quantity,
                x.Unit,
                x.UnitPrice,
                x.OdometerKm,
                x.Notes,
                x.TripId,
                TripNumber = TripNumber(x.TripId),
                Vehicle = Registration(x.VehicleId),
                Driver = drivers.FirstOrDefault(d => d.Id == x.DriverId)?.FullName,
                x.ReceiptFileId,
            }),
            Options = new
            {
                Trips = trips.Where(t => dispatchMode || driver is null || t.DriverId == driver.Id)
                    .Select(t => new { t.Id, t.TripNumber, t.VehicleId, Status = t.Status.ToString() }),
                Vehicles = vehicles.Where(v => v.Type != VehicleType.Trailer).OrderBy(v => v.Registration).Select(v => new { v.Id, v.Registration, v.OdometerKm }),
                Cards = vehicles.Where(v => !string.IsNullOrWhiteSpace(v.FuelCard)).OrderBy(v => v.FuelCard)
                    .Select(v => new { Label = v.FuelCard, Vehicle = v.Registration, Active = v.Status != VehicleStatus.OutOfService }),
            },
        });
    }
    private async Task<HashSet<Guid>?> ResolveDispatcherDriverIdsAsync(Guid companyId, CancellationToken ct)
    {
        if (!current.IsInRole("Dispatcher") || current.UserId is not Guid userId)
        {
            return null;
        }

        var tripIds = await db.RouteDispatches.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SentByUserId == userId)
            .Select(x => x.TripId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (tripIds.Count == 0)
        {
            return [];
        }

        return await db.Trips.AsNoTracking()
            .Where(x => x.CompanyId == companyId && tripIds.Contains(x.Id) && x.DriverId != null)
            .Select(x => x.DriverId!.Value)
            .Distinct()
            .ToHashSetAsync(ct)
            .ConfigureAwait(false);
    }
}
