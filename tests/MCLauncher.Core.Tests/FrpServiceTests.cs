using MCLauncher.Core.Connect;
using MCLauncher.Core.Tests.Support;

namespace MCLauncher.Core.Tests;

/// <summary>frpc 只有在大小、PE 头与 SHA-256 全部通过时才算就绪。</summary>
public sealed class FrpServiceTests
{
    [Fact]
    public void 未下载或校验失败时不算就绪()
    {
        using var temp = new TempDirectory();
        using var service = new FrpService(new TestLog(), temp.Path);

        Assert.False(service.IsBinaryReady());

        var path = Path.Combine(temp.Path, "frp", "frpc.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 大小足够但没有合法的 MZ 头。
        File.WriteAllText(path, new string('M', 2_000_000));
        Assert.False(service.IsBinaryReady());

        // 有 MZ 头但内容与固定哈希不符。
        var bytes = new byte[2_000_000];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        File.WriteAllBytes(path, bytes);
        Assert.False(service.IsBinaryReady());
    }
}
