using System.Runtime.InteropServices;

namespace MCLauncher.Core.Utils;

/// <summary>
/// 当前运行环境在 Minecraft 协议里的表示。
/// 版本 JSON 的 <c>rules</c> 用 <c>os.name</c> / <c>os.arch</c> 判定，
/// 取值固定为 <c>windows|osx|linux</c> 与 <c>x86|x86_64|arm64</c>，
/// 而 .NET 给出的名字是 <c>amd64|aarch64|…</c>，必须在这里做一次映射。
/// </summary>
public static class GameEnvironment
{
    public static bool IsWindows { get; } = OperatingSystem.IsWindows();

    public static bool IsMacOs { get; } = OperatingSystem.IsMacOS();

    public static bool IsLinux { get; } = OperatingSystem.IsLinux();

    /// <summary>协议中的 <c>os.name</c>：windows / osx / linux。</summary>
    public static string OsName { get; } = IsWindows ? "windows" : IsMacOs ? "osx" : "linux";

    /// <summary>协议中的 <c>os.arch</c>：x86 / x86_64 / arm64。</summary>
    public static string Architecture { get; } = MapArchitecture(RuntimeInformation.ProcessArchitecture);

    /// <summary>32 / 64：旧版本 natives 的 <c>${arch}</c> 替换值。</summary>
    public static string ArchitectureBits { get; } = Architecture switch
    {
        "x86" => "32",
        _ => "64",
    };

    public static string OsVersion { get; } =
        Environment.OSVersion.Version.ToString(3);

    /// <summary>进程位数（32 位进程不能正常加载现代版本的 natives）。</summary>
    public static bool Is64BitProcess { get; } = Environment.Is64BitProcess;

    /// <summary>把 .NET 的架构枚举映射为协议取值。注意此处必须写全限定名：类内已有同名属性 Architecture。</summary>
    public static string MapArchitecture(System.Runtime.InteropServices.Architecture architecture) => architecture switch
    {
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.X64 => "x86_64",
        System.Runtime.InteropServices.Architecture.Arm => "arm32",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        _ => "x86_64",
    };
}
