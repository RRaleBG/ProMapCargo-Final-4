using System.Net.Http.Json;
using System.Text.Json;
using ProMapCargo.Mobile.Models;

namespace ProMapCargo.Mobile.Services;

public sealed class OfflineRoutingBundleService(HttpClient httpClient)
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
            .GetFromJsonAsync<List<OfflineRoutingBundleCatalogItem>>("api/offline-routing-bundles", SerializerOptions, cancellationToken)
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

    public async Task<bool> HasAnyBundleAsync(CancellationToken cancellationToken)
    {
        var manifest = await GetInstalledManifestAsync(cancellationToken).ConfigureAwait(false);
        return manifest.Bundles.Any(bundle => File.Exists(bundle.GraphLocalPath) && File.Exists(bundle.PmtilesLocalPath));
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
        var pmtilesTempPath = Path.Combine(tempDir, catalogItem.PmtilesFileName);

        var graphWeight = Math.Max(catalogItem.GraphSizeBytes, 1);
        var pmtilesWeight = Math.Max(catalogItem.PmtilesSizeBytes, 1);
        var totalWeight = graphWeight + pmtilesWeight;

        var graphProgress = new Progress<double>(value => progress?.Report((value * graphWeight) / totalWeight));
        var pmtilesProgress = new Progress<double>(value => progress?.Report((graphWeight + (value * pmtilesWeight)) / totalWeight));

        try
        {
            await DownloadToFileAsync(catalogItem.GraphDownloadUrl, graphTempPath, graphProgress, cancellationToken).ConfigureAwait(false);
            await DownloadToFileAsync(catalogItem.PmtilesDownloadUrl, pmtilesTempPath, pmtilesProgress, cancellationToken).ConfigureAwait(false);

            var graphFinalPath = Path.Combine(bundleDir, catalogItem.GraphFileName);
            var pmtilesFinalPath = Path.Combine(bundleDir, catalogItem.PmtilesFileName);

            MoveFileAtomic(graphTempPath, graphFinalPath);
            MoveFileAtomic(pmtilesTempPath, pmtilesFinalPath);

            var installed = new InstalledOfflineRoutingBundle(
                catalogItem.Id,
                catalogItem.CountryCode,
                catalogItem.DisplayName,
                catalogItem.Version,
                graphFinalPath,
                pmtilesFinalPath,
                catalogItem.ChecksumSha256,
                DateTime.UtcNow);

            await UpsertManifestAsync(installed, cancellationToken).ConfigureAwait(false);
            progress?.Report(1d);
            return installed;
        }
        catch
        {
            SafeDeleteFile(graphTempPath);
            SafeDeleteFile(pmtilesTempPath);
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

        var url = relativeUrl.StartsWith('/') ? relativeUrl[1..] : relativeUrl;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
