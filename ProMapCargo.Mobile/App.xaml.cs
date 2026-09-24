using Microsoft.Maui.ApplicationModel;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

public partial class App : Application
{
	private readonly MobileSessionService mobileSessionService;
	private readonly OfflineMapService offlineMapService;
	private readonly AppShell appShell;
	private bool initialNavigationApplied;

	public App(MobileSessionService mobileSessionService, OfflineMapService offlineMapService, AppShell appShell)
	{
		InitializeComponent();
		this.mobileSessionService = mobileSessionService;
		this.offlineMapService = offlineMapService;
		this.appShell = appShell;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(appShell);
		window.Created += HandleWindowCreated;
		return window;
	}

	private async void HandleWindowCreated(object? sender, EventArgs e)
	{
		if (initialNavigationApplied)
		{
			return;
		}

		initialNavigationApplied = true;

		try
		{
			var hasInstalledMaps = await offlineMapService.HasInstalledMapsAsync(CancellationToken.None).ConfigureAwait(false);
			if (!hasInstalledMaps)
			{
				await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//install-maps"));
				return;
			}

			var hasSession = await mobileSessionService.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
			await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync(hasSession ? "//dashboard" : "//login"));
		}
		catch
		{
			await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//install-maps"));
		}
	}
}
