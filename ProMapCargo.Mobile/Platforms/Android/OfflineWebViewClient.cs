using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

/// <summary>
/// MAUI's WebView client (keeps Navigating/Navigated working) plus offline support:
/// requests to the ProMap server are answered by <see cref="OfflineWebCache"/>.
/// </summary>
public sealed class OfflineWebViewClient(WebViewHandler handler, OfflineWebCache cache) : MauiWebViewClient(handler)
{
    public override WebResourceResponse? ShouldInterceptRequest(global::Android.Webkit.WebView? view, IWebResourceRequest? request)
    {
        if (request?.Url?.ToString() is not { } urlText || !Uri.TryCreate(urlText, UriKind.Absolute, out var url))
        {
            return base.ShouldInterceptRequest(view, request);
        }

        try
        {
            var headers = request.RequestHeaders is { } requestHeaders
                ? requestHeaders.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (cache.Handle(request.Method ?? "GET", url, headers) is { } answer)
            {
                // Map archive: hand over a Java stream whose skip() seeks, so the
                // WebView can jump to the requested range without reading through it.
                var body = answer.RangeFilePath is { } filePath
                    ? new global::Android.Runtime.InputStreamInvoker(new SeekableFileInputStream(filePath, answer.RangeEnd))
                    : answer.Body;

                return new WebResourceResponse(
                    answer.MimeType,
                    answer.Charset ?? "utf-8",
                    answer.StatusCode,
                    answer.ReasonPhrase,
                    answer.Headers,
                    body);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Offline web cache failed for {url}: {ex.Message}");
        }

        return base.ShouldInterceptRequest(view, request);
    }
}

/// <summary>
/// Java InputStream over the first <c>end</c> bytes of a file, with a seeking skip().
/// </summary>
internal sealed class SeekableFileInputStream(string path, long end) : Java.IO.InputStream
{
    private readonly FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);

    private long Remaining => Math.Max(0, Math.Min(end, file.Length) - file.Position);

    public override int Read() => Remaining > 0 ? file.ReadByte() : -1;

    public override int Read(byte[]? buffer, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var allowed = (int)Math.Min(length, Remaining);
        if (allowed == 0)
        {
            return -1;
        }

        var read = file.Read(buffer, offset, allowed);
        return read == 0 ? -1 : read;
    }

    public override long Skip(long byteCount)
    {
        var target = Math.Clamp(file.Position + byteCount, 0, Math.Min(end, file.Length));
        var skipped = target - file.Position;
        file.Position = target;
        return skipped;
    }

    public override int Available() => (int)Math.Min(int.MaxValue, Remaining);

    public override void Close()
    {
        file.Dispose();
        base.Close();
    }
}
