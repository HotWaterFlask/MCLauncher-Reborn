using System.Text.Json.Serialization;
using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Models;

/// <summary><c>libraries[].downloads</c>。</summary>
public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public DownloadInfo? Artifact { get; set; }

    /// <summary>键为分类器名（<c>natives-windows</c>、<c>natives-windows-64</c> …）。</summary>
    [JsonPropertyName("classifiers")]
    public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

/// <summary><c>libraries[].extract</c>，natives 解压时要排除的路径前缀。</summary>
public sealed class ExtractRule
{
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }
}

/// <summary>
/// 一个 Maven 依赖。<c>name</c> 形如 <c>group:artifact:version[:classifier]</c>。
/// </summary>
public sealed class Library
{
    /// <summary>natives 描述符的可能拼法，按此顺序探测（"windows" → "natives-windows" → "natives-windows-64" …）。</summary>
    private static readonly string[] NativeDescriptors = BuildNativeDescriptors();

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Maven 仓库根地址；缺失时用默认的 <c>https://libraries.minecraft.net</c>。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    /// <summary>旧版 natives 标记：<c>{ "windows": "natives-windows-${arch}" }</c>。</summary>
    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("extract")]
    public ExtractRule? Extract { get; set; }

    [JsonPropertyName("rules")]
    public List<CompatibilityRule>? Rules { get; set; }

    /// <summary>默认的库仓库地址（原版库都在这里）。</summary>
    public const string DefaultRepository = "https://libraries.minecraft.net/";

    /// <summary>规则是否允许本库在当前环境生效。</summary>
    public bool Applies(IReadOnlyDictionary<string, bool>? features = null) =>
        RuleEvaluator.AppliesToCurrentEnvironment(Rules, features);

    /// <summary>是否是 natives（带平台分类器的库）。</summary>
    public bool IsNativeLibrary =>
        Natives is { Count: > 0 } || Downloads?.Classifiers is { Count: > 0 };

    /// <summary>
    /// 当前平台对应的 natives 分类器名。
    /// 先按 <c>natives</c> 映射表（旧版），再按 <c>downloads.classifiers</c> 的键（新版）探测。
    /// </summary>
    public string? ResolveNativeClassifier()
    {
        if (Natives is { Count: > 0 })
        {
            foreach (var descriptor in NativeDescriptors)
            {
                if (Natives.TryGetValue(descriptor, out var classifier) && !string.IsNullOrWhiteSpace(classifier))
                {
                    return classifier.Replace("${arch}", GameEnvironment.ArchitectureBits);
                }
            }
        }

        if (Downloads?.Classifiers is { Count: > 0 } classifiers)
        {
            foreach (var descriptor in NativeDescriptors)
            {
                if (classifiers.ContainsKey(descriptor))
                {
                    return descriptor;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maven 坐标 → 相对路径。
    /// <c>org.ow2.asm:asm:9.6</c> → <c>org/ow2/asm/asm/9.6/asm-9.6.jar</c>。
    /// </summary>
    public string? ArtifactPath(string? classifier = null)
    {
        var parsed = ParseCoordinates(Name);
        if (parsed is null)
        {
            return null;
        }

        var (group, artifact, version, inlineClassifier) = parsed.Value;
        var effectiveClassifier = classifier ?? inlineClassifier;

        var file = $"{artifact}-{version}";
        if (!string.IsNullOrWhiteSpace(effectiveClassifier))
        {
            file += "-" + effectiveClassifier;
        }

        return $"{group.Replace('.', '/')}/{artifact}/{version}/{file}.jar";
    }

    /// <summary>取该项的下载信息（natives 取分类器条目，普通库取 artifact）。</summary>
    public DownloadInfo? GetDownloadInfo(string? classifier = null)
    {
        if (classifier is not null)
        {
            return Downloads?.Classifiers is { } classifiers && classifiers.TryGetValue(classifier, out var info)
                ? info
                : null;
        }

        return Downloads?.Artifact;
    }

    /// <summary>
    /// 结合 <c>downloads</c> 里的 URL 与 <c>url</c> 仓库根，得出最终下载地址。
    /// 两者都没有时用 <paramref name="fallbackRepository"/> 拼出来（老版本 JSON 常缺 downloads）。
    /// </summary>
    public string? ResolveDownloadUrl(string? classifier = null, string fallbackRepository = DefaultRepository)
    {
        var info = GetDownloadInfo(classifier);
        if (info?.Url is { Length: > 0 } explicitUrl)
        {
            return explicitUrl;
        }

        var path = ArtifactPath(classifier);
        if (path is null)
        {
            return null;
        }

        var repository = string.IsNullOrWhiteSpace(Url) ? fallbackRepository : Url!;
        return repository.TrimEnd('/') + "/" + path;
    }

    /// <summary>natives 解压时的排除前缀；未显式声明时默认排除 META-INF（避免覆盖签名文件）。</summary>
    public IReadOnlyList<string> ExtractExclusions =>
        Extract?.Exclude is { Count: > 0 } exclusions ? exclusions : new[] { "META-INF/" };

    /// <summary>合并时的去重键：不区分大小写，且 natives 与普通库分开计。</summary>
    public string DedupKey() =>
        string.IsNullOrWhiteSpace(Name)
            ? $"{Guid.NewGuid():N}"
            : $"{Name}#{(IsNativeLibrary ? "n" : "a")}";

    public override string ToString() => Name ?? "(未命名)";

    private static (string Group, string Artifact, string Version, string? Classifier)? ParseCoordinates(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var coordinate = name!;

        // 极少数条目带 @jar / @zip 扩展名后缀。
        var atIndex = coordinate.IndexOf('@');
        if (atIndex >= 0)
        {
            coordinate = coordinate[..atIndex];
        }

        var parts = coordinate.Split(':');
        if (parts.Length < 3)
        {
            return null;
        }

        return (
            parts[0],
            parts[1],
            parts[2],
            parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : null);
    }

    private static string[] BuildNativeDescriptors()
    {
        // key 顺序必须是「最具体 → 最泛」：先架构名，再位数，最后平台名。
        // 反过来的话，在 arm64 上会先命中 "natives-windows"（x64 二进制），直接把游戏带崩。
        var keys = new[] { GameEnvironment.Architecture, GameEnvironment.ArchitectureBits, string.Empty };
        var variants = new[] { string.Empty, "native", "natives" };

        var result = new List<string>(keys.Length * variants.Length);
        foreach (var key in keys)
        {
            foreach (var variant in variants)
            {
                var name = string.IsNullOrEmpty(variant)
                    ? GameEnvironment.OsName
                    : $"{variant}-{GameEnvironment.OsName}";

                if (!string.IsNullOrEmpty(key))
                {
                    name += "-" + key;
                }

                result.Add(name);
            }
        }

        return result.ToArray();
    }
}
