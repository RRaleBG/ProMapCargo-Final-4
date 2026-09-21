using System.Windows.Input;
using Microsoft.Maui.Controls;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly MobileSessionService sessionService;
    private string email = "operator@promap.local";
    private string password = "Password123";
    private string? errorMessage;
    private bool isBusy;

    public LoginViewModel(MobileSessionService sessionService)
    {
        this.sessionService = sessionService;
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
            return;
        }

        try
        {
            IsBusy = true;
            await sessionService.LoginAsync(Email.Trim(), Password, CancellationToken.None).ConfigureAwait(false);
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
