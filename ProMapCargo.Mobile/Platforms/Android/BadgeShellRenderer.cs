using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace ProMapCargo.Mobile;

/// <summary>
/// Shell renderer that hands the bottom tab bar to <see cref="TabBadges"/>, so a tab
/// (Poruke) can show a number badge. MAUI Shell has no badge of its own.
/// </summary>
public sealed class BadgeShellRenderer : ShellRenderer
{
    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem)
        => new BadgeAppearanceTracker(this, shellItem);

    private sealed class BadgeAppearanceTracker(IShellContext context, ShellItem item)
        : ShellBottomNavViewAppearanceTracker(context, item)
    {
        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        {
            base.SetAppearance(bottomView, appearance);
            TabBadges.Attach(bottomView);
        }

        public override void ResetAppearance(BottomNavigationView bottomView)
        {
            base.ResetAppearance(bottomView);
            TabBadges.Attach(bottomView);
        }
    }
}

/// <summary>Number badges on the bottom tabs (index as in AppShell.xaml).</summary>
public static class TabBadges
{
    private static readonly Dictionary<int, int> Counts = new();
    private static WeakReference<BottomNavigationView>? view;

    public static void Attach(BottomNavigationView bottomView)
    {
        view = new WeakReference<BottomNavigationView>(bottomView);
        foreach (var (index, count) in Counts)
        {
            Apply(bottomView, index, count);
        }
    }

    public static void Set(int tabIndex, int count)
    {
        Counts[tabIndex] = count;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (view?.TryGetTarget(out var bottomView) == true)
            {
                Apply(bottomView, tabIndex, count);
            }
        });
    }

    private static void Apply(BottomNavigationView bottomView, int tabIndex, int count)
    {
        try
        {
            if (bottomView.Menu is not { } menu || tabIndex >= menu.Size())
            {
                return;
            }

            var itemId = menu.GetItem(tabIndex)!.ItemId;
            if (count <= 0)
            {
                bottomView.RemoveBadge(itemId);
                return;
            }

            var badge = bottomView.GetOrCreateBadge(itemId);
            badge.Number = count;
            badge.MaxCharacterCount = 3;
            badge.BackgroundColor = unchecked((int)0xFFF43F5E);
            badge.BadgeTextColor = unchecked((int)0xFFFFFFFF);
            badge.SetVisible(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tab badge not shown: {ex.Message}");
        }
    }
}
