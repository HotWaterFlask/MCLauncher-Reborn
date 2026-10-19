using System.Text.Json.Serialization;
using MCLauncher.Core.Models;

namespace MCLauncher.Core.Services;

/// <summary>启动器全局设置。序列化到 %LOCALAPPDATA%\MCLauncher-Reborn\settings.json。</summary>
public sealed class AppSettings
{
    // ---- 界面 ----
    /// <summary>窗口宽度（紧凑态默认 720，拉大后进入完整态）。</summary>
    public int WindowWidth { get; set; } = 720;

    /// <summary>窗口高度（默认 450）。</summary>
    public int WindowHeight { get; set; } = 450;

    /// <summary>上次是否为完整态（拉大过）。</summary>
    public bool FullMode { get; set; }

    // ---- 游戏目录与实例 ----
    public List<string> GameFolders { get; set; } = new();

    public string? SelectedGameFolder { get; set; }

    public string? SelectedInstanceId { get; set; }

    // ---- 下载 ----
    /// <summary>是否优先使用镜像源（BMCLAPI）。</summary>
    public bool PreferMirror { get; set; } = true;

    /// <summary>自定义镜像根地址，为空时用内置默认值。</summary>
    public string? MirrorBase { get; set; }

    // ---- 启动参数 ----
    public int MaxMemoryMb { get; set; } = 2048;

    public int MinMemoryMb { get; set; } = 512;

    public string? ExtraJvmArgs { get; set; }

    public string? ExtraGameArgs { get; set; }

    public int DefaultWidth { get; set; }

    public int DefaultHeight { get; set; }

    /// <summary>默认是否对实例启用版本隔离。</summary>
    public bool DefaultIsolation { get; set; } = true;

    // ---- Java ----
    /// <summary>手动指定的 Java 可执行文件路径；为空时自动探测。</summary>
    public string? JavaPath { get; set; }

    /// <summary>是否允许自动挑选 Java。</summary>
    public bool AutoSelectJava { get; set; } = true;

    /// <summary>最近一次探测到的 Java 列表缓存（路径 + 版本串）。</summary>
    public List<DetectedJava> DetectedJavas { get; set; } = new();

    // ---- 账号 ----
    public List<Account> Accounts { get; set; } = new();

    /// <summary>当前选中的账号 UUID。</summary>
    public string? SelectedAccountUuid { get; set; }

    // ---- 凭据（均不由仓库分发，缺失时界面引导）----
    /// <summary>微软登录用 Azure 应用客户端 ID。</summary>
    public string? MsClientId { get; set; }

    /// <summary>
    /// 微软登录的回调地址；留空时自动使用 <c>http://localhost:&lt;随机端口&gt;</c>。
    /// 若在 Azure 里注册的是带端口的具体地址（如 <c>http://localhost:39123</c>），把那个地址填到这里，
    /// 启动器就会用同样的端口与地址回调。
    /// </summary>
    public string? MsRedirectUri { get; set; }

    /// <summary>CurseForge API Key。</summary>
    public string? CurseForgeApiKey { get; set; }

    [JsonIgnore]
    public bool HasMsClientId => !string.IsNullOrWhiteSpace(MsClientId);
}
