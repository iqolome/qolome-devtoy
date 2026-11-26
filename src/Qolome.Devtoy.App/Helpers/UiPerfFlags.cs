namespace Qolome_Devtoy_App.Helpers;

/// <summary>供 XAML 辅助类读取的轻量性能开关（由 <c>UiPerformanceService</c> 刷新）。</summary>
public static class UiPerfFlags
{
    public static bool HardwareAcceleration { get; set; } = true;

    public static bool RichTableChrome { get; set; } = true;

    public static bool ParallelCollectors { get; set; } = true;
}
