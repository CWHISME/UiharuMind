using System.Runtime.CompilerServices;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.Infrastructure;

/// <summary>
/// 把日志单例的落盘位置挪到临时目录。测试里遍布 <c>Log.Error</c>，
/// 不挪的话一次测试就会写进并轮换用户真实的日志目录，把上次运行的日志挤掉
/// </summary>
internal static class LogDirectoryInitializer
{
    [ModuleInitializer]
    internal static void Initialize() =>
        LogManager.UseDirectory(Path.Combine(Path.GetTempPath(), $"uiharu-tests-log-{Guid.NewGuid():N}"));
}
