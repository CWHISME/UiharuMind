using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 版本号解析：带点的照常，只有一个数的（llama.cpp 构建号）当主版本号，比较才有意义
/// </summary>
public class ManagedVersionPackageTests
{
    [Theory]
    [InlineData("v0.1.0", "0.1.0")]
    [InlineData("b11443", "11443.0")]
    [InlineData("llama-b11443-bin-macos-arm64", "11443.0")]
    [InlineData("llama-b11443-bin-win-cuda-12.4-x64", "11443.0")]
    [InlineData("nightly", "0.0")]
    public void ParseVersion(string raw, string expected)
    {
        Assert.Equal(Version.Parse(expected), ManagedVersionPackage.ParseVersion(raw));
    }

    [Fact]
    public void BuildNumbers_CompareNumerically()
    {
        Assert.True(ManagedVersionPackage.ParseVersion("b11500") > ManagedVersionPackage.ParseVersion("llama-b11443-bin-macos-arm64"));
    }
}
