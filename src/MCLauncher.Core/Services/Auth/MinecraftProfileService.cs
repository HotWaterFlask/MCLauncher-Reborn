using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using MCLauncher.Core.Abstractions;
using MCLauncher.Core.Models;
using MCLauncher.Core.Network;

namespace MCLauncher.Core.Services.Auth;

/// <summary>皮肤模型（对应 Mojang 的 <c>variant</c>）。</summary>
public enum SkinVariant
{
    Classic,
    Slim,
}

/// <summary>账号拥有的一个披风。</summary>
public sealed class MinecraftCape
{
    public required string Id { get; init; }

    public required string Url { get; init; }

    /// <summary>披风的展示名，部分服务器/版本不返回。</summary>
    public string? Alias { get; init; }

    /// <summary>是否正在使用。</summary>
    public bool IsActive { get; init; }
}

/// <summary><c>/minecraft/profile</c> 的结果：当前皮肤与披风。</summary>
public sealed class MinecraftSkinProfile
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? SkinUrl { get; init; }

    public SkinVariant SkinVariant { get; init; }

    public IReadOnlyList<MinecraftCape> Capes { get; init; } = Array.Empty<MinecraftCape>();

    public MinecraftCape? ActiveCape => Capes.FirstOrDefault(cape => cape.IsActive);
}

/// <summary>换肤失败。消息可直接展示给用户。</summary>
public sealed class MinecraftProfileException : Exception
{
    public MinecraftProfileException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// 微软账户的皮肤 / 披风管理：读写 <c>api.minecraftservices.com</c> 的档案接口。
/// 只接受微软账户；离线与外置登录没有可修改的 Mojang 档案。
/// </summary>
public sealed class MinecraftProfileService
{
    private const string ApiBase = "https://api.minecraftservices.com";

    private readonly HttpDownloader _downloader;
    private readonly ILogger _log;

    public MinecraftProfileService(HttpDownloader downloader, ILogger log)
    {
        _downloader = downloader;
        _log = log;
    }

    /// <summary>读取当前皮肤与已拥有的披风。</summary>
    public async Task<MinecraftSkinProfile> GetProfileAsync(Account account, CancellationToken ct = default)
    {
        EnsureMicrosoft(account);

        string json;
        try
        {
            json = await _downloader
                .RequestStringAsync(
                    $"{ApiBase}/minecraft/profile",
                    HttpMethod.Get,
                    null,
                    null,
                    AuthHeaders(account),
                    ct)
                .ConfigureAwait(false);
        }
        catch (HttpApiException ex)
        {
            throw new MinecraftProfileException(Describe(ex, "读取皮肤与披风失败"), ex);
        }

        try
        {
            return ParseProfile(json);
        }
        catch (JsonException ex)
        {
            throw new MinecraftProfileException("服务器返回的档案格式无法解析", ex);
        }
    }

    /// <summary>上传皮肤。必须为 64×64 或 64×32 的 PNG。</summary>
    public async Task UploadSkinAsync(
        Account account,
        byte[] png,
        SkinVariant variant,
        CancellationToken ct = default)
    {
        EnsureMicrosoft(account);

        if (png.Length == 0)
        {
            throw new MinecraftProfileException("皮肤文件为空");
        }

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "skin.png");
        form.Add(
            new StringContent(variant == SkinVariant.Slim ? "SLIM" : "CLASSIC"),
            "variant");

        try
        {
            await _downloader
                .RequestContentAsync(
                    $"{ApiBase}/minecraft/profile/skins",
                    HttpMethod.Post,
                    form,
                    AuthHeaders(account),
                    ct)
                .ConfigureAwait(false);
        }
        catch (HttpApiException ex)
        {
            throw new MinecraftProfileException(Describe(ex, "上传皮肤失败"), ex);
        }

        _log.Info($"已上传皮肤：{account.Name}（{variant}）");
    }

    /// <summary>恢复成 Mojang 默认皮肤。</summary>
    public async Task ResetSkinAsync(Account account, CancellationToken ct = default)
    {
        EnsureMicrosoft(account);

        try
        {
            await _downloader
                .RequestStringAsync(
                    $"{ApiBase}/minecraft/profile/skins",
                    HttpMethod.Delete,
                    null,
                    null,
                    AuthHeaders(account),
                    ct)
                .ConfigureAwait(false);
        }
        catch (HttpApiException ex)
        {
            throw new MinecraftProfileException(Describe(ex, "恢复默认皮肤失败"), ex);
        }

        _log.Info($"已恢复默认皮肤：{account.Name}");
    }

    /// <summary>更换披风；<paramref name="capeId"/> 为空表示隐藏披风。</summary>
    public async Task SetCapeAsync(Account account, string? capeId, CancellationToken ct = default)
    {
        EnsureMicrosoft(account);

        try
        {
            if (string.IsNullOrWhiteSpace(capeId))
            {
                await _downloader
                    .RequestStringAsync(
                        $"{ApiBase}/minecraft/profile/capes/active",
                        HttpMethod.Delete,
                        null,
                        null,
                        AuthHeaders(account),
                        ct)
                    .ConfigureAwait(false);
            }
            else
            {
                await _downloader
                    .RequestStringAsync(
                        $"{ApiBase}/minecraft/profile/capes/active",
                        HttpMethod.Put,
                        JsonSerializer.Serialize(new { capeId }),
                        "application/json",
                        AuthHeaders(account),
                        ct)
                    .ConfigureAwait(false);
            }
        }
        catch (HttpApiException ex)
        {
            throw new MinecraftProfileException(Describe(ex, "更换披风失败"), ex);
        }
    }

    /// <summary>下载皮肤/披风纹理。地址是公开的 textures.minecraft.net，不需要令牌。</summary>
    public Task<byte[]> DownloadTextureAsync(string url, CancellationToken ct = default) =>
        _downloader.GetBytesAsync(url, ct);

    private static void EnsureMicrosoft(Account account)
    {
        if (!account.IsMicrosoft)
        {
            throw new MinecraftProfileException("只有微软账户支持更换皮肤与披风");
        }

        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            throw new MinecraftProfileException("该账号没有可用的登录令牌，请重新登录");
        }
    }

    private static IReadOnlyDictionary<string, string> AuthHeaders(Account account) =>
        new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + account.AccessToken,
        };

    private static MinecraftSkinProfile ParseProfile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string? skinUrl = null;
        var variant = SkinVariant.Classic;

        if (root.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
        {
            JsonElement? selected = null;
            foreach (var skin in skins.EnumerateArray())
            {
                selected ??= skin;
                if (skin.TryGetProperty("state", out var state) &&
                    string.Equals(state.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    selected = skin;
                    break;
                }
            }

            if (selected is { } entry)
            {
                skinUrl = entry.TryGetProperty("url", out var url) ? url.GetString() : null;
                if (entry.TryGetProperty("variant", out var variantElement) &&
                    string.Equals(variantElement.GetString(), "SLIM", StringComparison.OrdinalIgnoreCase))
                {
                    variant = SkinVariant.Slim;
                }
            }
        }

        var capes = new List<MinecraftCape>();
        if (root.TryGetProperty("capes", out var capesElement) && capesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var cape in capesElement.EnumerateArray())
            {
                var id = cape.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                var url = cape.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                capes.Add(new MinecraftCape
                {
                    Id = id!,
                    Url = url!,
                    Alias = cape.TryGetProperty("alias", out var aliasElement) ? aliasElement.GetString() : null,
                    IsActive = cape.TryGetProperty("state", out var stateElement) &&
                               string.Equals(stateElement.GetString(), "ACTIVE", StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        return new MinecraftSkinProfile
        {
            Id = root.TryGetProperty("id", out var profileId) ? profileId.GetString() ?? string.Empty : string.Empty,
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            SkinUrl = skinUrl,
            SkinVariant = variant,
            Capes = capes,
        };
    }

    private static string Describe(HttpApiException ex, string fallback)
    {
        var (code, detail) = ReadError(ex.ResponseBody);

        return ex.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "登录已失效，请重新登录微软账户后再试",
            HttpStatusCode.Forbidden => "该微软账户没有修改皮肤或披风的权限",
            HttpStatusCode.TooManyRequests => "操作过于频繁，请稍后再试",
            HttpStatusCode.BadRequest => code switch
            {
                "INVALID_SKIN" => "皮肤文件无效：请使用 64×64（或 64×32）的 PNG",
                "INVALID_CAPE" => "披风 ID 无效，可能该披风已不属于此账号",
                _ => string.IsNullOrWhiteSpace(detail) ? fallback : $"{fallback}：{detail}",
            },
            _ => $"{fallback}（HTTP {(int)ex.StatusCode}）" +
                 (string.IsNullOrWhiteSpace(detail) ? string.Empty : $"：{detail}"),
        };
    }

    private static (string? Code, string? Detail) ReadError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = root.TryGetProperty("errorType", out var type) ? type.GetString() : null;
            var detail = root.TryGetProperty("errorMessage", out var message) ? message.GetString() : null;
            detail ??= root.TryGetProperty("error", out var error) ? error.GetString() : null;
            return (code, detail);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
