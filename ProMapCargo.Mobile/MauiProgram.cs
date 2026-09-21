using Microsoft.Extensions.Logging;
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
                ? "https://10.0.2.2:5001/"
                : "https://localhost:5001/";
        });
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<ApiAuthHandler>();
        builder.Services.AddSingleton<MobileSessionService>();
        builder.Services.AddHttpClient<ApiClient>((services, client) =>
        {
            var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MobileAppOptions>>().Value;
            client.BaseAddress = new Uri(options.ApiBaseUrl);
        }).AddHttpMessageHandler<ApiAuthHandler>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<DashboardPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
