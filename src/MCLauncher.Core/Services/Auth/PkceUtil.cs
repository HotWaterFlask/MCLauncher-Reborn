using System.Security.Cryptography;
using System.Text;

namespace MCLauncher.Core.Services.Auth;

/// <summary>
/// OAuth 2.0 的 PKCE 相关串生成。
/// 授权码流程必须带 PKCE（S256），否则公开客户端（没有 client secret）无法保证授权码不被截胡。
/// </summary>
public static class PkceUtil
{
    /// <summary>code_verifier：43~128 个 base64url 字符。这里用 32 字节随机数。</summary>
    public static string CreateVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    /// <summary>code_challenge = base64url(SHA256(verifier))。</summary>
    public static string CreateChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    /// <summary>state：防 CSRF，回调时必须原样返回。</summary>
    public static string CreateState() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
