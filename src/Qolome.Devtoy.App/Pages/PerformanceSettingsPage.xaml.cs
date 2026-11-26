using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Services;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class PerformanceSettingsPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;
    private bool _suppressPerfChange;

    public PerformanceSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Bind();
    }

    private void Bind()
    {
        _suppressPerfChange = true;
        var s = _session.Settings;
        HardwareAccelSwitch.IsOn = s.HardwareAcceleration;
        ParallelCollectorsSwitch.IsOn = s.ParallelCollectors;
        RichTableSwitch.IsOn = s.RichTableChrome;
        RichTableSwitch.IsEnabled = s.HardwareAcceleration;
        _suppressPerfChange = false;
    }

    private void OnHardwareAccelToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPerfChange)
        {
            return;
        }

        AppServices.Performance.SetHardwareAcceleration(HardwareAccelSwitch.IsOn);
        RichTableSwitch.IsEnabled = HardwareAccelSwitch.IsOn;
        _session.Log(HardwareAccelSwitch.IsOn ? "已开启硬件加速" : "已关闭硬件加速（降级表格富视觉）");
    }

    private void OnParallelCollectorsToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPerfChange)
        {
            return;
        }

        AppServices.Performance.SetParallelCollectors(ParallelCollectorsSwitch.IsOn);
        _session.Log(ParallelCollectorsSwitch.IsOn ? "已开启多核并行采集" : "已关闭多核并行采集");
    }

    private void OnRichTableToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPerfChange)
        {
            return;
        }

        AppServices.Performance.SetRichTableChrome(RichTableSwitch.IsOn);
        _session.Log(RichTableSwitch.IsOn ? "已开启表格富视觉" : "已关闭表格富视觉");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _session.Settings.HardwareAcceleration = HardwareAccelSwitch.IsOn;
        _session.Settings.ParallelCollectors = ParallelCollectorsSwitch.IsOn;
        _session.Settings.RichTableChrome = RichTableSwitch.IsOn;
        _session.SaveSettings();
        AppServices.Performance.ApplyFromSettings();
        Bind();
        _session.Log("性能设置已保存");
    }
}
