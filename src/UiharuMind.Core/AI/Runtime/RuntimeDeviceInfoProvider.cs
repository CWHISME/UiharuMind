/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System.Diagnostics;
using System.Runtime.InteropServices;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime;

public static class RuntimeDeviceInfoProvider
{
    private static readonly object CpuLock = new();
    private static DateTimeOffset _lastCpuSampleAt;
    private static TimeSpan _lastProcessCpuTime;
    private static double _lastCpuUsage;

    // 一次采集要跑数个外部进程(macOS 上 sysctl×2 + vm_stat,实测约 60ms),
    // 而调用方多是绑定 getter 与属性变更处理器——切一次模型能连打十几次。
    // 设备内存/CPU 本就是采样值,秒级粒度对显示与风险评估都够用。
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(1);
    private static readonly object CacheLock = new();
    private static RuntimeDeviceInfo? _cached;

    private static readonly GpuDeviceProbe Gpu = new(() =>
        LLamaCppEngine.Distribution.CurrentVersion?.GetExecutable(LLamaCppSettingConfig.ServerExeName));
    private static long _cachedAtTimestamp;

    /// <summary>
    /// 采集设备信息。默认返回 1 秒内的缓存值，避免高频调用打爆外部进程。
    /// </summary>
    /// <param name="forceRefresh">true 时忽略缓存强制重采</param>
    /// <param name="waitForGpu">显存读数过期时当场重取（要起一次 llama-server，零点几秒到一两秒），加载前判风险用；
    /// 否则先用上次的、后台重取</param>
    /// <returns>设备信息</returns>
    public static RuntimeDeviceInfo Capture(bool forceRefresh = false, bool waitForGpu = false)
    {
        if (waitForGpu) forceRefresh = true;
        if (!forceRefresh)
        {
            lock (CacheLock)
            {
                if (_cached != null &&
                    Stopwatch.GetElapsedTime(_cachedAtTimestamp) < CacheTtl)
                {
                    return _cached;
                }
            }
        }

        RuntimeDeviceInfo info = CaptureCore(waitForGpu);
        lock (CacheLock)
        {
            _cached = info;
            _cachedAtTimestamp = Stopwatch.GetTimestamp();
        }

        return info;
    }

    private static RuntimeDeviceInfo CaptureCore(bool waitForGpu)
    {
        (long total, long available) = CaptureMemory();
        RuntimeGpuMemoryInfo gpu = CaptureGpuMemory(total, available, waitForGpu);
        return new RuntimeDeviceInfo(
            total,
            available,
            gpu.TotalBytes,
            gpu.AvailableBytes,
            CaptureProcessCpuUsage(),
            CaptureCpuName(),
            gpu.Name,
            gpu.Note,
            DateTimeOffset.Now,
            gpu.IsUnified);
    }

    private static (long Total, long Available) CaptureMemory()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return CaptureMacMemory();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return CaptureLinuxMemory();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CaptureWindowsMemory();

            long total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return (total, 0);
        }
        catch (Exception e)
        {
            Log.Warning($"Runtime device memory capture failed: {e.Message}");
            return (0, 0);
        }
    }

    private static (long Total, long Available) CaptureMacMemory()
    {
        long total = TryReadMacSysctlInt64("hw.memsize");
        long pageSize = TryReadMacSysctlInt64("hw.pagesize");
        if (pageSize <= 0) pageSize = 4096;

        string vmStat = RunCommand("vm_stat", "");
        long freePages = 0;
        long inactivePages = 0;
        long speculativePages = 0;
        foreach (string line in vmStat.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Pages free:", StringComparison.OrdinalIgnoreCase))
                freePages = ParseVmStatPages(line);
            else if (line.StartsWith("Pages inactive:", StringComparison.OrdinalIgnoreCase))
                inactivePages = ParseVmStatPages(line);
            else if (line.StartsWith("Pages speculative:", StringComparison.OrdinalIgnoreCase))
                speculativePages = ParseVmStatPages(line);
        }

        long available = (freePages + inactivePages + speculativePages) * pageSize;
        // 上面不含文件缓存与可压缩的内存，16G 的机器常只剩两三 G，按它判会把跑得动的模型拦下；
        // 内核自己的估计（memorystatus_level，可用百分比，内存压力就看它）把这些算进去，取两者大的
        long level = TryReadMacSysctlInt64("kern.memorystatus_level");
        if (level is > 0 and <= 100) available = Math.Max(available, total / 100 * level);
        return (total, available);
    }

    private static (long Total, long Available) CaptureWindowsMemory()
    {
        MEMORYSTATUSEX status = new();
        status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        return GlobalMemoryStatusEx(ref status)
            ? ((long)Math.Min(status.ullTotalPhys, long.MaxValue),
                (long)Math.Min(status.ullAvailPhys, long.MaxValue))
            : (0, 0);
    }

    private static (long Total, long Available) CaptureLinuxMemory()
    {
        long total = 0;
        long available = 0;
        foreach (string line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                total = ParseMemInfoKb(line) * 1024;
            else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                available = ParseMemInfoKb(line) * 1024;
        }

        return (total, available);
    }

    private static double CaptureProcessCpuUsage()
    {
        lock (CpuLock)
        {
            using Process process = Process.GetCurrentProcess();
            DateTimeOffset now = DateTimeOffset.Now;
            TimeSpan cpuTime = process.TotalProcessorTime;
            if (_lastCpuSampleAt == default)
            {
                _lastCpuSampleAt = now;
                _lastProcessCpuTime = cpuTime;
                return _lastCpuUsage;
            }

            double elapsedMs = (now - _lastCpuSampleAt).TotalMilliseconds;
            if (elapsedMs < 250) return _lastCpuUsage;

            double cpuMs = (cpuTime - _lastProcessCpuTime).TotalMilliseconds;
            _lastCpuUsage = Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100, 0, 100);
            _lastCpuSampleAt = now;
            _lastProcessCpuTime = cpuTime;
            return _lastCpuUsage;
        }
    }

    private static string? _cpuName; //型号名进程生命周期内不变,只探一次

    private static string CaptureCpuName()
    {
        return _cpuName ??= CaptureCpuNameCore();
    }

    private static string CaptureCpuNameCore()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return RunCommand("sysctl", "-n machdep.cpu.brand_string").Trim();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(x => x.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                    ?.Split(':', 2)
                    .LastOrDefault()
                    ?.Trim() ?? "";
        }
        catch
        {
            return "";
        }

        return RuntimeInformation.OSDescription;
    }

    private static RuntimeGpuMemoryInfo CaptureGpuMemory(long systemTotalBytes, long systemAvailableBytes,
        bool waitForGpu)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Apple Silicon/Metal 使用统一内存，显存就是系统内存
            return new RuntimeGpuMemoryInfo(
                systemTotalBytes,
                systemAvailableBytes,
                "Metal / unified memory",
                "Shared with system memory",
                true);
        }

        // 多卡时 llama.cpp 默认按层分摊到各卡，合起来算
        IReadOnlyList<GpuDevice> devices = Gpu.Devices(waitForGpu);
        if (devices.Count == 0) return new RuntimeGpuMemoryInfo(0, 0, "", "Unknown", false);
        return new RuntimeGpuMemoryInfo(
            devices.Sum(x => x.TotalBytes),
            devices.Sum(x => x.FreeBytes),
            string.Join(" + ", devices.Select(x => x.Name).Distinct()),
            string.Join(", ", devices.Select(x => x.Id)),
            false);
    }

    private static long TryReadMacSysctlInt64(string name)
    {
        string output = RunCommand("sysctl", $"-n {name}").Trim();
        return long.TryParse(output, out long value) ? value : 0;
    }

    private static string RunCommand(string fileName, string arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(1500);
        return output;
    }

    private static long ParseVmStatPages(string line)
    {
        string number = new(line.Where(char.IsDigit).ToArray());
        return long.TryParse(number, out long value) ? value : 0;
    }

    private static long ParseMemInfoKb(string line)
    {
        string number = new(line.Where(char.IsDigit).ToArray());
        return long.TryParse(number, out long value) ? value : 0;
    }

    private sealed record RuntimeGpuMemoryInfo(
        long TotalBytes,
        long AvailableBytes,
        string Name,
        string Note,
        bool IsUnified);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
