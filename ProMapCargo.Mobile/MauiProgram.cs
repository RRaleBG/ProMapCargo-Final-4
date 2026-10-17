using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;
using ProMapCargo.Mobile.Views;

namespace ProMapCargo.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // A connection attempt without signal blocks a pool thread for many seconds
        // (Android HttpURLConnection); a larger minimum keeps the app responsive meanwhile.
        ThreadPool.GetMinThreads(out var workerThreads, out var ioThreads);
        ThreadPool.SetMinThreads(Math.Max(workerThreads, 24), Math.Max(ioThreads, 24));

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.Configure<MobileAppOptions>(options =>
        {
            // 10.0.2.2 is the host PC only inside the Android emulator; a real phone or
            // tablet must use the PC's LAN address (changeable in Podešavanja).
            options.ApiBaseUrl = DeviceInfo.Platform != DevicePlatform.Android
                ? "http://localhost:8080/"
                : DeviceInfo.DeviceType == DeviceType.Virtual
                    ? "http://10.0.2.2:8080/"
                    : "http://192.168.1.2:8080/";
        });

        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<MobileServerEndpointService>();
        // Sign-in and token refresh without ApiAuthHandler (the handler refreshes through it).
        builder.Services.AddHttpClient<AuthApiClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<MobileAppOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        // DelegatingHandlers used with AddHttpMessageHandler must be transient: HttpClientFactory
        // builds a new handler chain on every rotation and a reused instance throws.
        builder.Services.AddTransient<ApiAuthHandler>();
        builder.Services.AddSingleton<MobileSessionService>();
        builder.Services.AddSingleton<IMobileNotifier, MobileNotifier>();
        builder.Services.AddHttpClient<ApiClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<MobileAppOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            // Per-request timeouts are enforced by ApiClient (routing may take much longer than 20 s).
            client.Timeout = TimeSpan.FromMinutes(3);
        })
            .AddHttpMessageHandler<ApiAuthHandler>();

        builder.Services.AddHttpClient<OfflineMapService>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<MobileAppOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        builder.Services.AddHttpClient<OfflineRoutingBundleService>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<MobileAppOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        builder.Services.AddSingleton<OfflineWebCache>();
        builder.Services.AddSingleton<SavedRouteStore>();
        builder.Services.AddSingleton<OfflineRoutingEngine>();
        builder.Services.AddSingleton<DriverPortalStore>();
        builder.Services.AddSingleton<GpsTracker>();
        builder.Services.AddSingleton<GpsTelemetryUploader>();

#if ANDROID
        // Navigation WebView requests go through the offline cache (map from the
        // downloaded file, page and assets from the stored copy when there is no signal).
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("WebViewClient", (handler, _) =>
        {
            var cache = handler.MauiContext?.Services.GetService<OfflineWebCache>();
            if (cache is not null && handler is Microsoft.Maui.Handlers.WebViewHandler webViewHandler)
            {
                handler.PlatformView.SetWebViewClient(new OfflineWebViewClient(webViewHandler, cache));
            }
        });
#endif

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<NavigationViewModel>();
        builder.Services.AddTransient<MapInstallViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<TripDetailsViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<MapInstallPage>();
        builder.Services.AddTransient<MobileNavigationPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<TripDetailsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
