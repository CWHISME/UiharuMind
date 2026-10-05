using System.Text.Json;
using UiharuMind.Core.Core.Instances;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Diagnostics;

/// <summary>
/// 上次没有正常退出的那次运行
/// </summary>
/// <param name="StartedAt">那次运行的启动时间</param>
/// <param name="DebuggerAttached">启动时是否挂着调试器——调试器「停止」就是直接杀进程，不算事故</param>
/// <param name="PreservedLog">留档的日志副本；挂调试器或找不到日志时为 null</param>
public sealed record UncleanExit(DateTime StartedAt, bool DebuggerAttached, string? PreservedLog);

/// <summary>
/// 异常退出检测：启动时按进程留一个标记，正常退出时删掉。
/// 下次启动发现标记还在、对应进程又已不在，就说明那次是被杀、崩溃或断电，
/// 而轮换只留 10 代，那次的日志此时就留档到崩溃目录，免得再启动几次被冲掉。
///
/// <para>标记按 pid 分文件，多开互不干扰；pid 被复用时靠进程启动时间分辨。</para>
/// </summary>
public sealed class UncleanExitGuard
{
    private const int MaxPreserved = 10;
    private const string PreservedPrefix = "Unclean-";

    private readonly string _markerPath;

    private UncleanExitGuard(string markerPath)
    {
        _markerPath = markerPath;
    }

    private sealed record Marker(int Pid, DateTime ProcessStart, DateTime StartedAt, bool DebuggerAttached, string? LogSession);

    /// <summary>
    /// 应用启动用：标记放 <c>run/instances</c>，留档放崩溃目录，查到的异常退出写进日志
    /// </summary>
    /// <param name="debuggerAttached">本进程是否挂着调试器</param>
    /// <returns>守卫，正常退出时调 <see cref="MarkClean"/></returns>
    public static UncleanExitGuard Start(bool debuggerAttached)
    {
        UncleanExitGuard guard = Begin(AppInstance.InstancesDirectory, LogManager.Instance.Directory,
            CrashLog.Directory, LogManager.Instance.SessionId, debuggerAttached, out List<UncleanExit> found);
        foreach (UncleanExit exit in found)
        {
            if (exit.DebuggerAttached)
            {
                Log.Debug($"Previous run (started {exit.StartedAt:yyyy-MM-dd HH:mm:ss}) was stopped under the debugger.");
            }
            else
            {
                Log.Warning($"Previous run (started {exit.StartedAt:yyyy-MM-dd HH:mm:ss}) did not exit normally. " +
                            $"Log preserved: {exit.PreservedLog ?? "(not found)"}");
            }
        }
        return guard;
    }

    /// <summary>
    /// 留下本进程的标记，并收拾之前没正常退出的那些运行
    /// </summary>
    /// <param name="markerDirectory">标记目录</param>
    /// <param name="logDirectory">日志目录，从这里找那次运行的日志</param>
    /// <param name="preserveDirectory">留档目录</param>
    /// <param name="logSession">本进程的日志会话戳</param>
    /// <param name="debuggerAttached">本进程是否挂着调试器</param>
    /// <param name="found">之前没正常退出的运行，按启动时间排序</param>
    /// <returns>守卫，正常退出时调 <see cref="MarkClean"/></returns>
    public static UncleanExitGuard Begin(string markerDirectory, string logDirectory, string preserveDirectory,
        string? logSession, bool debuggerAttached, out List<UncleanExit> found)
    {
        Directory.CreateDirectory(markerDirectory);
        found = CollectStale(markerDirectory, logDirectory, preserveDirectory);

        using System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess();
        string path = Path.Combine(markerDirectory, $"{self.Id}.json");
        Marker marker = new(self.Id, self.StartTime, DateTime.Now, debuggerAttached, logSession);
        TryWrite(path, JsonSerializer.Serialize(marker));
        return new UncleanExitGuard(path);
    }

    /// <summary>
    /// 除本进程外还有几个活着的实例
    /// </summary>
    /// <returns>活着的其它实例数</returns>
    public static int CountOtherAlive()
    {
        string markerDirectory = AppInstance.InstancesDirectory;
        if (!Directory.Exists(markerDirectory)) return 0;

        int count = 0;
        foreach (string path in Directory.EnumerateFiles(markerDirectory, "*.json"))
        {
            Marker? marker = TryRead(path);
            if (marker != null && marker.Pid != Environment.ProcessId && IsAlive(marker)) count++;
        }
        return count;
    }

    /// <summary>正常退出：删掉本进程的标记。崩溃路径上不要调</summary>
    public void MarkClean() => TryDelete(_markerPath);

    private static List<UncleanExit> CollectStale(string markerDirectory, string logDirectory, string preserveDirectory)
    {
        List<UncleanExit> found = new();
        foreach (string path in Directory.EnumerateFiles(markerDirectory, "*.json"))
        {
            Marker? marker = TryRead(path);

            if (marker != null && IsAlive(marker)) continue; //另一个还开着的实例

            TryDelete(path);
            if (marker == null) continue;

            string? preserved = marker.DebuggerAttached || marker.LogSession == null
                ? null
                : Preserve(logDirectory, preserveDirectory, marker);
            found.Add(new UncleanExit(marker.StartedAt, marker.DebuggerAttached, preserved));
        }

        if (found.Any(exit => exit.PreservedLog != null)) TrimPreserved(preserveDirectory);
        return found.OrderBy(exit => exit.StartedAt).ToList();
    }

    private static Marker? TryRead(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsAlive(Marker marker)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(marker.Pid);
            // 启动时间对不上就是 pid 被别的进程复用了
            return !process.HasExited && Math.Abs((process.StartTime - marker.ProcessStart).TotalSeconds) < 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 那次运行的主日志可能滚动过好几份,按从旧到新拼成一份
    private static string? Preserve(string logDirectory, string preserveDirectory, Marker marker)
    {
        List<string> files = LogStore.FindSessionFiles(logDirectory, marker.LogSession!);
        if (files.Count == 0) return null;

        try
        {
            Directory.CreateDirectory(preserveDirectory);
            string target = Path.Combine(preserveDirectory,
                $"{PreservedPrefix}{marker.StartedAt:yyyyMMdd-HHmmss}-{marker.LogSession}.txt");
            using FileStream output = new(target, FileMode.Create, FileAccess.Write);
            foreach (string file in files)
            {
                using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                input.CopyTo(output);
            }
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TrimPreserved(string preserveDirectory)
    {
        try
        {
            foreach (string stale in Directory.EnumerateFiles(preserveDirectory, $"{PreservedPrefix}*.txt")
                         .OrderByDescending(File.GetLastWriteTime)
                         .Skip(MaxPreserved))
            {
                File.Delete(stale);
            }
        }
        catch (Exception)
        {
            // 清理失败不影响启动
        }
    }

    private static void TryWrite(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
        }
        catch (Exception)
        {
            // 标记写不下只是少一次检测,不影响启动
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // 删不掉最坏是下次多报一次
        }
    }
}
