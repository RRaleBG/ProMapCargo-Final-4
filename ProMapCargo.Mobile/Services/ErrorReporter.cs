using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProMapCargo.Mobile.Services;

/// <summary>One error from this phone (server: MobileErrorsController).</summary>
public sealed record ErrorReport(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("device")] string Device,
    [property: JsonPropertyName("osVersion")] string OsVersion,
    [property: JsonPropertyName("appVersion")] string AppVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("screen")] string? Screen,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] string? Details,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt);

/// <summary>
/// Sends the app's errors to the server, where the administrator sees them per phone
/// (web: Greške sa telefona): crashes, failed actions (SafeUi) and the driver's own
/// "Prijavi problem". Kept on the phone until sent; a crash is written before the app closes.
/// </summary>
public sealed class ErrorReporter
{
    private const string DeviceIdKey = "device.id";
    private const int MaxQueued = 100;
    private readonly ApiClient apiClient;
    private readonly string queuePath = Path.Combine(FileSystem.AppDataDirectory, "error-queue.json");
    private readonly object sync = new();
    private int flushing;

    public ErrorReporter(ApiClient apiClient)
    {
        this.apiClient = apiClient;
        Current = this;
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                _ = FlushAsync();
            }
        };
    }

    /// <summary>For static helpers (SafeUi) and crash hooks.</summary>
    public static ErrorReporter? Current { get; private set; }

    /// <summary>Records an exception; <paramref name="crash"/> when the app is closing.</summary>
    public void Report(Exception exception, string? screen = null, bool crash = false)
    {
        Enqueue(Create(crash ? "crash" : "error", screen, exception.Message, exception.ToString()));
        if (!crash)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>The driver's "Prijavi problem" with a description.</summary>
    public Task ReportProblemAsync(string description)
    {
        Enqueue(Create("report", Shell.Current?.CurrentState?.Location?.ToString(), description, null));
        return FlushAsync();
    }

    public int PendingCount
    {
        get
        {
            lock (sync)
            {
                return Load().Count;
            }
        }
    }

    public async Task FlushAsync()
    {
        if (Interlocked.Exchange(ref flushing, 1) == 1)
        {
            return;
        }

        try
        {
            List<ErrorReport> pending;
            lock (sync)
            {
                pending = Load();
            }

            if (pending.Count == 0)
            {
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await apiClient.SendErrorReportsAsync(pending, timeout.Token).ConfigureAwait(false);
            lock (sync)
            {
                // Keep what was added while sending.
                Save(Load().Skip(pending.Count).ToList());
            }
        }
        catch (Exception)
        {
            // No signal: sent with the next attempt.
        }
        finally
        {
            Interlocked.Exchange(ref flushing, 0);
        }
    }

    private void Enqueue(ErrorReport report)
    {
        try
        {
            lock (sync)
            {
                var queue = Load();
                queue.Add(report);
                Save(queue.TakeLast(MaxQueued).ToList());
            }
        }
        catch (Exception)
        {
            // Reporting must never break the app.
        }
    }

    private static ErrorReport Create(string kind, string? screen, string message, string? details)
        => new(
            DeviceId(),
            $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}",
            DeviceInfo.Current.VersionString,
            $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})",
            kind,
            screen,
            message,
            details,
            DateTimeOffset.UtcNow);

    // A random id per installation (no hardware ids): tells the phones apart on the admin page.
    private static string DeviceId()
    {
        var id = Preferences.Default.Get(DeviceIdKey, string.Empty);
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N")[..12];
            Preferences.Default.Set(DeviceIdKey, id);
        }

        return id;
    }

    private List<ErrorReport> Load()
    {
        try
        {
            return File.Exists(queuePath)
                ? JsonSerializer.Deserialize<List<ErrorReport>>(File.ReadAllText(queuePath)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void Save(List<ErrorReport> queue) => File.WriteAllText(queuePath, JsonSerializer.Serialize(queue));
}
