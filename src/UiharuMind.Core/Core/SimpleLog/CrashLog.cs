namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 崩溃记录：只收让进程退出的异常，追加进日志目录下 <c>Crash/Crash.txt</c>。
/// 它<b>不参与启动轮换</b>——普通日志只留 10 代，多启动几次，崩溃那一代就被挤掉了；
/// 放子目录是为了躲开日志目录顶层的 7 天过期清理
/// </summary>
public static class CrashLog
{
    private const string FileName = "Crash.txt";
    private const string PreviousFileName = "Crash.old.txt";
    private const long MaxBytes = 1024 * 1024;

    /// <summary>崩溃记录与异常退出留档所在目录</summary>
    public static string Directory => Path.Combine(LogManager.Instance.Directory, "Crash");

    /// <summary>
    /// 追加一条崩溃记录。同步直写不走日志队列，失败也不抛——它跑在进程临死的路径上
    /// </summary>
    /// <param name="exception">导致退出的异常</param>
    /// <param name="directory">记录所在目录，缺省为 <see cref="Directory"/></param>
    public static void Append(Exception exception, string? directory = null)
    {
        try
        {
            directory ??= Directory;
            System.IO.Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);

            // 超过上限就整份挪成 .old，保留上一批而不无限增长
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                File.Move(path, Path.Combine(directory, PreviousFileName), overwrite: true);
            }

            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] pid {Environment.ProcessId}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 临死路径上不再抛
        }
    }
}
