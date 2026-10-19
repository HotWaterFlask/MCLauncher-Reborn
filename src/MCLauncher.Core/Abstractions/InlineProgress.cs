namespace MCLauncher.Core.Abstractions;

/// <summary>
/// 同步回调的 <see cref="IProgress{T}"/> 实现。
/// Core 层完全不知道 UI 线程的存在，所以刻意不用 <c>Progress&lt;T&gt;</c>（会捕获 SynchronizationContext），
/// 回调直接在工作线程上执行，由上层自行决定是否 marshal 到 UI 线程。
/// </summary>
public sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public void Report(T value)
    {
        try
        {
            _handler(value);
        }
        catch
        {
            // 进度回调不能把工作线程带崩。
        }
    }
}
