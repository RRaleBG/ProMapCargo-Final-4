using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProMapCargo.Api.Data;

namespace ProMapCargo.Api.Services;

[Authorize]
public sealed class NavigationHub(
    ICurrentUserContext current,
    ProMapCargoDbContext db) : Hub
{
    public Task JoinCompany()
    {
        if (!current.CompanyId.HasValue)
        {
            throw new HubException("Company context missing.");
        }

        return Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"company:{current.CompanyId}");
    }

    public async Task JoinTrip(Guid tripId)
    {
        var companyId = current.CompanyId
            ?? throw new HubException("Company context missing.");

        var exists = await db.Trips.AnyAsync(
            x => x.CompanyId == companyId && x.Id == tripId);

        if (!exists)
        {
            throw new HubException("Trip not found.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"trip:{tripId}");
    }

    public async Task JoinDriver(Guid driverId)
    {
        var companyId = current.CompanyId
            ?? throw new HubException("Company context missing.");

        var driver = await db.Drivers.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == driverId);

        if (driver is null)
        {
            throw new HubException("Driver not found.");
        }

        var isSelf = driver.UserId.HasValue && driver.UserId == current.UserId;
        var isDispatcher = current.IsInRole("Administrator")
                           || current.IsInRole("Dispatcher")
                           || current.IsInRole("Moderator");

        if (!isSelf && !isDispatcher)
        {
            throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"driver:{driverId}");
    }
}
