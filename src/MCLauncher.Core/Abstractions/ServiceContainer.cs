namespace MCLauncher.Core.Abstractions;

/// <summary>
/// 极简服务容器。刻意不引入第三方 DI 容器：注册是显式的、解析是显式的，
/// 出问题时能一眼看出依赖从哪来。
/// </summary>
public sealed class ServiceContainer
{
    private readonly Dictionary<Type, object> _singletons = new();
    private readonly object _gate = new();

    /// <summary>注册一个已经构造好的单例。</summary>
    public void RegisterSingleton<T>(T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        lock (_gate)
        {
            _singletons[typeof(T)] = instance;
        }
    }

    /// <summary>注册一个延迟构造的单例工厂（首次解析时才创建）。</summary>
    public void RegisterSingleton<T>(Func<ServiceContainer, T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_gate)
        {
            _singletons[typeof(T)] = new Lazy<T>(() => factory(this));
        }
    }

    /// <summary>解析服务，未注册时抛出带类型名的异常。</summary>
    public T Resolve<T>() where T : class
    {
        if (TryResolve<T>(out var service))
        {
            return service!;
        }

        throw new InvalidOperationException($"服务未注册：{typeof(T).FullName}");
    }

    /// <summary>尝试解析服务。</summary>
    public bool TryResolve<T>(out T? service) where T : class
    {
        object? entry;
        lock (_gate)
        {
            if (!_singletons.TryGetValue(typeof(T), out entry))
            {
                service = null;
                return false;
            }
        }

        service = entry is Lazy<T> lazy ? lazy.Value : (T)entry;
        return true;
    }
}
