using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 「进自家编辑窗」的准入规则：只判存在 + 大小，不判后缀。
/// .sh、无扩展名文件都是纯文本，白名单只会误伤；二进制由读取时的编码探测拦。
/// </summary>
public class TextFileOpenPolicyTests
{
    /// <summary>后缀不限：脚本、无扩展名文件一律放行到读取阶段</summary>
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("README.md")]
    [InlineData("deploy.sh")]
    [InlineData("Makefile")]
    [InlineData("noextension")]
    [InlineData("photo.png")]
    public void IsSupported_AllowsAnyExistingFileWithinLimit(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), $"open-policy-{Guid.NewGuid():N}-{fileName}");
        try
        {
            File.WriteAllText(path, "hello");

            Assert.True(TextFileOpenPolicy.IsSupported(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>不存在的路径拒绝</summary>
    [Fact]
    public void IsSupported_RejectsMissingFile()
    {
        Assert.False(TextFileOpenPolicy.IsSupported(Path.Combine(Path.GetTempPath(), "missing.md")));
    }

    /// <summary>超过编辑上限的退回系统打开</summary>
    [Fact]
    public void IsSupported_RejectsOversizedFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"open-policy-{Guid.NewGuid():N}.log");
        try
        {
            // 5MB + 1 字节：编辑场景扛不住，必须挡在门外
            File.WriteAllBytes(path, new byte[TextFileOpenPolicy.MaxEditBytes + 1]);

            Assert.False(TextFileOpenPolicy.IsWithinEditLimit(path));
            Assert.False(TextFileOpenPolicy.IsSupported(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
