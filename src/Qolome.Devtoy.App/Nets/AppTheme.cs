using Microsoft.UI;
using Microsoft.UI.Xaml;
using Qolome.Nets.Core.Data;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Qolome.Nets.App;

public static class AppTheme
{
    private static UISettings? _uiSettings;

    public static void Apply() => Apply(AppConfigStore.Current.Appearance.Theme);

    public static void Apply(string? theme)
    {
        EnsureSystemWatch();
        if (App.Window?.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = AppThemes.Normalize(theme) switch
        {
            AppThemes.Light => ElementTheme.Light,
            AppThemes.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        UpdateTitleBar(ResolveActual(root));
    }

    private static void EnsureSystemWatch()
    {
        if (_uiSettings is not null)
        {
            return;
        }

        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            App.DispatcherQueue?.TryEnqueue(() =>
            {
                if (AppThemes.Normalize(AppConfigStore.Current.Appearance.Theme) == AppThemes.System)
                {
                    Apply(AppThemes.System);
                }
            });
        };
    }

    private static ElementTheme ResolveActual(FrameworkElement root)
    {
        if (root.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
        {
            return root.ActualTheme;
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;
    }

    private static void UpdateTitleBar(ElementTheme theme)
    {
        var titleBar = App.Window?.AppWindow?.TitleBar;
        if (titleBar is null)
        {
            return;
        }

        var dark = theme == ElementTheme.Dark;
        var fg = dark ? Colors.White : Colors.Black;
        var inactive = dark ? Color.FromArgb(255, 158, 158, 158) : Color.FromArgb(255, 96, 96, 96);
        var hover = dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(32, 0, 0, 0);
        var pressed = dark ? Color.FromArgb(72, 255, 255, 255) : Color.FromArgb(48, 0, 0, 0);

        titleBar.ButtonForegroundColor = fg;
        titleBar.ButtonHoverForegroundColor = fg;
        titleBar.ButtonPressedForegroundColor = fg;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }
}
