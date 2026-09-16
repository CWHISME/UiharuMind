using UiharuMind.Shared.Utils;

namespace UiharuMind.App.Tests.Shared;

/// <summary>
/// 「双击文本文件进编辑窗」的准入规则。
/// 钉住两点：白名单大小写不敏感（.MD 和 .md 一视同仁），以及大小上限只对真实文件生效
/// （不存在的路径不该因为扩展名在名单里就放行）。
/// </summary>
public class TextFileOpenPolicyTests
{
    /// <summary>常见文本与代码扩展名放行</summary>
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("README.md")]
    [InlineData("app.config.json")]
    [InlineData("Program.cs")]
    [InlineData("style.css")]
    public void SupportedExtensions_AreAllowed(string fileName)
    {
        Assert.True(TextFileOpenPolicy.IsSupportedExtension(fileName));
    }

    /// <summary>大小写不敏感：.MD 当作 .md</summary>
    [Fact]
    public void ExtensionMatch_IsCaseInsensitive()
    {
        Assert.True(TextFileOpenPolicy.IsSupportedExtension("README.MD"));
        Assert.True(TextFileOpenPolicy.IsSupportedExtension("app.JSON"));
    }

    /// <summary>非文本扩展名与没有扩展名的文件不进编辑窗</summary>
    [Theory]
    [InlineData("photo.png")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void UnsupportedExtensions_AreRejected(string fileName)
    {
        Assert.False(TextFileOpenPolicy.IsSupportedExtension(fileName));
    }

    /// <summary>真实小文件放行；不存在的路径即使扩展名在名单里也拒绝</summary>
    [Fact]
    public void IsSupported_RequiresExistingFileWithinLimit()
    {
        string path = Path.Combine(Path.GetTempPath(), $"open-policy-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "hello");

            Assert.True(TextFileOpenPolicy.IsSupported(path));
            Assert.False(TextFileOpenPolicy.IsSupported(Path.Combine(Path.GetTempPath(), "missing.md")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>超过编辑上限的文本文件退回系统打开</summary>
    [Fact]
    public void IsSupported_RejectsOversizedFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"open-policy-{Guid.NewGuid():N}.log");
        try
        {
            // 5MB + 1 字节：编辑场景扛不住，必须挡在门外
            File.WriteAllBytes(path, new byte[TextFileOpenPolicy.MaxEditBytes + 1]);

            Assert.True(TextFileOpenPolicy.IsSupportedExtension(path));
            Assert.False(TextFileOpenPolicy.IsWithinEditLimit(path));
            Assert.False(TextFileOpenPolicy.IsSupported(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}