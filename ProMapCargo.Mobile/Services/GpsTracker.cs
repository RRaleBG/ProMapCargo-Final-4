namespace ProMapCargo.Mobile.Services;

/// <summary>
/// The one GPS listener of the app. Navigation and the trip (position for dispatch) both
/// need fixes; MAUI allows a single foreground listener, so they share this one. Each user
/// holds it under its own name; the GPS stops when nobody holds it. While it runs, the
/// Android foreground service keeps the fixes coming with the screen off.
/// </summary>
public sealed class GpsTracker
{
    private static readonly TimeSpan FixInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly HashSet<string> holders = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? pollCts;
    private bool listening;

    /// <summary>A new fix (raised on a background or the main thread).</summary>
    public event EventHandler<Location>? LocationUpdated;

    /// <summary>Running state changed (for the status lines on the screens).</summary>
    public event EventHandler? StateChanged;

    public Location? LastLocation { get; private set; }

    public bool IsRunning => listening || pollCts is not null;

    /// <summary>Why the GPS cannot run (permission, location off), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Starts the GPS for <paramref name="holder"/>; false when location is not allowed.</summary>
    public async Task<bool> AcquireAsync(string holder)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!await EnsurePermissionAsync().ConfigureAwait(false))
            {
                return false;
            }

            holders.Add(holder);
            if (IsRunning)
            {
                return true;
            }

            await MainThread.InvokeOnMainThreadAsync(StartAsync).ConfigureAwait(false);
            BackgroundTracking.Start();
            Problem = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReleaseAsync(string holder)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!holders.Remove(holder) || holders.Count > 0)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(Stop).ConfigureAwait(false);
            BackgroundTracking.Stop();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> EnsurePermissionAsync()
    {
        try
        {
            // Permission prompts must run on the main thread.
            var status = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var current = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                return current == PermissionStatus.Granted
                    ? current
                    : await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }).ConfigureAwait(false);

            if (status != PermissionStatus.Granted)
            {
                Problem = "Pristup lokaciji nije dozvoljen. Uključite dozvolu za lokaciju u podešavanjima telefona.";
                StateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }

            // Android 13+: without it the "location is being shared" notification stays hidden.
            if (DeviceInfo.Platform == DevicePlatform.Android && OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
                    {
                        await Permissions.RequestAsync<Permissions.PostNotifications>();
                    }
                }).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Problem = $"Lokacija nije dostupna: {ex.Message}";
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    private async Task StartAsync()
    {
        try
        {
            Geolocation.Default.LocationChanged += OnLocationChanged;
            listening = await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Best, FixInterval));
        }
        catch (Exception)
        {
            listening = false;
        }

        if (!listening)
        {
            // Some devices refuse continuous updates: ask for a fix every few seconds instead.
            Geolocation.Default.LocationChanged -= OnLocationChanged;
            pollCts = new CancellationTokenSource();
            _ = PollAsync(pollCts.Token);
        }
    }

    private void Stop()
    {
        pollCts?.Cancel();
        pollCts = null;

        if (listening)
        {
            Geolocation.Default.LocationChanged -= OnLocationChanged;
            try
            {
                Geolocation.Default.StopListeningForeground();
            }
            catch (Exception)
            {
                // Already stopped by the system.
            }

            listening = false;
        }
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e) => Publish(e.Location);

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)),
                    cancellationToken).ConfigureAwait(false);
                if (location is not null)
                {
                    Problem = null;
                    Publish(location);
                }
            }
            catch (FeatureNotEnabledException)
            {
                Problem = "Lokacija je isključena na telefonu.";
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // No fix right now (tunnel, indoors): try again.
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Publish(Location location)
    {
        LastLocation = location;
        LocationUpdated?.Invoke(this, location);
    }
}
