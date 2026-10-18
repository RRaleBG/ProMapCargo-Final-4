using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Input;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

/// <summary>
/// One row of "Poruke": a chat message (bubble, mine on the right) or an alert/route
/// card from the system; sorted by time, newest at the bottom like a chat.
/// </summary>
public sealed record MessageItem(
    Guid Id,
    DateTimeOffset At,
    bool IsChat,
    bool FromMe,
    string Title,
    string Text,
    string Meta,
    string Icon,
    bool IsCritical,
    bool IsWarning,
    bool IsOpen);

/// <summary>
/// "Poruke": live chat with dispatch (SignalR) together with the alerts on the driver's
/// trips and the routes sent by dispatch. The last list stays on the phone for no signal.
/// </summary>
public sealed class MessagesViewModel : ViewModelBase
{
    private const string CacheKey = "messages";
    private const string ChatCacheKey = "chat";
    private readonly ApiClient apiClient;
    private readonly MobileSessionService session;
    private readonly DriverDataCache cache;
    private readonly ChatClient chat;
    private readonly IMobileNotifier notifier;
    private List<DriverMessage> alerts = [];
    private List<ChatMessage> chatMessages = [];
    private string statusText = string.Empty;
    private string draft = string.Empty;
    private bool isBusy;
    private bool isSending;
    private bool isPullRefreshing;
    private bool showSignIn;
    private bool showNotDriver;

    public MessagesViewModel(ApiClient apiClient, MobileSessionService session, DriverDataCache cache, ChatClient chat, IMobileNotifier notifier)
    {
        this.apiClient = apiClient;
        this.session = session;
        this.cache = cache;
        this.chat = chat;
        this.notifier = notifier;
        RefreshCommand = new Command(async () =>
        {
            await LoadAsync();
            IsPullRefreshing = false;
        });
        SignInCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Login));
        SendCommand = new Command(async () => await SendAsync(), () => CanSend);
        chat.MessageReceived += (_, message) => MainThread.BeginInvokeOnMainThread(() => AddChat(message));
    }

    public ObservableCollection<MessageItem> Messages { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SignInCommand { get; }

    public ICommand SendCommand { get; }

    /// <summary>Raised after new rows, so the page scrolls to the newest.</summary>
    public event EventHandler? Updated;

    public bool IsEmpty => Messages.Count == 0 && !ShowSignIn && !ShowNotDriver;

    public bool CanWrite => session.IsSignedIn && !ShowNotDriver;

    public bool CanSend => CanWrite && !isSending && !string.IsNullOrWhiteSpace(Draft);

    public string Draft
    {
        get => draft;
        set
        {
            if (SetProperty(ref draft, value))
            {
                (SendCommand as Command)?.ChangeCanExecute();
            }
        }
    }

    public string Subtitle => ShowSignIn || ShowNotDriver
        ? string.Empty
        : "Razgovor sa dispečerom i obaveštenja o turi";

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public bool IsPullRefreshing
    {
        get => isPullRefreshing;
        set => SetProperty(ref isPullRefreshing, value);
    }

    public bool ShowSignIn
    {
        get => showSignIn;
        private set => SetProperty(ref showSignIn, value);
    }

    public bool ShowNotDriver
    {
        get => showNotDriver;
        private set => SetProperty(ref showNotDriver, value);
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await session.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
            if (session.CurrentSession?.User.Id is not { } userId)
            {
                await MainThread.InvokeOnMainThreadAsync(() => Apply(signIn: true, notDriver: false, string.Empty));
                return;
            }

            if (Messages.Count == 0)
            {
                alerts = cache.Load<List<DriverMessage>>(CacheKey, userId)?.Value ?? [];
                chatMessages = cache.Load<List<ChatMessage>>(ChatCacheKey, userId)?.Value ?? [];
                await MainThread.InvokeOnMainThreadAsync(() => Apply(false, false, alerts.Count + chatMessages.Count > 0 ? "Sačuvane poruke" : string.Empty));
            }

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var conversation = await apiClient.GetChatAsync(timeout.Token).ConfigureAwait(false);
                alerts = (await apiClient.GetDriverMessagesAsync(timeout.Token).ConfigureAwait(false)).ToList();
                chatMessages = conversation?.Messages.ToList() ?? [];
                cache.Save(CacheKey, alerts, userId);
                cache.Save(ChatCacheKey, chatMessages, userId);
                if (conversation is not null)
                {
                    _ = chat.StartAsync(conversation.DriverId);
                }

                await MainThread.InvokeOnMainThreadAsync(() => Apply(false, false, $"Osveženo {DateTime.Now:HH:mm}"));
                await chat.MarkReadAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                alerts = [];
                chatMessages = [];
                await MainThread.InvokeOnMainThreadAsync(() => Apply(false, notDriver: true, string.Empty));
            }
            catch (Exception)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    StatusText = Messages.Count > 0 ? "Bez veze · prikazane su sačuvane poruke" : "Bez veze sa serverom. Poruke stižu kad se veza vrati.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendAsync()
    {
        var text = Draft.Trim();
        if (text.Length == 0)
        {
            return;
        }

        isSending = true;
        (SendCommand as Command)?.ChangeCanExecute();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var sent = await chat.SendAsync(text, timeout.Token).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Draft = string.Empty;
                if (sent is not null)
                {
                    AddChat(sent);
                }
            });
        }
        catch (Exception ex)
        {
            await notifier.ShowErrorAsync($"Poruka nije poslata: {ApiClient.DescribeError(ex)}").ConfigureAwait(false);
        }
        finally
        {
            isSending = false;
            MainThread.BeginInvokeOnMainThread(() => (SendCommand as Command)?.ChangeCanExecute());
        }
    }

    // A live message (or the echo of the driver's own one): once, at the bottom.
    private void AddChat(ChatMessage message)
    {
        if (chatMessages.Any(item => item.Id == message.Id))
        {
            return;
        }

        chatMessages.Add(message);
        if (session.CurrentSession?.User.Id is { } userId)
        {
            cache.Save(ChatCacheKey, chatMessages, userId);
        }

        Messages.Add(ToItem(message));
        RaisePropertyChanged(nameof(IsEmpty));
        Updated?.Invoke(this, EventArgs.Empty);
        if (chat.IsChatVisible && !message.FromDriver)
        {
            _ = chat.MarkReadAsync();
        }
    }

    private void Apply(bool signIn, bool notDriver, string status)
    {
        ShowSignIn = signIn;
        ShowNotDriver = notDriver;
        Messages.Clear();
        foreach (var item in alerts.Select(ToItem).Concat(chatMessages.Select(ToItem)).OrderBy(item => item.At))
        {
            Messages.Add(item);
        }

        StatusText = status;
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(CanWrite));
        RaisePropertyChanged(nameof(Subtitle));
        (SendCommand as Command)?.ChangeCanExecute();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static MessageItem ToItem(ChatMessage message)
        => new(
            message.Id,
            message.CreatedAt,
            IsChat: true,
            FromMe: message.FromDriver,
            Title: message.FromDriver ? "Vi" : message.SenderName is { Length: > 0 } name ? name : "Dispečer",
            Text: message.Text,
            Meta: message.CreatedAt.ToLocalTime().ToString("dd.MM. HH:mm") + (message.FromDriver && message.ReadAt is not null ? " · pročitano" : string.Empty),
            Icon: string.Empty,
            IsCritical: false,
            IsWarning: false,
            IsOpen: false);

    private static MessageItem ToItem(DriverMessage message)
    {
        var title = message.Kind switch
        {
            "route" => "Ruta od dispečera",
            "incident" => "Vaša prijava incidenta",
            _ => message.Type switch
            {
                "Delay" => "Kašnjenje",
                "Border" => "Granica",
                "Restriction" => "Ograničenje na ruti",
                "OffRoute" => "Skretanje sa rute",
                "Incident" => "Incident",
                "Compliance" => "Dokumenti",
                "Ban" or "TruckBan" => "Zabrana vožnje",
                _ => "Obaveštenje",
            },
        };

        var status = message.Status switch
        {
            "Open" => "otvoreno",
            "Acknowledged" => "primljeno",
            "Resolved" => "rešeno",
            "Pending" or "Sent" => "čeka odgovor",
            "Accepted" => "prihvaćeno",
            "Rejected" => "odbijeno",
            _ => message.Status?.ToLowerInvariant() ?? string.Empty,
        };

        var meta = string.Join(" · ", new[]
        {
            message.CreatedAt.ToLocalTime().ToString("dd.MM. HH:mm"),
            message.TripNumber is { Length: > 0 } trip ? $"tura {trip}" : null,
            status,
        }.Where(part => !string.IsNullOrEmpty(part)));

        var icon = message.Kind switch
        {
            "route" => "ic_tab_trip.svg",
            "incident" => "ic_ban.svg",
            _ => "ic_tab_messages.svg",
        };

        return new MessageItem(
            message.Id,
            message.CreatedAt,
            IsChat: false,
            FromMe: false,
            title,
            message.Text,
            meta,
            icon,
            message.Severity is "Critical",
            message.Severity is "Warning",
            message.Status is "Open" or "Pending" or "Sent");
    }
}
