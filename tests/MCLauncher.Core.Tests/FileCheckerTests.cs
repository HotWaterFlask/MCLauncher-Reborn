using System.Text;
using MCLauncher.Core.Network;
using MCLauncher.Core.Tests.Support;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class FileCheckerTests
{
    [Fact]
    public void 哈希与大小都匹配时通过()
    {
        using var temp = new TempDirectory();
        var path = temp.File("a.bin");
        File.WriteAllText(path, "hello minecraft", Encoding.UTF8);

        var checker = FileChecker.Of(HashUtil.Sha1OfFile(path), new FileInfo(path).Length);
        Assert.True(checker.Check(path));
    }

    [Fact]
    public void 哈希不匹配时失败()
    {
        using var temp = new TempDirectory();
        var path = temp.File("a.bin");
        File.WriteAllText(path, "hello minecraft", Encoding.UTF8);

        Assert.False(FileChecker.Of(new string('0', 40)).Check(path));
    }

    [Fact]
    public void 大小不匹配时失败()
    {
        using var temp = new TempDirectory();
        var path = temp.File("a.bin");
        File.WriteAllText(path, "abc", Encoding.UTF8);

        Assert.False(FileChecker.Of(null, 999).Check(path));
    }

    [Fact]
    public void 文件不存在时失败()
    {
        using var temp = new TempDirectory();
        Assert.False(FileChecker.ExistenceOnly.Check(temp.File("missing.bin")));
    }

    [Fact]
    public void 无约束规则只要求文件存在()
    {
        using var temp = new TempDirectory();
        var path = temp.File("a.bin");
        File.WriteAllText(path, "x", Encoding.UTF8);

        Assert.False(FileChecker.ExistenceOnly.HasConstraint);
        Assert.True(FileChecker.ExistenceOnly.Check(path));
    }

    [Fact]
    public void SHA256匹配时通过()
    {
        using var temp = new TempDirectory();
        var path = temp.File("a.bin");
        File.WriteAllText(path, "hello minecraft", Encoding.UTF8);

        var checker = new FileChecker { Sha256 = HashUtil.Sha256OfFile(path) };
        Assert.True(checker.HasConstraint);
        Assert.True(checker.Check(path));
    }

    [Fact]
    public void SHA256不匹配时失败()
    {
        using var temp = new TempDirectory();
        var path = temp.File("a.bin");
        File.WriteAllText(path, "hello minecraft", Encoding.UTF8);

        Assert.False(new FileChecker { Sha256 = new string('0', 64) }.Check(path));
    }
}
