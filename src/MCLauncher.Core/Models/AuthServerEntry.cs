namespace MCLauncher.Core.Models;

/// <summary>
/// 外置登录认证服务器（authlib-injector / Yggdrasil API 根地址）。
/// 例如 LittleSkin 的 <c>https://littleskin.cn/api/yggdrasil</c>。
/// </summary>
public sealed class AuthServerEntry
{
    /// <summary>界面上显示的名字。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>API 根地址（不带结尾斜杠）。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>默认内置的 LittleSkin（与 HMCL 一致）。</summary>
    public static AuthServerEntry LittleSkin() => new()
    {
        Name = "LittleSkin",
        Url = "https://littleskin.cn/api/yggdrasil",
    };
}
