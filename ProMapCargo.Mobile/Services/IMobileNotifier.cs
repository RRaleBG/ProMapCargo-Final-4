namespace ProMapCargo.Mobile.Services;

public interface IMobileNotifier
{
    Task ShowInfoAsync(string message, CancellationToken cancellationToken = default);

    Task ShowSuccessAsync(string message, CancellationToken cancellationToken = default);

    Task ShowWarningAsync(string message, CancellationToken cancellationToken = default);

    Task ShowErrorAsync(string message, CancellationToken cancellationToken = default);
}
