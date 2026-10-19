using MCLauncher.Core.Models;
using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class AccountTests
{
    /// <summary>
    /// 离线 UUID 必须等于 <c>UUID.nameUUIDFromBytes(("OfflinePlayer:"+name).getBytes(UTF_8))</c>。
    /// 这几个值是按 Java 算法独立算出来的参照值，写死在这里防止将来"顺手改成普通 MD5"。
    /// </summary>
    [Theory]
    [InlineData("Notch", "b50ad385829d3141a2167e7d7539ba7f")]
    [InlineData("Steve", "5627dd98e6be3c21b8a8e92344183641")]
    [InlineData("Player", "a01e3843e5213998958af459800e4d11")]
    public void 离线玩家UUID符合协议(string name, string expected) =>
        Assert.Equal(expected, HashUtil.OfflinePlayerUuid(name));

    [Fact]
    public void 离线账号带上版本位与变体位()
    {
        var uuid = HashUtil.OfflinePlayerUuid("Notch");

        Assert.Equal(32, uuid.Length);
        // version 3
        Assert.Equal('3', uuid[12]);
        // RFC 4122 variant（8/9/a/b）
        Assert.Contains(uuid[16], "89ab");
    }

    [Fact]
    public void 离线账号的默认字段()
    {
        var account = Account.CreateOffline("Player");

        Assert.Equal(AccountKind.Offline, account.Kind);
        Assert.Equal("legacy", account.UserType);
        Assert.Equal("0", account.AccessToken);
        Assert.False(account.IsMicrosoft);
        Assert.Null(account.Xuid);
    }

    [Fact]
    public void 微软账号使用msa类型()
    {
        var account = new Account { Kind = AccountKind.Microsoft, Name = "Steve" };
        Assert.Equal("msa", account.UserType);
        Assert.True(account.IsMicrosoft);
    }

    [Fact]
    public void 过期判定()
    {
        var account = new Account
        {
            Kind = AccountKind.Microsoft,
            Name = "S",
            ExpiresAt = DateTimeOffset.Now.AddMinutes(5),
        };

        Assert.False(account.IsExpired(TimeSpan.FromMinutes(1)));
        Assert.True(account.IsExpired(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void UUID格式化()
    {
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", HashUtil.FormatUuid("b50ad385829d3141a2167e7d7539ba7f"));
        // 已是标准写法时原样返回
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", HashUtil.FormatUuid("b50ad385-829d-3141-a216-7e7d7539ba7f"));
        // 非法长度不抛异常
        Assert.Equal("short", HashUtil.FormatUuid("short"));
    }

    [Fact]
    public void 游戏输出编码按Java版本选择()
    {
        var modern = MCLauncher.Core.Services.GameLauncher.ResolveOutputEncoding(21);
        Assert.Equal(System.Text.Encoding.UTF8.WebName, modern.WebName);

        // 老版本不应抛异常
        var legacy = MCLauncher.Core.Services.GameLauncher.ResolveOutputEncoding(8);
        Assert.NotNull(legacy);
    }

    [Fact]
    public void 退出码有可读解释()
    {
        Assert.Equal("正常退出", MCLauncher.Core.Services.GameLauncher.DescribeExitCode(0));
        Assert.Contains("异常退出", MCLauncher.Core.Services.GameLauncher.DescribeExitCode(1));
        Assert.Contains("异常退出", MCLauncher.Core.Services.GameLauncher.DescribeExitCode(255));
    }
}
