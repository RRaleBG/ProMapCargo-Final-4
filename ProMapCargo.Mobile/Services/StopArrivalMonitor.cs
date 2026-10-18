using System.Net;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Records a stop by itself, as the driver would tap it:
/// - arrival when the truck is within 200 m of the stop;
/// - completion when, after arriving, the truck leaves again (more than 300 m away), so a
///   long loading or unloading at the stop never ends it early.
/// Two fixes in a row must agree, so one jumpy fix does not count. Without signal it is
/// tried again on the next fixes. The home screen sets the stop from the trip.
/// </summary>
public sealed class StopArrivalMonitor
{
    private const string TrackingHolder = "arrival";
    private const double ArrivalRadiusMeters = 200;
    private const double DepartureRadiusMeters = 300;
    private const double MaxAccuracyMeters = 100;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly GpsTracker tracker;
    private readonly ApiClient apiClient;
    private readonly MobileSessionService session;
    private readonly IMobileNotifier notifier;
    private readonly object sync = new();
    private Target? target;
    private int fixesInARow;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    private int sending;

    private sealed record Target(Guid StopId, double Latitude, double Longitude, string Title, bool Arrived);

    public StopArrivalMonitor(GpsTracker tracker, ApiClient apiClient, MobileSessionService session, IMobileNotifier notifier)
    {
        this.tracker = tracker;
        this.apiClient = apiClient;
        this.session = session;
        this.notifier = notifier;
        tracker.LocationUpdated += OnLocationUpdated;
    }

    /// <summary>Arrival or completion was recorded (the home screen reloads the trip).</summary>
    public event EventHandler? ArrivalRecorded;

    /// <summary>
    /// The stop to watch: the next stop of a running trip with coordinates; <paramref name="arrived"/>
    /// when the truck is already there (then its departure completes it). Null: nothing to watch.
    /// </summary>
    public async Task WatchAsync(Guid? stopId, double? latitude, double? longitude, string? title, bool arrived = false)
    {
        Target? next = stopId is { } id && latitude is { } lat && longitude is { } lon
            ? new Target(id, lat, lon, string.IsNullOrWhiteSpace(title) ? "stanica" : title, arrived)
            : null;

        lock (sync)
        {
            if (target?.StopId != next?.StopId || target?.Arrived != next?.Arrived)
            {
                fixesInARow = 0;
                retryAfter = DateTimeOffset.MinValue;
            }

            target = next;
        }

        // The GPS must run for this even when sharing the position is switched off.
        if (next is not null && session.IsSignedIn)
        {
            await tracker.AcquireAsync(TrackingHolder).ConfigureAwait(false);
        }
        else
        {
            await tracker.ReleaseAsync(TrackingHolder).ConfigureAwait(false);
        }
    }

    private void OnLocationUpdated(object? sender, Location location)
    {
        Target? current;
        lock (sync)
        {
            current = target;
            if (current is null || location.Accuracy is > MaxAccuracyMeters)
            {
                return;
            }

            var meters = Location.CalculateDistance(location.Latitude, location.Longitude, current.Latitude, current.Longitude, DistanceUnits.Kilometers) * 1000;
            var counts = current.Arrived ? meters > DepartureRadiusMeters : meters <= ArrivalRadiusMeters;
            fixesInARow = counts ? fixesInARow + 1 : 0;
            if (fixesInARow < 2 || DateTimeOffset.UtcNow < retryAfter)
            {
                return;
            }
        }

        _ = SendAsync(current);
    }

    private async Task SendAsync(Target stop)
    {
        if (Interlocked.Exchange(ref sending, 1) == 1)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (stop.Arrived)
            {
                await apiClient.CompleteStopAsync(stop.StopId, timeout.Token).ConfigureAwait(false);
                Finish(stop, next: null);
                TryVibrate();
                await notifier.ShowSuccessAsync($"Stanica „{stop.Title}“ je automatski završena.").ConfigureAwait(false);
            }
            else
            {
                await apiClient.ArriveAtStopAsync(stop.StopId, timeout.Token).ConfigureAwait(false);
                // Now wait for the truck to leave: that completes the stop.
                Finish(stop, next: stop with { Arrived = true });
                TryVibrate();
                await notifier.ShowSuccessAsync($"Stigli ste: {stop.Title}. Dolazak je automatski evidentiran.").ConfigureAwait(false);
            }

            ArrivalRecorded?.Invoke(this, EventArgs.Empty);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            // Already recorded (tapped by the driver or on the web), or no longer on the trip:
            // the home screen reloads the trip and sets what to watch next.
            Finish(stop, next: null);
            ArrivalRecorded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // No signal or server error: try again with the next fixes.
            lock (sync)
            {
                retryAfter = DateTimeOffset.UtcNow + RetryDelay;
            }
        }
        finally
        {
            Interlocked.Exchange(ref sending, 0);
        }
    }

    private void Finish(Target stop, Target? next)
    {
        lock (sync)
        {
            if (target?.StopId == stop.StopId)
            {
                target = next;
                fixesInARow = 0;
            }
        }

        if (next is null)
        {
            _ = tracker.ReleaseAsync(TrackingHolder);
        }
    }

    private static void TryVibrate()
    {
        try
        {
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(300));
        }
        catch (Exception)
        {
            // No vibration motor or permission: the message is enough.
        }
    }
}
