using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// nightly-tag.txt 解析：正式版钉住的构建就写在这个文件里，取第一行非空文本
/// </summary>
public class GitHubReleaseAssetHelperTests
{
    [Theory]
    [InlineData("b11429\n", "b11429")]
    [InlineData("  b11429  ", "b11429")]
    [InlineData("\n\nb11429\n", "b11429")]
    [InlineData("v0.6.0", "v0.6.0")]
    public void ParseNightlyTag_ReturnsFirstNonEmptyLine(string content, string expected)
    {
        Assert.Equal(expected, GitHubReleaseAssetHelper.ParseNightlyTag(content));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void ParseNightlyTag_Empty_ReturnsNull(string? content)
    {
        Assert.Null(GitHubReleaseAssetHelper.ParseNightlyTag(content));
    }
}
