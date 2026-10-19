using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;

namespace ProMapCargo.Mobile.Services;

/// <summary>A chat message with dispatch (server ChatMessageDto).</summary>
public sealed record ChatMessage(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("driverId")] Guid DriverId,
    [property: JsonPropertyName("fromDriver")] bool FromDriver,
    [property: JsonPropertyName("senderName")] string? SenderName,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("readAt")] DateTimeOffset? ReadAt);

public sealed record ChatConversation(
    [property: JsonPropertyName("driverId")] Guid DriverId,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages);

/// <summary>
/// Live chat with dispatch over SignalR (NavigationHub, group of the driver). A message
/// from dispatch raises <see cref="MessageReceived"/> and, when the Poruke screen is not
/// open, a phone notification. Reconnects by itself after a dead zone.
/// </summary>
public sealed class ChatClient
{
    private readonly MobileServerEndpointService endpoints;
    private readonly MobileSessionService session;
    private readonly ApiClient apiClient;

    public ChatClient(MobileServerEndpointService endpoints, MobileSessionService session, ApiClient apiClient)
    {
        this.endpoints = endpoints;
        this.session = session;
        this.apiClient = apiClient;
        ChatClientAccessor.Current = this;
    }

    private readonly SemaphoreSlim gate = new(1, 1);
    private HubConnection? connection;
    private Guid? driverId;

    public event EventHandler<ChatMessage>? MessageReceived;

    /// <summary>True while the Poruke screen is showing (no notification then).</summary>
    public bool IsChatVisible { get; set; }

    public int UnreadCount { get; private set; }

    public event EventHandler? UnreadChanged;

    /// <summary>Connects for this driver (from the trip portal); does nothing when already connected.</summary>
    public async Task StartAsync(Guid forDriverId)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (connection is not null && driverId == forDriverId)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            driverId = forDriverId;
            connection = new HubConnectionBuilder()
                .WithUrl(endpoints.CreateUri("hubs/navigation-telemetry"), options =>
                    options.AccessTokenProvider = () => session.GetAccessTokenAsync(CancellationToken.None))
                .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)])
                .Build();

            connection.On<ChatMessage>("chatMessage", OnMessage);
            connection.Reconnected += async _ => await JoinAsync().ConfigureAwait(false);
            connection.Closed += async _ =>
            {
                // Gave up reconnecting (long dead zone): try again in a minute.
                await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                await TryConnectAsync().ConfigureAwait(false);
            };

            await TryConnectAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> LoadAsync(CancellationToken cancellationToken)
        => (await apiClient.GetChatAsync(cancellationToken).ConfigureAwait(false))?.Messages ?? [];

    public Task<ChatMessage?> SendAsync(string text, CancellationToken cancellationToken)
        => apiClient.SendChatAsync(text, cancellationToken);

    /// <summary>
    /// Unread messages from dispatch as the server has them (also those that came while the
    /// app was closed); shown on the Poruke tab.
    /// </summary>
    public async Task RefreshUnreadAsync(CancellationToken cancellationToken)
    {
        if (!session.IsSignedIn || (IsChatVisible && AppLifecycle.InForeground))
        {
            return;
        }

        try
        {
            var conversation = await apiClient.GetChatAsync(cancellationToken).ConfigureAwait(false);
            SetUnread(conversation?.Messages.Count(x => !x.FromDriver && x.ReadAt is null) ?? 0);
        }
        catch (Exception)
        {
            // No signal or no driver profile: keep the last count.
        }
    }

    private void SetUnread(int count)
    {
        if (UnreadCount == count)
        {
            return;
        }

        UnreadCount = count;
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The driver read the conversation (dispatch sees "pročitano").</summary>
    public async Task MarkReadAsync()
    {
        UnreadCount = 0;
        UnreadChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await apiClient.MarkChatReadAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Marked next time.
        }
    }

    private void OnMessage(ChatMessage message)
    {
        MessageReceived?.Invoke(this, message);
        // The Poruke screen left open in the background is not "seen": count and notify.
        if (message.FromDriver || (IsChatVisible && AppLifecycle.InForeground))
        {
            return;
        }

        UnreadCount++;
        UnreadChanged?.Invoke(this, EventArgs.Empty);
        LocalNotifications.Show(
            message.Id.GetHashCode() & 0x7FFF,
            message.SenderName is { Length: > 0 } name ? $"Dispečer · {name}" : "Poruka od dispečera",
            message.Text,
            "promap-messages");
    }

    private async Task TryConnectAsync()
    {
        if (connection is null || connection.State != HubConnectionState.Disconnected || !session.IsSignedIn)
        {
            return;
        }

        try
        {
            await connection.StartAsync().ConfigureAwait(false);
            await JoinAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No signal: SignalR does not retry a first start, so retry here.
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => TryConnectAsync(), TaskScheduler.Default);
        }
    }

    private async Task JoinAsync()
    {
        if (connection is { State: HubConnectionState.Connected } && driverId is { } id)
        {
            await connection.InvokeAsync("JoinDriver", id).ConfigureAwait(false);
        }
    }

    private async Task StopCoreAsync()
    {
        if (connection is not null)
        {
            var old = connection;
            connection = null;
            driverId = null;
            try
            {
                await old.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }
}

/// <summary>The app's chat connection for code without DI access (sign-out).</summary>
public static class ChatClientAccessor
{
    public static ChatClient? Current { get; set; }
}
