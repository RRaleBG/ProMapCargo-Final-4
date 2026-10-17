using ProMapCargo.Mobile.Services;
using System.Windows.Input;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly MobileSessionService sessionService;
    private readonly MobileServerEndpointService serverEndpointService;
    private readonly IMobileNotifier notifier;
    private CancellationTokenSource? loginCancellationTokenSource;
    private string email = string.Empty;
    private string password = string.Empty;
    private string? errorMessage;
    private bool isBusy;
    private bool isPasswordHidden = true;

    public LoginViewModel(MobileSessionService sessionService, MobileServerEndpointService serverEndpointService, IMobileNotifier notifier)
    {
        this.sessionService = sessionService;
        this.serverEndpointService = serverEndpointService;
        this.notifier = notifier;
        LoginCommand = new Command(async () => await LoginAsync(), () => !IsBusy);
        SkipCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Dashboard), () => !IsBusy);
        OpenSettingsCommand = new Command(async () => await AppNavigation.GoToAsync(AppNavigation.Settings));
        TogglePasswordCommand = new Command(() => IsPasswordHidden = !IsPasswordHidden);
    }

    public event EventHandler? LoginSucceeded;

    public ICommand LoginCommand { get; }

    public ICommand SkipCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand TogglePasswordCommand { get; }

    public string Email
    {
        get => email;
        set => SetProperty(ref email, value);
    }

    public string Password
    {
        get => password;
        set => SetProperty(ref password, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public bool IsPasswordHidden
    {
        get => isPasswordHidden;
        private set
        {
            if (SetProperty(ref isPasswordHidden, value))
            {
                RaisePropertyChanged(nameof(PasswordToggleText));
            }
        }
    }

    public string PasswordToggleText => IsPasswordHidden ? "Prikaži" : "Sakrij";

    public string LoginButtonText => IsBusy ? "Prijavljivanje…" : "Prijavi se";

    /// <summary>Address the app signs in at (changed in Podešavanja).</summary>
    public string ServerText => serverEndpointService.CurrentApiBaseUrl;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaisePropertyChanged(nameof(LoginButtonText));
                // Set again after awaits that continue off the main thread; the buttons
                // may only be updated on it (Android closes the app otherwise).
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    (LoginCommand as Command)?.ChangeCanExecute();
                    (SkipCommand as Command)?.ChangeCanExecute();
                });
            }
        }
    }

    /// <summary>The page appears (again, e.g. back from the settings): show the current server.</summary>
    public void Refresh()
    {
        RaisePropertyChanged(nameof(ServerText));
        if (string.IsNullOrEmpty(Email) && sessionService.CurrentSession?.User.Email is { Length: > 0 } last)
        {
            Email = last;
        }
    }

    private async Task LoginAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Unesite e-poštu i lozinku.";
            return;
        }

        loginCancellationTokenSource?.Cancel();
        loginCancellationTokenSource?.Dispose();
        loginCancellationTokenSource = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            var response = await sessionService.LoginAsync(Email.Trim(), Password, loginCancellationTokenSource.Token).ConfigureAwait(false);
            Password = string.Empty;
            await notifier.ShowSuccessAsync($"Dobrodošli, {response.User.DisplayName}.").ConfigureAwait(false);
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            ErrorMessage = $"Server {serverEndpointService.CurrentApiBaseUrl} ne odgovara. Proverite vezu ili adresu servera.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.InnerException?.Message ?? ex.Message;
        }
        finally
        {
            IsBusy = false;
            loginCancellationTokenSource?.Dispose();
            loginCancellationTokenSource = null;
        }
    }
}
