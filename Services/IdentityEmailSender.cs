namespace ProMapCargo.Api.Services;

/// <summary>
/// Delivery channel for Identity generated links (password reset, email confirmation).
/// </summary>
public interface IIdentityEmailSender
{
    Task SendPasswordResetLinkAsync(string email, string resetLink, CancellationToken cancellationToken = default);

    Task SendEmailConfirmationLinkAsync(string email, string confirmationLink, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default sender used until an SMTP/transactional provider is configured.
/// Links are written to the application log so they remain recoverable by an operator.
/// </summary>
public sealed class LoggingIdentityEmailSender(ILogger<LoggingIdentityEmailSender> logger) : IIdentityEmailSender
{
    public Task SendPasswordResetLinkAsync(string email, string resetLink, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "No email provider configured. Password reset link for {Email}: {ResetLink}",
            email,
            resetLink);

        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationLinkAsync(string email, string confirmationLink, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "No email provider configured. Email confirmation link for {Email}: {ConfirmationLink}",
            email,
            confirmationLink);

        return Task.CompletedTask;
    }
}
