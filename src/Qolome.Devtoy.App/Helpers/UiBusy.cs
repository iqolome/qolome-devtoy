using Qolome_Devtoy_App.Services;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>页面侧调用全局遮罩的便捷入口。</summary>
public static class UiBusy
{
    private static UiBusyService Service => AppServices.Busy;

    public static Task RunAsync(string message, Func<Task> action) =>
        Service.RunAsync(message, action);

    public static Task<T> RunAsync<T>(string message, Func<Task<T>> action) =>
        Service.RunAsync(message, action);

    public static Task RunBackgroundAsync(string message, Action action) =>
        Service.RunBackgroundAsync(message, action);

    public static Task<T> RunBackgroundAsync<T>(string message, Func<T> action) =>
        Service.RunBackgroundAsync(message, action);
}
