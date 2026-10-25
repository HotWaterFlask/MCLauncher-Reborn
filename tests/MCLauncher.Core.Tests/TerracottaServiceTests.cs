using MCLauncher.Core.Connect;

namespace MCLauncher.Core.Tests;

public sealed class TerracottaServiceTests
{
    [Theory]
    [InlineData("U/ABCD-1234-EFGH-5678", true)]
    [InlineData("u/abcd-1234-efgh-5678", true)]
    [InlineData("  U/ABCD-1234-EFGH-5678  ", true)]
    [InlineData("U/ABCD", false)]
    [InlineData("U/ABCD-1234-EFGH", false)]
    [InlineData("ABCD-1234-EFGH-5678", false)]
    [InlineData("U/ABCD_1234-EFGH-5678", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 房间码校验(string? code, bool expected)
    {
        Assert.Equal(expected, TerracottaService.IsValidRoomCode(code));
    }
}
