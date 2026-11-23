using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>自定义标题栏时，按明暗主题设置右上角系统按钮（最小化/最大化/关闭）颜色。</summary>
public static class TitleBarChrome
{
    public static void Apply(Window window)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = window.AppWindow.TitleBar;
        if (IsLightTheme(window))
        {
            ApplyLight(titleBar);
        }
        else
        {
            ApplyDark(titleBar);
        }
    }

    public static void ListenToThemeChanges(Window window, UIElement? themeRoot)
    {
        if (themeRoot is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => Apply(window);
        }

        try
        {
            var settings = new UISettings();
            settings.ColorValuesChanged += (_, _) =>
            {
                window.DispatcherQueue.TryEnqueue(() => Apply(window));
            };
        }
        catch
        {
            // 非桌面环境可忽略
        }
    }

    private static bool IsLightTheme(Window window)
    {
        try
        {
            if (window.Content is FrameworkElement root)
            {
                if (root.ActualTheme == ElementTheme.Light)
                {
                    return true;
                }

                if (root.ActualTheme == ElementTheme.Dark)
                {
                    return false;
                }
            }
        }
        catch (COMException)
        {
            // 窗口已关闭时 Content 不可用
        }

        return Application.Current?.RequestedTheme != ApplicationTheme.Dark;
    }

    private static void ApplyLight(AppWindowTitleBar titleBar)
    {
        var fg = ColorHelper.FromArgb(255, 32, 32, 32);
        var fgInactive = ColorHelper.FromArgb(255, 130, 130, 130);
        titleBar.ButtonForegroundColor = fg;
        titleBar.ButtonHoverForegroundColor = fg;
        titleBar.ButtonPressedForegroundColor = fg;
        titleBar.ButtonInactiveForegroundColor = fgInactive;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(28, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(40, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ForegroundColor = Colors.Transparent;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveForegroundColor = fgInactive;
    }

    private static void ApplyDark(AppWindowTitleBar titleBar)
    {
        var fg = ColorHelper.FromArgb(255, 255, 255, 255);
        var fgInactive = ColorHelper.FromArgb(255, 170, 170, 170);
        titleBar.ButtonForegroundColor = fg;
        titleBar.ButtonHoverForegroundColor = fg;
        titleBar.ButtonPressedForegroundColor = fg;
        titleBar.ButtonInactiveForegroundColor = fgInactive;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(36, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(52, 255, 255, 255);
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ForegroundColor = Colors.Transparent;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveForegroundColor = fgInactive;
    }
}