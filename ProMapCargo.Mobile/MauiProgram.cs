using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProMapCargo.Mobile.Models;
using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;
using ProMapCargo.Mobile.Views;
using UraniumUI;

namespace ProMapCargo.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseUraniumUI()
            .UseUraniumUIMaterial()
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

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<NavigationViewModel>();
        builder.Services.AddTransient<MapInstallViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<MapInstallPage>();
        builder.Services.AddTransient<MobileNavigationPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
