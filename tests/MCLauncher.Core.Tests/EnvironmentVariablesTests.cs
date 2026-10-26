using MCLauncher.Core.Utils;

namespace MCLauncher.Core.Tests;

public sealed class EnvironmentVariablesTests
{
    [Fact]
    public void 解析每行的键值并忽略注释与空行()
    {
        var parsed = EnvironmentVariables.Parse("""
            JAVA_TOOL_OPTIONS=-Dfile.encoding=UTF-8
            # 注释
            EMPTY=
            QUOTED="a b"
            没有等号
            """);

        Assert.Equal("-Dfile.encoding=UTF-8", parsed["JAVA_TOOL_OPTIONS"]);
        Assert.Equal(string.Empty, parsed["EMPTY"]);
        Assert.Equal("a b", parsed["QUOTED"]);
        Assert.Equal(3, parsed.Count);
    }

    [Fact]
    public void 键名不区分大小写()
    {
        var parsed = EnvironmentVariables.Parse("My_Var=1");
        Assert.Equal("1", parsed["MY_VAR"]);
    }

    [Fact]
    public void 空文本返回空字典()
    {
        Assert.Empty(EnvironmentVariables.Parse(null));
        Assert.Empty(EnvironmentVariables.Parse("   "));
    }
}
