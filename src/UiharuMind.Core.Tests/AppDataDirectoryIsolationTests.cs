using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Infrastructure;

/// <summary>
/// 钉住 Core 测试的数据根目录隔离（<see cref="AppDataDirectoryInitializer"/>，照 App 测试那份）：
/// 一旦有测试碰配置或会话，落盘必须落在临时目录，不能写用户的 <c>~/.uiharu</c>。
/// 外面显式指定过 <c>UIHARU_HOME</c> 也放行——那是刻意要复现真实档案，隔离依然成立。
/// </summary>
public class AppDataDirectoryIsolationTests
{
    [Fact]
    public void RootPointsToTempHome()
    {
        string? home = Environment.GetEnvironmentVariable("UIHARU_HOME");
        Assert.False(string.IsNullOrWhiteSpace(home));

        Assert.Equal(home, AppPaths.Root);

        Assert.NotEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".uiharu"),
            AppPaths.Root);
    }
}
