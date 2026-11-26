using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;

namespace Qolome_Devtoy_App.Services;

/// <summary>
/// 界面性能选项：硬件合成、并行采集、表格富视觉。
/// WinUI 本身走 Composition/GPU；关闭「硬件加速」时降级附加可视层以减轻驱动/合成压力。
/// </summary>
public sealed class UiPerformanceService
{
    private readonly DevtoySession _session;
    private Window? _window;

    public UiPerformanceService(DevtoySession session) => _session = session;

    public bool HardwareAcceleration => _session.Settings.HardwareAcceleration;

    public bool ParallelCollectors => _session.Settings.ParallelCollectors;

    public bool RichTableChrome =>
        _session.Settings.RichTableChrome && _session.Settings.HardwareAcceleration;

    public event Action? OptionsChanged;

    public void Attach(Window window)
    {
        _window = window;
        Apply();
    }

    public void ApplyFromSettings() => Apply();

    public void Apply()
    {
        var s = _session.Settings;
        UiPerfFlags.HardwareAcceleration = s.HardwareAcceleration;
        UiPerfFlags.ParallelCollectors = s.ParallelCollectors;
        UiPerfFlags.RichTableChrome = s.RichTableChrome && s.HardwareAcceleration;

        if (_window?.Content is FrameworkElement root)
        {
            ApplyCompositionHint(root, s.HardwareAcceleration);
        }

        OptionsChanged?.Invoke();
    }

    public void SetHardwareAcceleration(bool enabled)
    {
        _session.Settings.HardwareAcceleration = enabled;
        PersistAndApply();
    }

    public void SetParallelCollectors(bool enabled)
    {
        _session.Settings.ParallelCollectors = enabled;
        _session.SaveSettings();
        UiPerfFlags.ParallelCollectors = enabled;
        OptionsChanged?.Invoke();
    }

    public void SetRichTableChrome(bool enabled)
    {
        _session.Settings.RichTableChrome = enabled;
        PersistAndApply();
    }

    private void PersistAndApply()
    {
        _session.SaveSettings();
        Apply();
    }

    /// <summary>
    /// 通过 Composition 提示 GPU 合成路径；关闭时去掉平移合成提示以降低驱动负担。
    /// </summary>
    private static void ApplyCompositionHint(FrameworkElement root, bool hardwareAcceleration)
    {
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(root, hardwareAcceleration);
        }
        catch
        {
            // Composition 不可用时忽略（如测试宿主）
        }
    }
}
