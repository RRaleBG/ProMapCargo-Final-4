using ProMapCargo.Mobile.Services;
using ProMapCargo.Mobile.ViewModels;

namespace ProMapCargo.Mobile.Views;

public partial class MessagesPage : ContentPage
{
    private readonly MessagesViewModel viewModel;
    private readonly ChatClient chat;

    public MessagesPage(MessagesViewModel viewModel, ChatClient chat)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        this.chat = chat;
        // Newest at the bottom, like any chat.
        viewModel.Updated += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (viewModel.Messages.Count > 0)
            {
                MessageList.ScrollTo(viewModel.Messages.Count - 1, position: ScrollToPosition.End, animate: false);
            }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // While the screen is open a message from dispatch shows here, not as a notification.
        chat.IsChatVisible = true;
        AppLifecycle.Resumed += OnAppResumed;
        SafeUi.Run(() => viewModel.LoadAsync());
    }

    protected override void OnDisappearing()
    {
        chat.IsChatVisible = false;
        AppLifecycle.Resumed -= OnAppResumed;
        base.OnDisappearing();
    }

    // Back in the app on this screen: what came meanwhile shows and counts as read.
    private void OnAppResumed(object? sender, EventArgs e)
        => SafeUi.Run(() => viewModel.LoadAsync());
}
