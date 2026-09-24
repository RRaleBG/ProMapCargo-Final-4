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
            options.ApiBaseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:8080/"
                : "http://localhost:8080/";
        });

        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<ApiAuthHandler>();
        builder.Services.AddSingleton<MobileSessionService>();
        builder.Services.AddHttpClient<ApiClient>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<MobileAppOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
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
