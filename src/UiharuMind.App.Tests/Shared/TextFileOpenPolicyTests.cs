using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 「进自家窗口」的准入规则：只判存在 + 大小，不判后缀。
/// .sh、无扩展名文件都是纯文本，白名单只会误伤；二进制由读取时的编码探测拦。
/// 两档：编辑上限内可编辑，超编辑但在查看上限内只读看，超查看才走系统。
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

    /// <summary>超编辑但在查看上限内：不可编辑，但仍进应用内只读窗</summary>
    [Fact]
    public void IsSupported_AllowsViewGapAsReadOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"open-policy-{Guid.NewGuid():N}.log");
        try
        {
            // 5MB + 1 字节：编辑扛不住，只读窗按行虚拟化扛得住
            using (var stream = File.Create(path))
            {
                stream.SetLength(TextFileOpenPolicy.MaxEditBytes + 1);
            }

            Assert.False(TextFileOpenPolicy.IsWithinEditLimit(path));
            Assert.True(TextFileOpenPolicy.IsWithinViewLimit(path));
            Assert.True(TextFileOpenPolicy.IsSupported(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>超过查看上限的退回系统打开</summary>
    [Fact]
    public void IsSupported_RejectsOversizedFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"open-policy-{Guid.NewGuid():N}.log");
        try
        {
            // 查看上限 + 1 字节：全量读盘也扛不住，必须挡在门外（稀疏创建，不真写 20MB）
            using (var stream = File.Create(path))
            {
                stream.SetLength(TextFileOpenPolicy.MaxViewBytes + 1);
            }

            Assert.False(TextFileOpenPolicy.IsWithinEditLimit(path));
            Assert.False(TextFileOpenPolicy.IsWithinViewLimit(path));
            Assert.False(TextFileOpenPolicy.IsSupported(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
