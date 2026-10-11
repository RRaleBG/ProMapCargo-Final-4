using System.Net;
using System.Net.Http.Headers;

namespace ProMapCargo.Mobile.Services;

/// <summary>
/// Downloads a large file (map, routing package) so that a lost connection does not cost
/// what was already received: bytes go to "&lt;target&gt;.part", a broken transfer is
/// resumed with an HTTP Range request (If-Range keeps it to the same file version on the
/// server, a changed file starts over), and the target is replaced only by a complete file.
/// The .part file survives a failed attempt, so tapping "Instaliraj" again continues too.
/// With the server's SHA-256 the finished file is verified; a mismatch (e.g. a part file
/// damaged by a power loss) is downloaded again from the start.
/// </summary>
public static class ResumableDownload
{
    private const int MaxAttempts = 6;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    public static async Task DownloadAsync(
        HttpClient httpClient,
        Uri uri,
        string targetPath,
        string? expectedSha256,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var partPath = targetPath + ".part";
        var validatorPath = partPath + ".etag";

        for (var attempt = 1; ; attempt++)
        {
            var before = PartLength(partPath);
            try
            {
                await DownloadAttemptAsync(httpClient, uri, partPath, validatorPath, progress, cancellationToken).ConfigureAwait(false);
                await VerifyAsync(partPath, validatorPath, expectedSha256, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested && IsTransient(ex))
            {
                // Only attempts without progress count: a long download over a weak
                // connection (or the client timeout per request) keeps going while it advances.
                if (PartLength(partPath) > before) attempt = 0;

                // 2, 4, 8, 16, 30 s: a truck often drives out of coverage for a while.
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(partPath, targetPath, overwrite: true);
        TryDelete(validatorPath);
        progress?.Report(1d);
    }

    private static async Task DownloadAttemptAsync(
        HttpClient httpClient,
        Uri uri,
        string partPath,
        string validatorPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        var validator = existing > 0 && File.Exists(validatorPath) ? await File.ReadAllTextAsync(validatorPath, cancellationToken).ConfigureAwait(false) : null;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0 && !string.IsNullOrWhiteSpace(validator))
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
            request.Headers.IfRange = validator.StartsWith('"') || validator.StartsWith("W/", StringComparison.Ordinal)
                ? new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(validator))
                : new RangeConditionHeaderValue(DateTimeOffset.Parse(validator, System.Globalization.CultureInfo.InvariantCulture));
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The part file is not a prefix of the current file: start over.
            TryDelete(partPath);
            throw new IOException("Preuzimanje se ponavlja od početka.");
        }

        response.EnsureSuccessStatusCode();

        var resumed = response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
        {
            // Full body: the server file changed (or Range is not supported).
            existing = 0;
            var newValidator = response.Headers.ETag?.ToString()
                ?? response.Content.Headers.LastModified?.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (newValidator is null) TryDelete(validatorPath);
            else await File.WriteAllTextAsync(validatorPath, newValidator, cancellationToken).ConfigureAwait(false);
        }

        var total = resumed
            ? response.Content.Headers.ContentRange?.Length
            : response.Content.Headers.ContentLength;

        await using (var destination = new FileStream(partPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64, useAsync: true))
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            var buffer = new byte[1024 * 64];
            var received = existing;
            while (true)
            {
                // A connection that stops delivering without closing would hang forever.
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stall.CancelAfter(StallTimeout);
                int read;
                try
                {
                    read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Veza je prekinuta (nema podataka 30 s).");
                }

                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                if (total is > 0) progress?.Report(Math.Min(1d, (double)received / total.Value));
            }
        }

        var length = new FileInfo(partPath).Length;
        if (total is > 0 && length != total.Value)
        {
            throw new IOException($"Preuzeto {length} od {total.Value} bajtova.");
        }
    }

    // A missing file (404) or another client error does not get better by retrying.
    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        HttpRequestException or IOException or TimeoutException or TaskCanceledException => true,
        _ => false,
    };

    private static async Task VerifyAsync(string partPath, string validatorPath, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return;

        string actual;
        await using (var stream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true))
        {
            actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(partPath);
            TryDelete(validatorPath);
            throw new IOException("Kontrolna suma preuzetog fajla se ne slaže, preuzimanje se ponavlja.");
        }
    }

    private static long PartLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
