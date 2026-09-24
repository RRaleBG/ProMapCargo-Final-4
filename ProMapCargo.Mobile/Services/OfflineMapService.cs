using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class OfflineMapService(HttpClient httpClient, IOptions<MobileAppOptions> options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string mapsRootPath = Path.Combine(FileSystem.AppDataDirectory, "offline-maps");
    private readonly string manifestPath = Path.Combine(FileSystem.AppDataDirectory, "offline-maps", "manifest.json");

    public async Task<IReadOnlyList<MapPackageCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = await httpClient
            .GetFromJsonAsync<List<MapPackageCatalogItem>>("api/map-packages", SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return items ?? [];
    }

    public async Task<InstalledMapManifest> GetInstalledManifestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(manifestPath))
        {
            return InstalledMapManifest.Empty;
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<InstalledMapManifest>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return manifest ?? InstalledMapManifest.Empty;
    }

    public async Task<bool> HasInstalledMapsAsync(CancellationToken cancellationToken)
    {
        var manifest = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
        return manifest.Packages.Any(package => File.Exists(package.LocalPath));
    }

    public async Task<InstalledMapPackage> DownloadPackageAsync(MapPackageCatalogItem package, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(mapsRootPath);

        var normalizedBaseUrl = options.Value.ApiBaseUrl.TrimEnd('/');
        var relative = package.DownloadUrl.StartsWith('/') ? package.DownloadUrl : $"/{package.DownloadUrl}";
        var downloadUrl = new Uri($"{normalizedBaseUrl}{relative}");

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var targetPath = Path.Combine(mapsRootPath, package.FileName);
        await using var target = File.Create(targetPath);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[1024 * 64];
        long totalRead = 0;
        var contentLength = response.Content.Headers.ContentLength;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalRead += read;

            if (contentLength.HasValue && contentLength.Value > 0)
            {
                progress?.Report((double)totalRead / contentLength.Value);
            }
        }

        progress?.Report(1d);

        var installed = new InstalledMapPackage(
            package.Id,
            package.CountryCode,
            package.DisplayName,
            package.FileName,
            targetPath,
            new FileInfo(targetPath).Length,
            DateTime.UtcNow,
            package.LastModifiedUtc);

        await UpsertManifestAsync(installed, cancellationToken).ConfigureAwait(false);
        return installed;
    }

    private async Task UpsertManifestAsync(InstalledMapPackage package, CancellationToken cancellationToken)
    {
        var existing = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);

        var list = existing.Packages
            .Where(item => !string.Equals(item.Id, package.Id, StringComparison.OrdinalIgnoreCase))
            .Append(package)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var updated = new InstalledMapManifest(DateTime.UtcNow, list);

        Directory.CreateDirectory(mapsRootPath);
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, updated, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}
