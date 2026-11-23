using Microsoft.UI.Dispatching;

namespace Qolome_Devtoy_App.Services;

/// <summary>内容区 Busy 遮罩（仅覆盖当前页，侧栏/控制台不受影响）。</summary>
public sealed class UiBusyService
{
    private readonly object _gate = new();
    private int _depth;
    private string _message = "处理中…";
    private Action<bool, string>? _presenter;
    private DispatcherQueue? _dispatcher;

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _depth > 0;
            }
        }
    }

    public void Attach(DispatcherQueue dispatcher, Action<bool, string> presenter)
    {
        _dispatcher = dispatcher;
        _presenter = presenter;
        PushUi(false, _message);
    }

    public void Detach()
    {
        _presenter = null;
        _dispatcher = null;
    }

    public Task RunAsync(string message, Func<Task> action) =>
        RunAsync(message, async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        });

    public async Task<T> RunAsync<T>(string message, Func<Task<T>> action)
    {
        Enter(message);
        try
        {
            return await action().ConfigureAwait(true);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>在后台线程执行同步工作，并显示遮罩。</summary>
    public Task RunBackgroundAsync(string message, Action action) =>
        RunAsync(message, () => Task.Run(action));

    public Task<T> RunBackgroundAsync<T>(string message, Func<T> action) =>
        RunAsync(message, () => Task.Run(action));

    public void Enter(string message)
    {
        lock (_gate)
        {
            _depth++;
            if (!string.IsNullOrWhiteSpace(message))
            {
                _message = message.Trim();
            }

            // 首次进入与嵌套进入都刷新遮罩文案
            PushUi(true, _message);
        }
    }

    public void Exit()
    {
        lock (_gate)
        {
            if (_depth <= 0)
            {
                _depth = 0;
                PushUi(false, _message);
                return;
            }

            _depth--;
            if (_depth == 0)
            {
                PushUi(false, _message);
            }
        }
    }

    private void PushUi(bool visible, string message)
    {
        var presenter = _presenter;
        var dispatcher = _dispatcher;
        if (presenter is null || dispatcher is null)
        {
            return;
        }

        void Apply() => presenter(visible, message);

        if (dispatcher.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, Apply);
        }
    }
}
