using UiharuMind.Core;

namespace UiharuMind.Core.Tests.AppIdentity;

/// <summary>
/// <see cref="AppInfo.Version"/> 的来源不变量。钉住的事实：版本号来自程序集属性
/// （唯一来源是 Directory.Build.props 的 &lt;Version&gt;），而不是代码里的手写常量。
/// <para>
/// 这条测试防的是一类真实事故：版本号曾同时存在于 AppInfo、Info.plist 和构建脚本的
/// grep 里三份，三者悄悄漂移到互不相同——代码写 0.1.0、发出去的包上写 1.0.0、
/// 而 grep 因为被重构过早已匹配不到，把 CFBundleVersion 填成了空串。
/// </para>
/// <para>
/// 只要 &lt;Version&gt; 没流进程序集（少了 PropertyGroup、被某个宿主吞掉），
/// <c>GetName().Version</c> 就退化成 0.0.0.0，这里必红。
/// </para>
/// </summary>
public class AppInfoVersionTests
{
    /// <summary>
    /// 版本号非零：程序集属性确实被填上了
    /// </summary>
    [Fact]
    public void Version_ComesFromAssembly_NotDefaultZero()
    {
        Assert.NotEqual(new Version(0, 0, 0), AppInfo.Version);
    }

    /// <summary>
    /// 归一成三段：程序集版本是四段，显示与比较沿用三段口径
    /// </summary>
    [Fact]
    public void Version_IsNormalizedToThreeParts()
    {
        Assert.Equal(-1, AppInfo.Version.Revision); //未指定
        Assert.Equal(AppInfo.Version.ToString(), $"{AppInfo.Version.Major}.{AppInfo.Version.Minor}.{AppInfo.Version.Build}");
    }

    /// <summary>
    /// 与承载 AppInfo 的程序集前三段一致
    /// </summary>
    [Fact]
    public void Version_MatchesDeclaringAssembly()
    {
        Version? assemblyVersion = typeof(AppInfo).Assembly.GetName().Version;

        Assert.NotNull(assemblyVersion);
        Assert.Equal(assemblyVersion.Major, AppInfo.Version.Major);
        Assert.Equal(assemblyVersion.Minor, AppInfo.Version.Minor);
        Assert.Equal(assemblyVersion.Build, AppInfo.Version.Build);
    }
}
