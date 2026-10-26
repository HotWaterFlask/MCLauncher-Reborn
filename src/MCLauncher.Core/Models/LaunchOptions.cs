namespace MCLauncher.Core.Models;

/// <summary>游戏窗口类型。</summary>
public enum GameWindowMode
{
    /// <summary>窗口化，使用 <see cref="LaunchOptions.Width"/> / <see cref="LaunchOptions.Height"/>。</summary>
    Windowed = 0,

    /// <summary>全屏（写入 options.txt 的 fullscreen 开关）。</summary>
    Fullscreen = 1,

    /// <summary>最大化：以主显示器分辨率窗口化启动。</summary>
    Maximized = 2,
}

/// <summary>快速游玩模式（1.20 及以上由游戏原生支持）。</summary>
public enum QuickPlayMode
{
    /// <summary>正常进入游戏主菜单。</summary>
    None = 0,

    /// <summary>直接进入多人服务器。</summary>
    Multiplayer = 1,

    /// <summary>直接进入单人存档。</summary>
    Singleplayer = 2,

    /// <summary>直接进入 Realms 服务器。</summary>
    Realms = 3,
}

/// <summary>游戏进程优先级；映射到 Windows 的进程优先级类。</summary>
public enum GameProcessPriority
{
    Low = 0,
    BelowNormal = 1,
    Normal = 2,
    AboveNormal = 3,
    High = 4,
    RealTime = 5,
}

/// <summary>一次启动的可调参数。</summary>
public sealed class LaunchOptions
{
    /// <summary>最大堆内存（MB）。</summary>
    public int MaxMemoryMb { get; init; } = 2048;

    /// <summary>初始堆内存（MB）。</summary>
    public int MinMemoryMb { get; init; } = 512;

    /// <summary>游戏窗口宽（0 表示用版本自带默认值）。</summary>
    public int Width { get; init; }

    /// <summary>游戏窗口高（0 表示用版本自带默认值）。</summary>
    public int Height { get; init; }

    /// <summary>窗口类型：窗口化 / 全屏 / 最大化。</summary>
    public GameWindowMode WindowMode { get; init; } = GameWindowMode.Windowed;

    /// <summary>附加 JVM 参数（空格分隔，支持 ${...} 变量）。</summary>
    public string? ExtraJvmArgs { get; init; }

    /// <summary>附加游戏参数（空格分隔，支持 ${...} 变量）。</summary>
    public string? ExtraGameArgs { get; init; }

    /// <summary>游戏进程工作目录；为空时使用实例目录。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>传给游戏进程的额外环境变量。</summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>游戏进程优先级。</summary>
    public GameProcessPriority ProcessPriority { get; init; } = GameProcessPriority.Normal;

    /// <summary>放在 java 命令前面的包装命令（可执行文件 + 参数）。</summary>
    public string? WrapperCommand { get; init; }

    /// <summary>快速游玩模式。</summary>
    public QuickPlayMode QuickPlayMode { get; init; } = QuickPlayMode.None;

    /// <summary>快速游玩：单人存档的文件夹名（saves 下的目录名）。</summary>
    public string? QuickPlayWorld { get; init; }

    /// <summary>快速游玩：Realms 服务器 ID。</summary>
    public string? QuickPlayRealmsId { get; init; }

    /// <summary>启动后自动进入的服务器地址。</summary>
    public string? ServerIp { get; init; }

    public int ServerPort { get; init; } = 25565;

    /// <summary>实例对应的 Minecraft 版本号，用于决定直连参数（1.20+ 用 Quick Play）。</summary>
    public string? GameVersion { get; init; }

    public string LauncherName { get; init; } = "MCLauncher-Reborn";

    public string LauncherVersion { get; init; } = "6.0";

    /// <summary>是否在启动前补全缺失文件（"启动即安装"）。</summary>
    public bool CompleteBeforeLaunch { get; init; } = true;

    public bool HasCustomResolution => Width > 0 && Height > 0;
}
