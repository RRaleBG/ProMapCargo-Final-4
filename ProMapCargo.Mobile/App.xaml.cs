using ProMapCargo.Mobile.Services;

namespace ProMapCargo.Mobile;

public partial class App : Application
{
	private readonly MobileSessionService mobileSessionService;
	private readonly AppShell appShell;

	public App(MobileSessionService mobileSessionService, AppShell appShell)
	{
		InitializeComponent();
		this.mobileSessionService = mobileSessionService;
		this.appShell = appShell;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(appShell);
	}

	protected override async void OnStart()
	{
		base.OnStart();
		var hasSession = await mobileSessionService.RestoreAsync(CancellationToken.None);
        //await Shell.Current.GoToAsync(hasSession ? "//dashboard" : "//login");
        await Shell.Current.GoToAsync(hasSession ? "//dashboard" : "//navigation");
    }
}
