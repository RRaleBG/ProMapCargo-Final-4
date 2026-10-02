namespace ProMapCargo.Mobile;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	private async void OnOpenOperationsClicked(object? sender, EventArgs e)
	{
		var shell = Shell.Current;
		if (shell is null)
		{
			return;
		}

		await shell.GoToAsync("//dashboard");
	}

	private async void OnOpenNavigationClicked(object? sender, EventArgs e)
	{
		var shell = Shell.Current;
		if (shell is null)
		{
			return;
		}

		await shell.GoToAsync("//dashboard/mobile-navigation");
	}
}
