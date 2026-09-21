using System.Runtime.CompilerServices;

namespace UiharuMind.App.Tests.Infrastructure;

/// <summary>
/// 把应用数据根目录挪到临时目录。
///
/// <see cref="UiharuMind.Core.Core.AppPaths.Root"/> 是个静态只读字段，<b>首次触达即定死</b>，
/// 所以只能在模块初始化里改——一旦某个测试先碰了配置或会话，根目录就已经是用户的
/// <c>~/.uiharu</c> 了，接下来写进去的都是真数据。
/// </summary>
internal static class AppDataDirectoryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // 外面显式指定过就听外面的(便于手工复现某个真实档案下的问题)
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UIHARU_HOME"))) return;

        Environment.SetEnvironmentVariable("UIHARU_HOME",
            Path.Combine(Path.GetTempPath(), $"uiharu-tests-home-{Guid.NewGuid():N}"));
    }
}
