namespace MCLauncher.App;

/// <summary>
/// 把工作线程上的回调安全地丢回 UI 线程。
/// Core 层完全不认识 Dispatcher，所有 marshal 都集中在这里做。
/// </summary>
public static class UiThread
{
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // 用 BeginInvoke 而不是 Invoke：调用方多半是下载/启动的后台线程，
        // 不能让它等 UI 线程（否则容易出现互相等待）。
        dispatcher.BeginInvoke(action);
    }
}
