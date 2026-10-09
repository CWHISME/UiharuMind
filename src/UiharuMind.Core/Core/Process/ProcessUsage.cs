using System.Runtime.InteropServices;

namespace UiharuMind.Core.Core.Process;

/// <summary>
/// 一个进程此刻的占用
/// </summary>
/// <param name="MemoryBytes">内存（macOS 取活动监视器那一列的物理占用，含 Metal 缓冲；其余平台取常驻集）</param>
/// <param name="CpuTime">累计 CPU 时间</param>
public readonly record struct ProcessUsageSample(long MemoryBytes, TimeSpan CpuTime);

/// <summary>
/// 读别的进程的占用与子进程
/// </summary>
public static class ProcessUsage
{
    private const int RusageInfoV0 = 0;
    private const int PhysFootprintOffset = 16 + 7 * sizeof(ulong); //rusage_info_v0：16 字节 uuid 后第 8 个 uint64

    /// <summary>
    /// 取一次占用
    /// </summary>
    /// <param name="processId">进程 Id</param>
    /// <returns>占用；进程不在了为 null</returns>
    public static ProcessUsageSample? TrySample(int processId)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
            long memory = OperatingSystem.IsMacOS() && TryPhysFootprint(processId) is { } footprint
                ? footprint
                : process.WorkingSet64;
            return new ProcessUsageSample(memory, process.TotalProcessorTime);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
                                      or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 某进程的直接子进程（macOS / Linux 用 pgrep）
    /// </summary>
    /// <param name="processId">父进程 Id</param>
    /// <returns>子进程 Id；查不到为空</returns>
    public static IReadOnlyList<int> ChildrenOf(int processId)
    {
        if (OperatingSystem.IsWindows()) return [];
        try
        {
            using System.Diagnostics.Process pgrep = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("pgrep", ["-P", processId.ToString()])
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                })!;
            string output = pgrep.StandardOutput.ReadToEnd();
            pgrep.WaitForExit(2000);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim(), out int id) ? id : 0)
                .Where(x => x > 0)
                .ToList();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return [];
        }
    }

    // 活动监视器「内存」那一列：Metal 缓冲、压缩掉的都算在里面，常驻集会少算
    private static long? TryPhysFootprint(int processId)
    {
        byte[] buffer = new byte[256];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (proc_pid_rusage(processId, RusageInfoV0, handle.AddrOfPinnedObject()) != 0) return null;
            return (long)BitConverter.ToUInt64(buffer, PhysFootprintOffset);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            handle.Free();
        }
    }

    [DllImport("libproc.dylib", SetLastError = true)]
    private static extern int proc_pid_rusage(int pid, int flavor, IntPtr buffer);
}
