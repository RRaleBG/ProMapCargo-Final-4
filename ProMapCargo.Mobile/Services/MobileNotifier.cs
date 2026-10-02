using Microsoft.Maui.Controls.Shapes;

namespace ProMapCargo.Mobile.Services;

public sealed class MobileNotifier : IMobileNotifier
{
    private const uint EnterDurationMs = 220;
    private const uint ExitDurationMs = 180;
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(2.6);

    public Task ShowInfoAsync(string message, CancellationToken cancellationToken = default)
        => ShowAsync(message, Color.FromArgb("#38BDF8"), cancellationToken);

    public Task ShowSuccessAsync(string message, CancellationToken cancellationToken = default)
        => ShowAsync(message, Color.FromArgb("#10B981"), cancellationToken);

    public Task ShowWarningAsync(string message, CancellationToken cancellationToken = default)
        => ShowAsync(message, Color.FromArgb("#FBBF24"), cancellationToken);

    public Task ShowErrorAsync(string message, CancellationToken cancellationToken = default)
        => ShowAsync(message, Color.FromArgb("#FB7185"), cancellationToken);

    private static async Task ShowAsync(string message, Color accentColor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = ResolveActivePage();
            if (page is null)
            {
                return;
            }

            var host = EnsureToastHost(page);

            var toast = BuildToast(message, accentColor);
            toast.Opacity = 0;
            toast.TranslationX = 360;

            host.Children.Add(toast);

            try
            {
                await Task.WhenAll(
                    toast.FadeTo(1, EnterDurationMs, Easing.CubicOut),
                    toast.TranslateTo(0, 0, EnterDurationMs, Easing.CubicOut));

                if (VisibleDuration > TimeSpan.Zero)
                {
                    await Task.Delay(VisibleDuration, cancellationToken);
                }

                await Task.WhenAll(
                    toast.FadeTo(0, ExitDurationMs, Easing.CubicIn),
                    toast.TranslateTo(-24, 0, ExitDurationMs, Easing.CubicIn));
            }
            catch (OperationCanceledException)
            {
                // no-op
            }
            finally
            {
                host.Children.Remove(toast);
            }
        });
    }

    private static Grid BuildToast(string message, Color accentColor)
    {
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#E6061F19"),
            Stroke = accentColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 10),
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new BoxView
                    {
                        WidthRequest = 8,
                        HeightRequest = 8,
                        CornerRadius = 4,
                        Color = accentColor,
                        VerticalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = message,
                        FontSize = 13,
                        TextColor = Colors.White,
                        VerticalTextAlignment = TextAlignment.Center,
                        MaxLines = 3,
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            }
        };

        return new Grid
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(14, 12, 14, 0),
            InputTransparent = true,
            Children = { card }
        };
    }

    private static AbsoluteLayout EnsureToastHost(Page page)
    {
        if (page is not ContentPage contentPage)
        {
            throw new InvalidOperationException("Toast host requires a ContentPage.");
        }

        if (contentPage.Content is not Layout baseLayout)
        {
            var fallbackGrid = new Grid();
            if (contentPage.Content is not null)
            {
                fallbackGrid.Children.Add(contentPage.Content);
            }

            contentPage.Content = fallbackGrid;
            baseLayout = fallbackGrid;
        }

        var host = baseLayout
            .OfType<AbsoluteLayout>()
            .FirstOrDefault(layout => layout.StyleId == "PmToastHost");

        if (host is not null)
        {
            return host;
        }

        host = new AbsoluteLayout
        {
            InputTransparent = true,
            StyleId = "PmToastHost",
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = 9999
        };

        if (baseLayout is Grid grid)
        {
            grid.Children.Add(host);
        }
        else
        {
            var wrapper = new Grid();
            wrapper.Children.Add(baseLayout);
            wrapper.Children.Add(host);
            contentPage.Content = wrapper;
        }

        return host;
    }

    private static Page? ResolveActivePage()
    {
        var window = Application.Current?.Windows.FirstOrDefault(static w => w.Page is not null);
        if (window?.Page is not Page root)
        {
            return null;
        }

        if (root is Shell shell)
        {
            return shell.CurrentPage;
        }

        return root;
    }
}
