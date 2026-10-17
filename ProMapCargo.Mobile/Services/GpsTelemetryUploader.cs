using System.Net;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Sends the truck's GPS position to the server (POST /api/navigation/gps) while the
/// driver has a running trip, so Monitoring and Dispatch show the real position. Positions
/// are thinned out (one every 10 s while driving, every minute while standing) and kept in
/// a queue on the phone without signal; they are sent with their own time once the
/// connection is back. Off a trip nothing is sent.
/// </summary>
public sealed class GpsTelemetryUploader
{
    private const string TrackingHolder = "trip";
    private const string VehicleKey = "gps.target_vehicle";
    private const string TripKey = "gps.target_trip";
    private const int MaxQueued = 720; // about two hours of driving
    private static readonly TimeSpan MovingInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StandingInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly ApiClient apiClient;
    private readonly GpsTracker tracker;
    private readonly MobileSessionService session;
    private readonly string queuePath = Path.Combine(FileSystem.AppDataDirectory, "gps-queue.json");
    private readonly object sync = new();
    private readonly List<GpsPositionRequest> queue;
    private DateTimeOffset lastQueuedAt = DateTimeOffset.MinValue;
    private Location? lastQueuedLocation;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    private int flushing;
    private bool tripRunning;

    public GpsTelemetryUploader(ApiClient apiClient, GpsTracker tracker, MobileSessionService session)
    {
        this.apiClient = apiClient;
        this.tracker = tracker;
        this.session = session;
        queue = LoadQueue();
        tracker.LocationUpdated += OnLocationUpdated;
        tracker.StateChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                retryAfter = DateTimeOffset.MinValue;
                _ = FlushAsync();
            }
        };
    }

    public Guid? VehicleId { get; private set; } = ReadGuid(VehicleKey);

    public Guid? TripId { get; private set; } = ReadGuid(TripKey);

    public DateTimeOffset? LastSentAt { get; private set; }

    public int PendingCount
    {
        get
        {
            lock (sync)
            {
                return queue.Count;
            }
        }
    }

    /// <summary>Sending positions right now (trip running, signed in, allowed in the settings).</summary>
    public bool IsActive => tripRunning && VehicleId is not null && session.IsSignedIn && AppSettings.ShareLocation && tracker.IsRunning;

    public event EventHandler? StatusChanged;

    /// <summary>
    /// The driver's trip from the portal: positions go to this vehicle and trip. A running
    /// trip keeps the GPS on (also with the screen off); without a trip it is released.
    /// </summary>
    public async Task SetTripAsync(Guid? vehicleId, Guid? tripId, bool running)
    {
        VehicleId = vehicleId;
        TripId = tripId;
        WriteGuid(VehicleKey, vehicleId);
        WriteGuid(TripKey, tripId);
        tripRunning = running && vehicleId is not null;

        if (tripRunning && session.IsSignedIn && AppSettings.ShareLocation)
        {
            await tracker.AcquireAsync(TrackingHolder).ConfigureAwait(false);
        }
        else
        {
            await tracker.ReleaseAsync(TrackingHolder).ConfigureAwait(false);
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
        _ = FlushAsync();
    }

    /// <summary>Signed out or sharing switched off: stop sending (optionally drop what is pending).</summary>
    public async Task StopAsync(bool clearQueue)
    {
        tripRunning = false;
        await tracker.ReleaseAsync(TrackingHolder).ConfigureAwait(false);
        if (clearQueue)
        {
            lock (sync)
            {
                queue.Clear();
                SaveQueue();
            }
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public string StatusText
    {
        get
        {
            if (!AppSettings.ShareLocation)
            {
                return "Slanje lokacije je isključeno u podešavanjima.";
            }

            if (tracker.Problem is { } problem)
            {
                return problem;
            }

            var pending = PendingCount;
            if (!IsActive)
            {
                return pending > 0
                    ? $"Lokacija se trenutno ne šalje · na čekanju {pending} pozicija"
                    : "Lokacija se šalje dispečeru dok je tura u toku.";
            }

            var last = LastSentAt is { } at ? $" · poslednja u {at.ToLocalTime():HH:mm}" : string.Empty;
            return pending > 1
                ? $"Bez signala · {pending} pozicija čeka slanje{last}"
                : $"Lokacija se šalje dispečeru{last}";
        }
    }

    private void OnLocationUpdated(object? sender, Location location)
    {
        if (!tripRunning || VehicleId is not { } vehicleId || !session.IsSignedIn || !AppSettings.ShareLocation)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        double? speedKmh = location.Speed is { } mps and >= 0 ? mps * 3.6 : null;
        // Driving by speed, or by distance when the fix carries no speed (some phones).
        var moved = lastQueuedLocation is { } previous
            ? Location.CalculateDistance(previous, location, DistanceUnits.Kilometers) * 1000
            : double.MaxValue;
        var interval = speedKmh is > 3 || moved > 100 ? MovingInterval : StandingInterval;
        if (now - lastQueuedAt < interval)
        {
            return;
        }

        lastQueuedAt = now;
        lastQueuedLocation = location;
        var position = new GpsPositionRequest(
            vehicleId,
            TripId,
            location.Latitude,
            location.Longitude,
            location.Accuracy,
            speedKmh,
            location.Course is { } course and >= 0 ? course : null,
            location.Timestamp == default ? now : location.Timestamp);

        lock (sync)
        {
            queue.Add(position);
            if (queue.Count > MaxQueued)
            {
                queue.RemoveRange(0, queue.Count - MaxQueued);
            }

            SaveQueue();
        }

        _ = FlushAsync();
    }

    /// <summary>Sends the queue, oldest first; stops at the first network error.</summary>
    public async Task FlushAsync()
    {
        if (DateTimeOffset.UtcNow < retryAfter || !session.IsSignedIn ||
            Interlocked.Exchange(ref flushing, 1) == 1)
        {
            return;
        }

        try
        {
            while (true)
            {
                GpsPositionRequest next;
                lock (sync)
                {
                    if (queue.Count == 0)
                    {
                        break;
                    }

                    next = queue[0];
                }

                try
                {
                    await apiClient.SendGpsPositionAsync(next, CancellationToken.None).ConfigureAwait(false);
                    LastSentAt = DateTimeOffset.UtcNow;
                    Drop(next);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                {
                    // Vehicle no longer in the company (or invalid data): sending it again cannot help.
                    Drop(next);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
                {
                    retryAfter = DateTimeOffset.UtcNow + RetryDelay;
                    break;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref flushing, 0);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Drop(GpsPositionRequest position)
    {
        lock (sync)
        {
            queue.Remove(position);
            SaveQueue();
        }
    }

    private List<GpsPositionRequest> LoadQueue()
    {
        try
        {
            return File.Exists(queuePath)
                ? JsonSerializer.Deserialize<List<GpsPositionRequest>>(File.ReadAllText(queuePath)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    // Called under the lock.
    private void SaveQueue()
    {
        try
        {
            File.WriteAllText(queuePath, JsonSerializer.Serialize(queue));
        }
        catch (Exception)
        {
            // Only the offline copy is lost; the positions in memory are still sent.
        }
    }

    private static Guid? ReadGuid(string key)
        => Guid.TryParse(Preferences.Default.Get(key, string.Empty), out var value) ? value : null;

    private static void WriteGuid(string key, Guid? value)
    {
        if (value is { } id)
        {
            Preferences.Default.Set(key, id.ToString());
        }
        else
        {
            Preferences.Default.Remove(key);
        }
    }
}
