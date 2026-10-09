using System.Runtime.InteropServices;

namespace UiharuMind.Core.Core.Process;

/// <summary>
/// 整机 CPU 占用：每次取样与上一次比，算这段时间里忙的比例
/// </summary>
public sealed class SystemCpuMeter
{
    private const int HostCpuLoadInfo = 3; //HOST_CPU_LOAD_INFO：user/system/idle/nice 四项累计 tick
    private static readonly Lazy<uint> MachHost = new(mach_host_self);

    private readonly Lock _gate = new();
    private (ulong Busy, ulong Total)? _last;

    /// <summary>
    /// 取一次
    /// </summary>
    /// <returns>占全部核心的百分比（0~100）；头一回或读不到为 null</returns>
    public double? Sample()
    {
        if (TryReadTicks() is not { } ticks) return null;
        lock (_gate)
        {
            // macOS 的 tick 是 32 位，开机久了会回绕，回绕那次不算
            double? percent = _last is { } last && ticks.Total > last.Total && ticks.Busy >= last.Busy
                ?Math.Clamp((double)(ticks.Busy - last.Busy) / (ticks.Total - last.Total) * 100, 0, 100)
                : null;
            _last = ticks;
            return percent;
        }
    }

    private static (ulong Busy, ulong Total)? TryReadTicks()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) return ReadMac();
            if (OperatingSystem.IsLinux()) return ReadLinux();
            if (OperatingSystem.IsWindows()) return ReadWindows();
        }
        catch (Exception e) when (e is IOException or DllNotFoundException or EntryPointNotFoundException
                                      or FormatException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static (ulong Busy, ulong Total)? ReadMac()
    {
        uint[] info = new uint[4];
        uint count = (uint)info.Length;
        if (host_statistics(MachHost.Value, HostCpuLoadInfo, info, ref count) != 0) return null;
        ulong idle = info[2];
        ulong total = (ulong)info[0] + info[1] + info[2] + info[3];
        return (total - idle, total);
    }

    // 首行：cpu user nice system idle iowait irq softirq steal …，空闲算 idle + iowait
    private static (ulong Busy, ulong Total)? ReadLinux()
    {
        string? line = File.ReadLines("/proc/stat").FirstOrDefault();
        if (line == null || !line.StartsWith("cpu ")) return null;
        ulong[] values = line[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8).Select(ulong.Parse)
            .ToArray();
        if (values.Length < 5) return null;
        ulong total = values.Aggregate(0UL, (sum, x) => sum + x);
        ulong idle = values[3] + values[4];
        return (total - idle, total);
    }

    // 内核时间里含空闲
    private static (ulong Busy, ulong Total)? ReadWindows()
    {
        if (!GetSystemTimes(out ulong idle, out ulong kernel, out ulong user)) return null;
        return (kernel + user - idle, kernel + user);
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics(uint host, int flavor, uint[] info, ref uint count);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);
}
