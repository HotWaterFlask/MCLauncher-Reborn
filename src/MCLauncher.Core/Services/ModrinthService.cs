using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Services;

/// <summary>
/// Modrinth 检索与文件挑选。
/// <para>
/// 使用 v2 接口，无需 API Key；请求必须带可识别的 User-Agent。
/// 搜索用 facets 过滤项目类型、游戏版本与加载器，版本接口按加载器筛选后
/// 由 <see cref="PickBestFile"/> 选出正式版优先的最新文件。
/// </para>
/// </summary>
public sealed class ModrinthService
{
    private const string BaseUrl = "https://api.modrinth.com/v2";

    private static readonly HashSet<string> KnownLoaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "fabric", "forge", "neoforge", "quilt", "liteloader", "rift",
    };

    private readonly HttpDownloader _downloader;
    private readonly DownloadSourceResolver _resolver;
    private readonly ILogger _log;

    public ModrinthService(HttpDownloader downloader, DownloadSourceResolver resolver, ILogger log)
    {
        _downloader = downloader;
        _resolver = resolver;
        _log = log;
    }

    /// <summary>Modrinth 是否提供该类资源（世界存档只有 CurseForge）。</summary>
    public static bool SupportsDownload(ResourceKind kind) =>
        kind is ResourceKind.Mod or ResourceKind.ResourcePack or ResourceKind.ShaderPack
            or ResourceKind.Modpack;

    /// <summary>资源类别对应的 Modrinth project_type。</summary>
    public static string? ProjectTypeFor(ResourceKind kind) => kind switch
    {
        ResourceKind.Mod => "mod",
        ResourceKind.ResourcePack => "resourcepack",
        ResourceKind.ShaderPack => "shader",
        ResourceKind.Modpack => "modpack",
        _ => null,
    };

    /// <summary>把实例的加载器名转成 Modrinth 的 loader 值。</summary>
    public static string? NormalizeLoader(string? loaderName) => loaderName?.Trim().ToLowerInvariant() switch
    {
        "forge" => "forge",
        "neoforge" => "neoforge",
        "fabric" => "fabric",
        "quilt" => "quilt",
        _ => null,
    };

    /// <summary>按名称检索项目。</summary>
    public async Task<ModrinthSearchPage> SearchAsync(
        ResourceKind kind,
        string? search,
        string? gameVersion,
        string sortIndex,
        string? loaderName,
        int pageIndex,
        int pageSize,
        CancellationToken ct)
    {
        var projectType = ProjectTypeFor(kind)
                          ?? throw new InvalidOperationException(
                              $"{ResourceRepository.DisplayName(kind)}不支持从 Modrinth 下载。");

        var facets = new List<string[]> { new[] { "project_type:" + projectType } };

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            facets.Add(new[] { "versions:" + gameVersion.Trim() });
        }

        var loader = kind == ResourceKind.Mod ? NormalizeLoader(loaderName) : null;
        if (loader is not null)
        {
            facets.Add(new[] { "categories:" + loader });
        }

        var parameters = new List<string>
        {
            "query=" + Uri.EscapeDataString(search?.Trim() ?? string.Empty),
            "limit=" + pageSize,
            "offset=" + Math.Max(pageIndex, 0) * pageSize,
            "index=" + Uri.EscapeDataString(sortIndex),
            "facets=" + Uri.EscapeDataString(JsonSerializer.Serialize(facets)),
        };

        var json = await SendAsync($"{BaseUrl}/search?{string.Join("&", parameters)}", ct)
            .ConfigureAwait(false);
        var response = JsonUtil.Deserialize<SearchResponse>(json);
        var projects = response?.Hits is null
            ? new List<ModrinthProject>()
            : response.Hits.Select(ToProject).ToList();

        return new ModrinthSearchPage
        {
            Projects = projects,
            TotalCount = response?.TotalHits ?? projects.Count,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
    }

    /// <summary>获取项目在某个游戏版本 / 加载器下的全部版本（按发布时间倒序）。</summary>
    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        ResourceKind kind,
        string? gameVersion,
        string? loaderName,
        CancellationToken ct)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            parameters.Add("game_versions=" + Uri.EscapeDataString(
                JsonSerializer.Serialize(new[] { gameVersion.Trim() })));
        }

        var loader = kind == ResourceKind.Mod ? NormalizeLoader(loaderName) : null;
        if (loader is not null)
        {
            parameters.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader })));
        }

        var url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectId)}/version";
        if (parameters.Count > 0)
        {
            url += "?" + string.Join("&", parameters);
        }

        var json = await SendAsync(url, ct).ConfigureAwait(false);
        var response = JsonUtil.Deserialize<List<VersionDto>>(json);

        return response is null
            ? Array.Empty<ModrinthVersion>()
            : response
                .Select(ToVersion)
                .OrderByDescending(version => version.PublishedAt)
                .ToList();
    }

    /// <summary>查询并挑选项目当前最合适的文件。</summary>
    public async Task<ResourceFile?> PickFileAsync(
        string projectId,
        ResourceKind kind,
        string? gameVersion,
        string? loaderName,
        CancellationToken ct)
    {
        var versions = await GetVersionsAsync(projectId, kind, gameVersion, loaderName, ct)
            .ConfigureAwait(false);
        return PickBestFile(versions, kind, loaderName);
    }

    /// <summary>获取项目详情（详情页头部使用）。</summary>
    public async Task<ResourceProjectSummary> GetProjectAsync(string idOrSlug, CancellationToken ct)
    {
        var json = await SendAsync(
                $"{BaseUrl}/project/{Uri.EscapeDataString(idOrSlug)}",
                ct)
            .ConfigureAwait(false);

        var dto = JsonUtil.Deserialize<ProjectDto>(json)
                  ?? throw new InvalidOperationException("Modrinth 返回的项目信息为空。");

        return ToSummary(dto);
    }

    /// <summary>批量获取项目摘要（一次解析一个版本的全部前置内容）。</summary>
    public async Task<IReadOnlyList<ResourceProjectSummary>> GetProjectsAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<ResourceProjectSummary>();
        }

        // 批量接口只支持 GET + ids（JSON 数组），POST 会 404。
        var query = Uri.EscapeDataString(JsonSerializer.Serialize(ids, JsonUtil.Options));
        var json = await SendAsync($"{BaseUrl}/projects?ids={query}", ct)
            .ConfigureAwait(false);

        var response = JsonUtil.Deserialize<List<ProjectDto>>(json);
        return response is null
            ? Array.Empty<ResourceProjectSummary>()
            : response.Select(ToSummary).ToList();
    }

    /// <summary>
    /// 获取项目全部版本的下载信息（不做游戏版本 / 加载器过滤，交给界面按实例推荐分组）。
    /// </summary>
    public async Task<IReadOnlyList<ResourceVersionInfo>> GetVersionInfosAsync(
        string projectId,
        ResourceKind kind,
        CancellationToken ct)
    {
        var versions = await GetVersionsAsync(projectId, kind, null, null, ct)
            .ConfigureAwait(false);

        var result = new List<ResourceVersionInfo>(versions.Count);

        foreach (var version in versions)
        {
            var file = PickFile(version, kind);
            if (file is null || string.IsNullOrWhiteSpace(file.DownloadUrl))
            {
                continue;
            }

            result.Add(new ResourceVersionInfo
            {
                Source = ResourceSource.Modrinth,
                Id = version.Id,
                Name = string.IsNullOrWhiteSpace(version.Name) ? version.VersionNumber : version.Name,
                VersionType = version.VersionType,
                PublishedAt = version.PublishedAt,
                GameVersions = version.GameVersions,
                Loaders = version.Loaders,
                File = new ResourceFile
                {
                    FileName = file.FileName,
                    DisplayName = version.Name,
                    DownloadUrl = file.DownloadUrl,
                    Sha1 = file.Sha1,
                    FileLength = file.Size,
                },
                Dependencies = ToDependencies(version),
            });
        }

        return result;
    }

    /// <summary>
    /// 从版本列表里挑文件：加载器匹配优先、正式版优先，再按发布时间取最新。
    /// </summary>
    public static ResourceFile? PickBestFile(
        IReadOnlyList<ModrinthVersion> versions,
        ResourceKind kind,
        string? loaderName)
    {
        if (versions.Count == 0)
        {
            return null;
        }

        var pool = versions.Where(version => PickFile(version, kind) is not null).ToList();
        if (pool.Count == 0)
        {
            return null;
        }

        if (kind == ResourceKind.Mod)
        {
            var loader = NormalizeLoader(loaderName);
            if (loader is not null)
            {
                var withLoader = pool
                    .Where(version => version.Loaders.Any(value =>
                        string.Equals(value, loader, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                pool = withLoader.Count > 0
                    ? withLoader
                    : pool.Where(version => !version.Loaders.Any(value => KnownLoaders.Contains(value))).ToList();

                if (pool.Count == 0)
                {
                    return null;
                }
            }
        }

        var ordered = pool.OrderByDescending(version => version.PublishedAt).ToList();
        var chosen = ordered.FirstOrDefault(version => version.IsRelease)
                     ?? ordered.FirstOrDefault(version =>
                         string.Equals(version.VersionType, "beta", StringComparison.OrdinalIgnoreCase))
                     ?? ordered[0];

        var file = PickFile(chosen, kind);
        return file is null
            ? null
            : new ResourceFile
            {
                FileName = file.FileName,
                DisplayName = string.IsNullOrWhiteSpace(chosen.Name) ? chosen.VersionNumber : chosen.Name,
                DownloadUrl = file.DownloadUrl,
                Sha1 = file.Sha1,
                FileLength = file.Size,
            };
    }

    // ---------------------------------------------------------------- 请求

    private async Task<string> SendAsync(
        string url,
        CancellationToken ct,
        HttpMethod? method = null,
        string? body = null)
    {
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        Exception? lastError = null;

        foreach (var candidate in _resolver.Resolve(url, DownloadUrlKind.ModPlatform))
        {
            try
            {
                return await _downloader
                    .RequestStringAsync(
                        candidate.Url,
                        method ?? HttpMethod.Get,
                        body,
                        body is null ? null : "application/json",
                        headers,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpApiException ex)
            {
                lastError = ex;
                _log.Warn($"Modrinth 请求失败（{candidate.SourceName}）：HTTP {(int)ex.StatusCode} {url}", ex);

                if ((int)ex.StatusCode is >= 400 and < 500)
                {
                    break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                lastError = ex;
                _log.Warn($"Modrinth 请求失败（{candidate.SourceName}）：{url}", ex);
            }
        }

        var hint = lastError is HttpApiException api
                   && api.StatusCode is HttpStatusCode.TooManyRequests
            ? "Modrinth 请求过于频繁，请稍后再试。"
            : "Modrinth 请求失败：" + (lastError?.Message ?? "网络不可用");

        throw new InvalidOperationException(hint, lastError);
    }

    // ---------------------------------------------------------------- 映射

    private static ModrinthProject ToProject(HitDto hit) => new()
    {
        Id = hit.ProjectId ?? string.Empty,
        Slug = hit.Slug ?? string.Empty,
        Name = string.IsNullOrWhiteSpace(hit.Title) ? hit.Slug ?? string.Empty : hit.Title!,
        Summary = hit.Description ?? string.Empty,
        IconUrl = string.IsNullOrWhiteSpace(hit.IconUrl) ? null : hit.IconUrl,
        Author = hit.Author,
        Categories = hit.Categories ?? new List<string>(),
        DownloadCount = hit.Downloads,
    };

    private static ModrinthVersion ToVersion(VersionDto dto) => new()
    {
        Id = dto.Id ?? string.Empty,
        Name = dto.Name ?? string.Empty,
        VersionNumber = dto.VersionNumber ?? string.Empty,
        VersionType = dto.VersionType ?? string.Empty,
        PublishedAt = dto.DatePublished,
        GameVersions = dto.GameVersions ?? new List<string>(),
        Loaders = dto.Loaders ?? new List<string>(),
        Files = dto.Files?
                    .Where(file => !string.IsNullOrWhiteSpace(file.Filename))
                    .Select(file => new ModrinthVersionFile
                    {
                        FileName = file.Filename!,
                        DownloadUrl = file.Url,
                        Sha1 = file.Hashes?.Sha1,
                        Size = file.Size,
                        IsPrimary = file.Primary,
                    })
                    .ToList()
                ?? new List<ModrinthVersionFile>(),
        Dependencies = dto.Dependencies?
                           .Where(dependency => !string.IsNullOrWhiteSpace(dependency.ProjectId))
                           .Select(dependency => new ModrinthDependency
                           {
                               ProjectId = dependency.ProjectId,
                               VersionId = dependency.VersionId,
                               DependencyType = dependency.DependencyType ?? string.Empty,
                           })
                           .ToList()
                       ?? new List<ModrinthDependency>(),
    };

    private static ResourceProjectSummary ToSummary(ProjectDto dto)
    {
        var type = string.IsNullOrWhiteSpace(dto.ProjectType) ? "mod" : dto.ProjectType;
        var slug = dto.Slug ?? string.Empty;

        return new ResourceProjectSummary
        {
            Source = ResourceSource.Modrinth,
            ModrinthId = dto.Id,
            Name = string.IsNullOrWhiteSpace(dto.Title) ? slug : dto.Title!,
            Summary = dto.Description ?? string.Empty,
            IconUrl = string.IsNullOrWhiteSpace(dto.IconUrl) ? null : dto.IconUrl,
            Categories = dto.Categories ?? new List<string>(),
            DownloadCount = dto.Downloads,
            WebsiteUrl = slug.Length == 0 ? null : $"https://modrinth.com/{type}/{slug}",
        };
    }

    /// <summary>把版本里的依赖压成「必需 / 可选」两类，同一项目出现多次时以必修为准。</summary>
    private static IReadOnlyList<ResourceDependency> ToDependencies(ModrinthVersion version)
    {
        var result = new List<ResourceDependency>();

        foreach (var group in version.Dependencies
                     .Where(dependency => !string.IsNullOrWhiteSpace(dependency.ProjectId))
                     .GroupBy(dependency => dependency.ProjectId!, StringComparer.Ordinal))
        {
            var kinds = group
                .Select(dependency => dependency.DependencyType.Trim().ToLowerInvariant())
                .ToList();

            var kind = kinds.Contains("required")
                ? ResourceDependencyKind.Required
                : kinds.Contains("optional")
                    ? ResourceDependencyKind.Optional
                    : (ResourceDependencyKind?)null;

            if (kind is null)
            {
                continue;
            }

            result.Add(new ResourceDependency
            {
                Source = ResourceSource.Modrinth,
                Kind = kind.Value,
                ModrinthProjectId = group.Key,
            });
        }

        return result;
    }

    /// <summary>按资源类别选版本里合适的文件：优先 primary，其次第一个符合扩展名的。</summary>
    private static ModrinthVersionFile? PickFile(ModrinthVersion version, ResourceKind kind)
    {
        var candidates = version.Files
            .Where(file => MatchesExtension(kind, file.FileName))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.FirstOrDefault(file => file.IsPrimary) ?? candidates[0];
    }

    private static bool MatchesExtension(ResourceKind kind, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return kind switch
        {
            ResourceKind.Mod => extension.Equals(".jar", StringComparison.OrdinalIgnoreCase),
            ResourceKind.ResourcePack => extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                                         || extension.Equals(".mcpack", StringComparison.OrdinalIgnoreCase),
            ResourceKind.ShaderPack => extension.Equals(".zip", StringComparison.OrdinalIgnoreCase),
            ResourceKind.Modpack => extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase)
                                    || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    // ---------------------------------------------------------------- DTO

    private sealed class SearchResponse
    {
        [JsonPropertyName("hits")]
        public List<HitDto>? Hits { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("total_hits")]
        public int TotalHits { get; set; }
    }

    private sealed class HitDto
    {
        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }
    }

    private sealed class VersionDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version_number")]
        public string? VersionNumber { get; set; }

        [JsonPropertyName("version_type")]
        public string? VersionType { get; set; }

        [JsonPropertyName("date_published")]
        public DateTimeOffset DatePublished { get; set; }

        [JsonPropertyName("game_versions")]
        public List<string>? GameVersions { get; set; }

        [JsonPropertyName("loaders")]
        public List<string>? Loaders { get; set; }

        [JsonPropertyName("files")]
        public List<VersionFileDto>? Files { get; set; }

        [JsonPropertyName("dependencies")]
        public List<DependencyDto>? Dependencies { get; set; }
    }

    private sealed class DependencyDto
    {
        [JsonPropertyName("version_id")]
        public string? VersionId { get; set; }

        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("dependency_type")]
        public string? DependencyType { get; set; }
    }

    private sealed class ProjectDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("project_type")]
        public string? ProjectType { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }
    }

    private sealed class VersionFileDto
    {
        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; } = -1;

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }

        [JsonPropertyName("hashes")]
        public HashesDto? Hashes { get; set; }
    }

    private sealed class HashesDto
    {
        [JsonPropertyName("sha1")]
        public string? Sha1 { get; set; }

        [JsonPropertyName("sha512")]
        public string? Sha512 { get; set; }
    }
}
