using System.Net.Http.Json;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class OfflineRoutingBundleService(HttpClient httpClient, MobileServerEndpointService serverEndpointService)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string bundlesRootPath = Path.Combine(FileSystem.AppDataDirectory, "offline-routing-bundles");
    private readonly string manifestPath = Path.Combine(FileSystem.AppDataDirectory, "offline-routing-bundles", "manifest.json");

    public async Task<IReadOnlyList<OfflineRoutingBundleCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = await httpClient
            .GetFromJsonAsync<List<OfflineRoutingBundleCatalogItem>>(serverEndpointService.CreateUri("api/offline-routing-bundles"), SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return items ?? [];
    }

    public async Task<InstalledOfflineRoutingManifest> GetInstalledManifestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(manifestPath))
        {
            return InstalledOfflineRoutingManifest.Empty;
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<InstalledOfflineRoutingManifest>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return manifest ?? InstalledOfflineRoutingManifest.Empty;
    }

    /// <summary>
    /// A routing package on the server that is newer than the installed one (or the
    /// only one, when none is installed yet); null when up to date or unreachable.
    /// </summary>
    public async Task<OfflineRoutingBundleCatalogItem?> FindNewerOnServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var installed = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in catalog)
            {
                var current = installed.Bundles.FirstOrDefault(bundle => string.Equals(bundle.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                if (current is not null && string.CompareOrdinal(item.Version, current.Version) > 0)
                {
                    return item;
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return null;
    }

    public async Task<bool> HasAnyBundleAsync(CancellationToken cancellationToken)
    {
        var manifest = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
        return manifest.Bundles.Any(bundle => File.Exists(bundle.GraphLocalPath) && File.Exists(bundle.PlacesLocalPath));
    }

    public async Task<InstalledOfflineRoutingBundle> DownloadAndInstallAsync(
        OfflineRoutingBundleCatalogItem catalogItem,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogItem);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(bundlesRootPath);

        var bundleDir = Path.Combine(bundlesRootPath, catalogItem.Id);
        var tempDir = Path.Combine(bundleDir, "_tmp");
        Directory.CreateDirectory(bundleDir);

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }

        Directory.CreateDirectory(tempDir);

        var graphTempPath = Path.Combine(tempDir, catalogItem.GraphFileName);
        var placesTempPath = Path.Combine(tempDir, catalogItem.PlacesFileName);

        var graphWeight = Math.Max(catalogItem.GraphSizeBytes, 1);
        var placesWeight = Math.Max(catalogItem.PlacesSizeBytes, 1);
        var totalWeight = graphWeight + placesWeight;

        var graphProgress = new Progress<double>(value => progress?.Report((value * graphWeight) / totalWeight));
        var placesProgress = new Progress<double>(value => progress?.Report((graphWeight + (value * placesWeight)) / totalWeight));

        try
        {
            await DownloadToFileAsync(catalogItem.GraphDownloadUrl, graphTempPath, graphProgress, cancellationToken).ConfigureAwait(false);
            await DownloadToFileAsync(catalogItem.PlacesDownloadUrl, placesTempPath, placesProgress, cancellationToken).ConfigureAwait(false);

            var graphFinalPath = Path.Combine(bundleDir, catalogItem.GraphFileName);
            var placesFinalPath = Path.Combine(bundleDir, catalogItem.PlacesFileName);

            MoveFileAtomic(graphTempPath, graphFinalPath);
            MoveFileAtomic(placesTempPath, placesFinalPath);

            var installed = new InstalledOfflineRoutingBundle(
                catalogItem.Id,
                catalogItem.CountryCode,
                catalogItem.DisplayName,
                catalogItem.Version,
                graphFinalPath,
                placesFinalPath,
                catalogItem.ChecksumSha256,
                DateTime.UtcNow);

            await UpsertManifestAsync(installed, cancellationToken).ConfigureAwait(false);
            progress?.Report(1d);
            return installed;
        }
        catch
        {
            SafeDeleteFile(graphTempPath);
            SafeDeleteFile(placesTempPath);
            throw;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private async Task DownloadToFileAsync(string relativeUrl, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl))
        {
            throw new ArgumentException("Bundle URL is missing.", nameof(relativeUrl));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, serverEndpointService.CreateUri(relativeUrl));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var destination = File.Create(targetPath);
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

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalRead += read;

            if (contentLength.HasValue && contentLength.Value > 0)
            {
                progress?.Report((double)totalRead / contentLength.Value);
            }
        }

        progress?.Report(1d);
    }

    private async Task UpsertManifestAsync(InstalledOfflineRoutingBundle bundle, CancellationToken cancellationToken)
    {
        var existing = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);

        var updatedBundles = existing.Bundles
            .Where(item => !string.Equals(item.Id, bundle.Id, StringComparison.OrdinalIgnoreCase))
            .Append(bundle)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var updated = new InstalledOfflineRoutingManifest(DateTime.UtcNow, updatedBundles);

        Directory.CreateDirectory(bundlesRootPath);
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, updated, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void MoveFileAtomic(string source, string destination)
    {
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        File.Move(source, destination);
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
