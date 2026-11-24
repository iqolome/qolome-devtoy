using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Qolome.Nets.App;

/// <summary>
/// 嵌入 Devtoy 后替代原 nets <c>App.xaml.cs</c> 的窗口/调度入口。
/// </summary>
public static class App
{
    public static Window? Window => Qolome_Devtoy_App.App.TryGetWindow();

    public static nint WindowHandle =>
        Window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public static DispatcherQueue? DispatcherQueue => Window?.DispatcherQueue;
}
