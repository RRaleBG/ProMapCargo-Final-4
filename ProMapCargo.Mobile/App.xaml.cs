using Microsoft.Maui.ApplicationModel;
using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

public partial class App : Application
{
	private readonly OfflineMapService offlineMapService;
	private readonly AppShell appShell;
	private bool initialNavigationApplied;

	public App(OfflineMapService offlineMapService, AppShell appShell)
	{
		InitializeComponent();
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

			await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//dashboard"));
		}
		catch
		{
			await MainThread.InvokeOnMainThreadAsync(() => appShell.GoToAsync("//install-maps"));
		}
	}
}
