using System.Net;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Trip expenses waiting for a connection. An expense entered without signal (fuel at a
/// station far from the city) is kept on the phone with its receipt and its own time, and
/// sent when the connection is back, so nothing the driver typed is lost.
/// </summary>
public sealed class ExpenseQueue
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly ApiClient apiClient;
    private readonly MobileSessionService session;
    private readonly IMobileNotifier notifier;
    private readonly string folder = Path.Combine(FileSystem.AppDataDirectory, "expense-queue");
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    private int flushing;

    // Stored without the receipt bytes; those go to a file next to it.
    private sealed record Pending(DriverExpenseDraft Draft, Guid UserId);

    public ExpenseQueue(ApiClient apiClient, MobileSessionService session, IMobileNotifier notifier)
    {
        this.apiClient = apiClient;
        this.session = session;
        this.notifier = notifier;
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                retryAfter = DateTimeOffset.MinValue;
                _ = FlushAsync();
            }
        };
    }

    public event EventHandler? Changed;

    public int PendingCount => Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Length : 0;

    /// <summary>The signed-in driver's expenses still waiting to be sent (without receipt bytes).</summary>
    public IReadOnlyList<DriverExpenseDraft> PendingDrafts()
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var userId = session.CurrentSession?.User.Id;
        return Directory.GetFiles(folder, "*.json")
            .Select(file =>
            {
                try
                {
                    return JsonSerializer.Deserialize<Pending>(File.ReadAllText(file));
                }
                catch (Exception)
                {
                    return null;
                }
            })
            .Where(pending => pending is not null && pending.UserId == userId)
            .Select(pending => pending!.Draft)
            .OrderByDescending(draft => draft.IncurredAt)
            .ToList();
    }

    /// <summary>Sends the expense now; without a connection it is kept and sent later. True when sent.</summary>
    public async Task<bool> SubmitAsync(DriverExpenseDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.AddTripCostAsync(draft, cancellationToken).ConfigureAwait(false);
            _ = FlushAsync();
            return true;
        }
        catch (Exception ex) when (IsConnectionProblem(ex))
        {
            Save(draft);
            return false;
        }
    }

    /// <summary>Sends what is waiting, oldest first; stops at the first connection problem.</summary>
    public async Task FlushAsync()
    {
        if (DateTimeOffset.UtcNow < retryAfter || !session.IsSignedIn || !Directory.Exists(folder) ||
            Interlocked.Exchange(ref flushing, 1) == 1)
        {
            return;
        }

        var sent = 0;
        try
        {
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(File.GetCreationTimeUtc))
            {
                if (Load(file) is not { } pending)
                {
                    Delete(file);
                    continue;
                }

                // Another driver signed in on this phone: not theirs to send.
                if (pending.UserId != session.CurrentSession?.User.Id)
                {
                    continue;
                }

                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    await apiClient.AddTripCostAsync(pending.Draft, timeout.Token).ConfigureAwait(false);
                    Delete(file);
                    sent++;
                }
                catch (Exception ex) when (IsConnectionProblem(ex))
                {
                    retryAfter = DateTimeOffset.UtcNow + RetryDelay;
                    break;
                }
                catch (Exception)
                {
                    // The server refused it (e.g. the trip was removed): sending again cannot help.
                    Delete(file);
                    await notifier.ShowWarningAsync("Jedan sačuvan trošak server nije prihvatio i uklonjen je.").ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref flushing, 0);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (sent > 0)
        {
            await notifier.ShowSuccessAsync(sent == 1 ? "Sačuvan trošak je poslat dispečeru." : $"Poslato sačuvanih troškova: {sent}.").ConfigureAwait(false);
        }
    }

    private static bool IsConnectionProblem(Exception ex)
        => ex is OperationCanceledException ||
           ex is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout };

    private void Save(DriverExpenseDraft draft)
    {
        Directory.CreateDirectory(folder);
        var id = Guid.NewGuid().ToString("N");
        var receiptPath = Path.Combine(folder, id + ".bin");
        if (draft.ReceiptBytes is { Length: > 0 } bytes)
        {
            File.WriteAllBytes(receiptPath, bytes);
        }

        var pending = new Pending(draft with { ReceiptBytes = null }, session.CurrentSession?.User.Id ?? Guid.Empty);
        File.WriteAllText(Path.Combine(folder, id + ".json"), JsonSerializer.Serialize(pending));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Pending? Load(string jsonPath)
    {
        try
        {
            var pending = JsonSerializer.Deserialize<Pending>(File.ReadAllText(jsonPath));
            var receiptPath = Path.ChangeExtension(jsonPath, ".bin");
            return pending is null
                ? null
                : File.Exists(receiptPath) ? pending with { Draft = pending.Draft with { ReceiptBytes = File.ReadAllBytes(receiptPath) } } : pending;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Delete(string jsonPath)
    {
        try
        {
            File.Delete(Path.ChangeExtension(jsonPath, ".bin"));
            File.Delete(jsonPath);
        }
        catch (Exception)
        {
        }
    }
}
