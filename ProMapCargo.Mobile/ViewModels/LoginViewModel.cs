using ProMapCargo.Mobile.Services;
using System.Windows.Input;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly MobileSessionService sessionService;
    private readonly IMobileNotifier notifier;
    private CancellationTokenSource? loginCancellationTokenSource;
    private string email = string.Empty;
    private string password = string.Empty;
    private string? errorMessage;
    private bool isBusy;

    public LoginViewModel(MobileSessionService sessionService, IMobileNotifier notifier)
    {
        this.sessionService = sessionService;
        this.notifier = notifier;
        LoginCommand = new Command(async () => await LoginAsync(), () => !IsBusy);
    }

    public event EventHandler? LoginSucceeded;

    public ICommand LoginCommand { get; }

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

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value) && LoginCommand is Command command)
            {
                command.ChangeCanExecute();
            }
        }
    }

    private async Task LoginAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email and password are required.";
            await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
            return;
        }

        loginCancellationTokenSource?.Cancel();
        loginCancellationTokenSource?.Dispose();
        loginCancellationTokenSource = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            await sessionService.LoginAsync(Email.Trim(), Password, loginCancellationTokenSource.Token).ConfigureAwait(false);
            await notifier.ShowSuccessAsync("Signed in successfully.").ConfigureAwait(false);
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Login canceled.";
            await notifier.ShowInfoAsync(ErrorMessage).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            await notifier.ShowWarningAsync(ErrorMessage).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = ex.Message;
            await notifier.ShowErrorAsync(ErrorMessage).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            await notifier.ShowErrorAsync(ErrorMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.InnerException?.Message ?? ex.Message;
            await notifier.ShowErrorAsync(ErrorMessage).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
            loginCancellationTokenSource?.Dispose();
            loginCancellationTokenSource = null;
        }
    }
}
