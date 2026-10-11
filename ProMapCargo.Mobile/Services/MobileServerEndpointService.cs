using Microsoft.Extensions.Options;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class MobileServerEndpointService
{
    private const string PreferencesKey = "mobile_api_base_url_v1";
    private readonly string defaultApiBaseUrl;

    public MobileServerEndpointService(IOptions<MobileAppOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        defaultApiBaseUrl = NormalizeApiBaseUrl(options.Value.ApiBaseUrl);
        CurrentApiBaseUrl = LoadStoredApiBaseUrl() ?? defaultApiBaseUrl;
    }

    public string CurrentApiBaseUrl { get; private set; }

    public string NavigationUrl => CreateUri("navigation?embedded=1&mobile=1").AbsoluteUri;

    /// <summary>
    /// Creates an absolute URI for the currently configured mobile server endpoint.
    /// </summary>
    public Uri CreateUri(string relativeOrAbsoluteUri)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsoluteUri))
        {
            throw new ArgumentException("URI is required.", nameof(relativeOrAbsoluteUri));
        }

        if (Uri.TryCreate(relativeOrAbsoluteUri, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri;
        }

        var relative = relativeOrAbsoluteUri.StartsWith('/')
            ? relativeOrAbsoluteUri[1..]
            : relativeOrAbsoluteUri;

        return new Uri(new Uri(CurrentApiBaseUrl), relative);
    }

    /// <summary>
    /// Validates and persists the mobile server endpoint for future app launches.
    /// </summary>
    public bool TrySaveApiBaseUrl(string value, out string normalizedApiBaseUrl, out string errorMessage)
    {
        try
        {
            normalizedApiBaseUrl = NormalizeApiBaseUrl(value);
            Preferences.Default.Set(PreferencesKey, normalizedApiBaseUrl);
            CurrentApiBaseUrl = normalizedApiBaseUrl;
            errorMessage = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            normalizedApiBaseUrl = CurrentApiBaseUrl;
            errorMessage = ex.Message;
            return false;
        }
    }

    private static string NormalizeApiBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Unesite adresu servera.", nameof(value));
        }

        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"http://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Adresa servera mora biti potpuni HTTP ili HTTPS URL, na primer http://192.168.1.10:8080/.", nameof(value));
        }

        var path = string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/"
            ? "/"
            : $"{uri.AbsolutePath.TrimEnd('/')}/";

        var builder = new UriBuilder(uri)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }

    private static string? LoadStoredApiBaseUrl()
    {
        try
        {
            var storedValue = Preferences.Default.Get(PreferencesKey, string.Empty);
            return string.IsNullOrWhiteSpace(storedValue)
                ? null
                : NormalizeApiBaseUrl(storedValue);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
