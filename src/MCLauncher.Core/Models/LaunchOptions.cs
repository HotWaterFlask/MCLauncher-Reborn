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

    /// <summary>启动后自动进入的服务器地址。</summary>
    public string? ServerIp { get; init; }

    public int ServerPort { get; init; } = 25565;

    public string LauncherName { get; init; } = "MCLauncher-Reborn";

    public string LauncherVersion { get; init; } = "6.0";

    /// <summary>是否在启动前补全缺失文件（"启动即安装"）。</summary>
    public bool CompleteBeforeLaunch { get; init; } = true;

    public bool HasCustomResolution => Width > 0 && Height > 0;
}
