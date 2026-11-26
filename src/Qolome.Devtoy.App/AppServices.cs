using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Services;

namespace Qolome_Devtoy_App;

public static class AppServices
{
    public static DevtoySession Session { get; } = new();

    public static IconThemeService IconTheme { get; } = new(Session);

    public static AppThemeService AppTheme { get; } = new(Session);

    public static UiPerformanceService Performance { get; } = new(Session);

    public static UiBusyService Busy { get; } = new();
}