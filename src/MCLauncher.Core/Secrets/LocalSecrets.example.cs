/**
 * ============================================================================
 *  LocalSecrets.example.cs —— 本机私有凭据模板
 * ============================================================================
 *
 *  这个文件是**模板**，不参与编译，也不会被 git 忽略。
 *  真正生效的是同目录下的 LocalSecrets.cs（已被 .gitignore 排除，绝不提交）。
 *
 *  首次使用：
 *    1. 把本文件复制一份，重命名为 LocalSecrets.cs
 *       （如果直接构建也能跑：工程会在缺少该文件时自动复制一份）
 *    2. 把下面两个空字符串换成你自己的真实值
 *    3. 构建时 LocalSecrets.cs 会被编译进 MCLauncher.Core
 *
 *  凭据的三级优先级（见 ConfigService）：
 *      界面设置（settings.json） > 环境变量 > 本文件
 *  也就是说本文件是"默认值"，用户在设置页填了值就会覆盖它。
 *
 *  严禁把真实凭据提交到任何仓库、截图、issue。
 * ============================================================================
 */

namespace MCLauncher.Core.Secrets;

/// <summary>
/// 本机私有凭据。
/// <para>
/// 这里只放**开发者自己的**凭据，用于本地构建时省去每次手填。
/// 留空字符串表示"未配置"，此时上层会退回环境变量、再退回界面设置。
/// </para>
/// </summary>
internal static class LocalSecrets
{
    /// <summary>
    /// 微软登录用的 Azure 应用（客户端）ID。
    /// <para>
    /// 获取位置：Azure 门户 → 应用注册 → 你的应用 → 概述 →「应用程序(客户端) ID」。
    /// 注意：
    ///  - 「支持的账户类型」必须包含**个人 Microsoft 账户**；
    ///  - 「身份验证 → 允许公共客户端流」必须为**是**（设备码登录需要）；
    ///  - 设备码登录不需要注册任何回调地址。
    /// </para>
    /// <para>
    /// 另外：新注册的应用还必须提交 Mojang AppID 审核
    /// （https://aka.ms/mce-reviewappid）加入允许清单，否则
    /// api.minecraftservices.com/authentication/login_with_xbox 会直接返回 403。
    /// </para>
    /// </summary>
    public const string MsClientId = "";

    /// <summary>
    /// CurseForge API Key。
    /// <para>
    /// 获取位置：https://console.curseforge.com/ → API Keys → 创建并复制。
    /// 为空时 CurseForge 检索与 CurseForge 整合包导入不可用；
    /// Modrinth 不需要 Key，始终可用。
    /// </para>
    /// </summary>
    public const string CurseForgeApiKey = "";
}
