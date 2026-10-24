using System.Text.Json.Serialization;

namespace MCLauncher.Core.Models;

/// <summary>
/// Forge / NeoForge 安装器里的 <c>install_profile.json</c>。
/// <para>
/// 带 <see cref="Spec"/> 的是处理器式安装器（1.13+），安装器本身只提供数据，
/// 客户端 jar 的补丁、映射表等靠 <see cref="Processors"/> 里的外部程序生成；
/// 没有 <see cref="Spec"/> 但带 <see cref="Install"/> + <see cref="VersionInfo"/> 的是老式安装器（1.12.2 及更早），
/// 只需要把内嵌的 universal jar 解出来。
/// </para>
/// </summary>
public sealed class ForgeInstallProfile
{
    /// <summary>安装器格式版本；有值即表示需要跑处理器。</summary>
    [JsonPropertyName("spec")]
    public int? Spec { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    /// <summary>安装后的版本 id，例如 <c>1.20.1-forge-47.2.0</c>。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>主制品的 Maven 坐标；部分安装器为 null。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>对应的原版版本 id。</summary>
    [JsonPropertyName("minecraft")]
    public string? Minecraft { get; set; }

    /// <summary>版本 JSON 在安装器里的条目名，例如 <c>/version.json</c>。</summary>
    [JsonPropertyName("json")]
    public string? Json { get; set; }

    /// <summary>安装器自带的依赖库，同时也是处理器的 classpath。</summary>
    [JsonPropertyName("libraries")]
    public List<Library>? Libraries { get; set; }

    [JsonPropertyName("processors")]
    public List<ForgeProcessor>? Processors { get; set; }

    /// <summary>处理器变量表；每一项按 <c>client</c> 侧取值。</summary>
    [JsonPropertyName("data")]
    public Dictionary<string, ForgeDataEntry>? Data { get; set; }

    /// <summary>老式安装器才有的安装信息。</summary>
    [JsonPropertyName("install")]
    public ForgeLegacyInstall? Install { get; set; }

    /// <summary>老式安装器内嵌的版本 JSON。</summary>
    [JsonPropertyName("versionInfo")]
    public GameVersionJson? VersionInfo { get; set; }

    /// <summary>
    /// 是否需要执行处理器。1.12.2 的安装器也写着 <c>spec: 0</c>，
    /// 但它的处理器列表为空、只靠内嵌 maven + 版本 JSON，因此以处理器列表为准。
    /// </summary>
    public bool UsesProcessors => Processors is { Count: > 0 };

    /// <summary>该安装器对应的原版版本；老格式从 <see cref="Install"/> 里取。</summary>
    public string? ResolveMinecraftVersion() =>
        Minecraft ?? Install?.Minecraft;
}

/// <summary>installation profile 里的一个外部处理器。</summary>
public sealed class ForgeProcessor
{
    /// <summary>只在哪些侧运行；缺省表示两侧都运行。</summary>
    [JsonPropertyName("sides")]
    public List<string>? Sides { get; set; }

    /// <summary>可执行 jar 的 Maven 坐标。</summary>
    [JsonPropertyName("jar")]
    public string? Jar { get; set; }

    /// <summary>classpath，元素是 Maven 坐标。</summary>
    [JsonPropertyName("classpath")]
    public List<string>? Classpath { get; set; }

    /// <summary>命令行参数，元素可以是字面量、<c>{变量}</c> 或 <c>[Maven 坐标]</c>。</summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    /// <summary>输出文件 → SHA-1；输出齐全且校验通过时整个处理器可以跳过。</summary>
    [JsonPropertyName("outputs")]
    public Dictionary<string, string>? Outputs { get; set; }

    /// <summary>与官方安装器一致：没有 sides 表示两侧都跑，有 sides 时必须包含 client。</summary>
    public bool AppliesToClient =>
        Sides is null || Sides.Exists(side => string.Equals(side, "client", StringComparison.OrdinalIgnoreCase));
}

/// <summary>处理器变量表里的一项。</summary>
public sealed class ForgeDataEntry
{
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}

/// <summary>老式安装器（1.12.2 及更早）的 install 段。</summary>
public sealed class ForgeLegacyInstall
{
    /// <summary>universal jar 落盘时的 Maven 坐标。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>universal jar 在安装器 jar 里的条目名。</summary>
    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    [JsonPropertyName("minecraft")]
    public string? Minecraft { get; set; }
}
