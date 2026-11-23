using Microsoft.UI.Xaml;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;
using Windows.UI.ViewManagement;

namespace Qolome_Devtoy_App.Services;

/// <summary>应用 ElementTheme：跟随系统 / 浅色 / 深色。</summary>
public sealed class AppThemeService
{
    private readonly DevtoySession _session;
    private Window? _window;
    private UISettings? _uiSettings;
    private bool _listeningSystem;

    public AppThemeService(DevtoySession session) => _session = session;

    public void Attach(Window window)
    {
        _window = window;
        EnsureSystemListener();
        ApplyFromSettings();
    }

    public void ApplyFromSettings() => Apply(_session.Settings.AppTheme);

    public void Apply(string? themeId)
    {
        var mode = AppThemeMode.Resolve(themeId);
        var elementTheme = mode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (_window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = elementTheme;
        }

        if (_window is not null)
        {
            TitleBarChrome.Apply(_window);
        }
    }

    /// <summary>当前是否为深色外观（固定深色，或跟随系统且系统为深色）。</summary>
    public bool IsEffectiveDark(string? themeId = null)
    {
        var mode = AppThemeMode.Resolve(themeId ?? _session.Settings.AppTheme);
        return mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsOsDark(),
        };
    }

    public static bool IsOsDark()
    {
        try
        {
            var settings = new UISettings();
            // 前景偏亮 → 系统处于深色模式
            var fg = settings.GetColorValue(UIColorType.Foreground);
            return (fg.R * 0.299 + fg.G * 0.587 + fg.B * 0.114) > 140;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureSystemListener()
    {
        if (_listeningSystem)
        {
            return;
        }

        try
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += (_, _) =>
            {
                if (!string.Equals(
                        AppThemeMode.Resolve(_session.Settings.AppTheme),
                        AppThemeMode.System,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _window?.DispatcherQueue.TryEnqueue(() =>
                {
                    Apply(AppThemeMode.System);
                    AppServices.IconTheme.SyncWithAppTheme(coerceUserVariant: false, log: false);
                });
            };
            _listeningSystem = true;
        }
        catch
        {
            // 非桌面环境可忽略
        }
    }
}
