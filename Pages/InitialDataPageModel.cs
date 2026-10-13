using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace ProMapCargo.Api.Pages;

/// <summary>
/// A page whose first load comes from the server: OnGet puts the JSON of the page's GET
/// endpoints into the page (same services and JSON settings as the API controllers), and
/// wwwroot/js/core/initial-data.js hands it to the page script instead of a request.
/// Refresh, search and filters still call the API with AJAX.
/// </summary>
public abstract class InitialDataPageModel : PageModel
{
    private static JsonSerializerOptions? embedOptions;
    private readonly Dictionary<string, object> initial = [];

    /// <summary>Embeds data as the answer of url; null (no company context) embeds nothing.</summary>
    protected void Seed(string url, object? data)
    {
        if (data is not null) initial[url] = data;
    }

    /// <summary>For BusinessService calls, which throw when there is no company context.</summary>
    protected static async Task<object?> OrNull<T>(Func<Task<T>> load)
    {
        try
        {
            return await load();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IHtmlContent RenderInitialData()
    {
        if (initial.Count == 0) return HtmlString.Empty;

        // The API's JSON settings, with the default encoder: it escapes <, > and &, so the
        // JSON cannot close the <script> element it is embedded in.
        var options = embedOptions ??= new JsonSerializerOptions(
            HttpContext.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions)
        {
            Encoder = JavaScriptEncoder.Default,
        };

        var html = new HtmlContentBuilder();
        foreach (var (url, data) in initial)
        {
            html.AppendHtml($"<script type=\"application/json\" data-initial-url=\"{WebUtility.HtmlEncode(url)}\">");
            html.AppendHtml(JsonSerializer.Serialize(data, data.GetType(), options));
            html.AppendHtml("</script>\n");
        }
        return html;
    }
}
