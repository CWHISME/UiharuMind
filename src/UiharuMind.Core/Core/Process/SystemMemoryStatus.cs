using System.Runtime.InteropServices;

namespace UiharuMind.Core.Core.Process;

/// <summary>
/// 系统内存压力档
/// </summary>
public enum ESystemMemoryPressure
{
    /// <summary>
    /// 系统不给（Windows、Linux），由调用方按可用比例自己分档
    /// </summary>
    Unknown,
    Normal,
    Warning,
    Critical
}

/// <summary>
/// 内存紧不紧：系统自己的压力档，加上压缩与换出了多少（可用内存只看得见余量，看不出已经在换页）
/// </summary>
/// <param name="Pressure">压力档</param>
/// <param name="CompressedBytes">被压缩占着的物理内存（macOS）</param>
/// <param name="SwapUsedBytes">已用交换区</param>
public sealed record SystemMemoryStatus(ESystemMemoryPressure Pressure, long CompressedBytes, long SwapUsedBytes)
{
    /// <summary>
    /// 什么都读不到
    /// </summary>
    public static SystemMemoryStatus Empty { get; } = new(ESystemMemoryPressure.Unknown, 0, 0);
}

/// <summary>
/// 读 <see cref="SystemMemoryStatus"/>。macOS 取内核的压力档（活动监视器「内存压力」那条线）、压缩器占用与交换区；
/// Linux 只有交换区；Windows 都没有
/// </summary>
public static class SystemMemoryStatusReader
{
    private const int HostVmInfo64 = 4;
    private const int VmStatistics64Count = 38; //sizeof(vm_statistics64_data_t) / sizeof(integer_t)
    private const int CompressorPageCountIndex = 32; //compressor_page_count 在第 33 个 integer_t
    private const int SpeculativeCountIndex = 23; //speculative_count；free/active/inactive 是前三个
    private static readonly Lazy<uint> MachHost = new(mach_host_self); //每次要一个会漏端口

    /// <summary>
    /// 读一次
    /// </summary>
    /// <returns>读不到的项为 0 / Unknown</returns>
    public static SystemMemoryStatus Read()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) return ReadMac();
            if (OperatingSystem.IsLinux()) return ReadLinux();
        }
        catch (Exception e) when (e is IOException or DllNotFoundException or EntryPointNotFoundException
                                      or FormatException or UnauthorizedAccessException)
        {
        }

        return SystemMemoryStatus.Empty;
    }

    private static SystemMemoryStatus ReadMac()
    {
        // 1 正常、2 警告、4 严重
        ESystemMemoryPressure pressure = SysctlInt("kern.memorystatus_vm_pressure_level") switch
        {
            1 => ESystemMemoryPressure.Normal,
            2 => ESystemMemoryPressure.Warning,
            4 => ESystemMemoryPressure.Critical,
            _ => ESystemMemoryPressure.Unknown
        };

        long pageSize = SysctlInt("hw.pagesize");
        long compressed = 0;
        if (pageSize > 0 && ReadVmStatistics() is { } info)
            compressed = (uint)info[CompressorPageCountIndex] * pageSize;

        // xsw_usage：total、avail、used 三个 uint64 打头
        long swapUsed = 0;
        byte[] swap = new byte[32];
        nint swapSize = swap.Length;
        if (sysctlbyname("vm.swapusage", swap, ref swapSize, IntPtr.Zero, 0) == 0)
            swapUsed = (long)BitConverter.ToUInt64(swap, 16);

        return new SystemMemoryStatus(pressure, compressed, swapUsed);
    }

    /// <summary>
    /// macOS 的空闲 + 非活跃 + 预读页（字节），即 vm_stat 那三行之和；直接问内核，不起 vm_stat 进程
    /// </summary>
    /// <returns>读不到为 null</returns>
    internal static long? ReadMacFreeInactiveSpeculative()
    {
        long pageSize = SysctlInt("hw.pagesize");
        if (pageSize <= 0 || ReadVmStatistics() is not { } info) return null;
        return ((long)(uint)info[0] + (uint)info[2] + (uint)info[SpeculativeCountIndex]) * pageSize;
    }

    private static int[]? ReadVmStatistics()
    {
        int[] info = new int[VmStatistics64Count];
        int count = info.Length;
        return host_statistics64(MachHost.Value, HostVmInfo64, info, ref count) == 0 ? info : null;
    }

    private static SystemMemoryStatus ReadLinux()
    {
        long swapTotal = 0;
        long swapFree = 0;
        foreach (string line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("SwapTotal:")) swapTotal = ParseKb(line);
            else if (line.StartsWith("SwapFree:")) swapFree = ParseKb(line);
        }

        return new SystemMemoryStatus(ESystemMemoryPressure.Unknown, 0, Math.Max(0, swapTotal - swapFree));
    }

    private static long ParseKb(string line) =>
        long.Parse(line[(line.IndexOf(':') + 1)..].Replace("kB", "").Trim()) * 1024;

    /// <summary>
    /// 读一个整数 sysctl（macOS）
    /// </summary>
    /// <param name="name">名字，如 hw.memsize</param>
    /// <returns>读不到为 0</returns>
    internal static long SysctlInt(string name)
    {
        byte[] value = new byte[8];
        nint size = value.Length;
        if (sysctlbyname(name, value, ref size, IntPtr.Zero, 0) != 0) return 0;
        return size == 4 ? BitConverter.ToInt32(value, 0) : BitConverter.ToInt64(value, 0);
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int sysctlbyname(string name, byte[] oldValue, ref nint oldSize, IntPtr newValue,
        nint newSize);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics64(uint host, int flavor, int[] info, ref int count);
}
