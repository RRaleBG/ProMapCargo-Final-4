using System.Net.Http.Json;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class OfflineMapService(HttpClient httpClient, MobileServerEndpointService serverEndpointService)
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
            .GetFromJsonAsync<List<MapPackageCatalogItem>>(serverEndpointService.CreateUri("api/map-packages"), SerializerOptions, cancellationToken)
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

        // The installed map stays usable until the new one is complete (an update used to
        // overwrite it in place, so a lost connection left a broken map).
        var targetPath = Path.Combine(mapsRootPath, package.FileName);
        await ResumableDownload.DownloadAsync(httpClient, serverEndpointService.CreateUri(package.DownloadUrl), targetPath, package.Sha256, progress, cancellationToken)
            .ConfigureAwait(false);

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

    /// <summary>Removes an installed map (file and manifest entry) to free space.</summary>
    public async Task DeleteAsync(string packageId, CancellationToken cancellationToken)
    {
        var existing = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
        foreach (var package in existing.Packages.Where(item => string.Equals(item.Id, packageId, StringComparison.OrdinalIgnoreCase)))
        {
            TryDelete(package.LocalPath);
            TryDelete(package.LocalPath + ".part");
        }

        await WriteManifestAsync(existing.Packages.Where(item => !string.Equals(item.Id, packageId, StringComparison.OrdinalIgnoreCase)), cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // The map is open in the navigation WebView: it goes with the next delete.
        }
    }

    private async Task UpsertManifestAsync(InstalledMapPackage package, CancellationToken cancellationToken)
    {
        var existing = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);

        await WriteManifestAsync(
            existing.Packages
                .Where(item => !string.Equals(item.Id, package.Id, StringComparison.OrdinalIgnoreCase))
                .Append(package),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteManifestAsync(IEnumerable<InstalledMapPackage> packages, CancellationToken cancellationToken)
    {
        var updated = new InstalledMapManifest(
            DateTime.UtcNow,
            packages.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());

        Directory.CreateDirectory(mapsRootPath);
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, updated, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}
