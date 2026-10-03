using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests;

/// <summary>开发者口令只比哈希；标记文件开关存在即为真</summary>
public class DeveloperModeTests
{
    private const string TestPassphrase = "test-passphrase";

    [Theory]
    [InlineData("test-passphrase", true)]
    [InlineData("  test-passphrase\n", true)]
    [InlineData("TEST-PASSPHRASE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_ComparesTheSaltedHash(string? input, bool expected)
    {
        string hash = Convert.ToHexString(DeveloperMode.Hash(TestPassphrase));

        Assert.Equal(expected, DeveloperMode.Matches(input, hash));
    }

    [Theory]
    [InlineData("https://wangjiaying.top")] //屏蔽角色的入口不开开发者模式
    [InlineData("test-passphrase")]
    [InlineData("")]
    public void Matches_RealPassphraseRejectsOthers(string input)
    {
        Assert.False(DeveloperMode.Matches(input));
    }

    [Fact]
    public void MarkerFileFlag_PersistsAndNotifies()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uiharu-flag-{Guid.NewGuid():N}", "Marker");
        MarkerFileFlag flag = new(path);
        int changes = 0;
        flag.Changed += () => changes++;

        Assert.False(flag.IsSet);
        flag.Set(true);
        flag.Set(true); //没变不触发
        Assert.True(File.Exists(path));
        Assert.True(new MarkerFileFlag(path).IsSet); //重启后仍生效

        flag.Set(false);
        Assert.False(File.Exists(path));
        Assert.Equal(2, changes);
        Directory.Delete(Path.GetDirectoryName(path)!);
    }
}
