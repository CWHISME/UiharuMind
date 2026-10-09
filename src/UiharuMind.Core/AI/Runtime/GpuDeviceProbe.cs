using System.Diagnostics;
using System.Text.RegularExpressions;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Runtime;

/// <summary>
/// 一块 GPU
/// </summary>
/// <param name="Id">ggml 设备名，如 CUDA0、ROCm0、Vulkan0、MTL0</param>
/// <param name="Name">型号</param>
/// <param name="TotalBytes">显存总量</param>
/// <param name="FreeBytes">此刻空闲</param>
public sealed record GpuDevice(string Id, string Name, long TotalBytes, long FreeBytes);

/// <summary>
/// 问 llama-server 有哪些 GPU、各剩多少显存（<c>--list-devices</c>，与 ollama 同法）：
/// 走的是 ggml 各后端自己的查询，N 卡、A 卡（ROCm/Vulkan）、Intel（SYCL/Vulkan）都认，不靠厂商工具。
/// 起一次进程要零点几秒到一两秒（CUDA 初始化），结果缓存一阵，过期在后台重取
/// </summary>
public sealed partial class GpuDeviceProbe
{
    private const long MiB = 1024L * 1024L;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<string?> _executable;
    private readonly Lock _gate = new();
    private IReadOnlyList<GpuDevice> _devices = [];
    private long? _probedAt; //上次取完的时刻，没取过为 null
    private Task? _probing;

    /// <param name="executable">llama-server 路径；没装引擎为 null</param>
    public GpuDeviceProbe(Func<string?> executable)
    {
        _executable = executable;
    }

    /// <summary>
    /// 取设备列表
    /// </summary>
    /// <param name="wait">缓存过期时当场重取（加载前判风险用）；否则先给旧的、后台重取</param>
    /// <returns>有显存的设备；没装引擎或取不到为空</returns>
    public IReadOnlyList<GpuDevice> Devices(bool wait)
    {
        Task? probing;
        lock (_gate)
        {
            if (_probedAt is { } at && Stopwatch.GetElapsedTime(at) < Ttl) return _devices;
            probing = _probing ??= Task.Run(Probe);
        }

        if (wait) probing.Wait();
        lock (_gate) return _devices;
    }

    /// <summary>
    /// 解析 <c>--list-devices</c> 的输出，每行形如 <c>  CUDA0: NVIDIA GeForce RTX 4090 (24563 MiB, 23012 MiB free)</c>；
    /// 显存为 0 的（BLAS 之类）不算
    /// </summary>
    /// <param name="output">命令输出</param>
    /// <returns>设备</returns>
    public static IReadOnlyList<GpuDevice> Parse(string output)
    {
        List<GpuDevice> devices = [];
        foreach (Match match in DeviceLine().Matches(output))
        {
            long total = long.Parse(match.Groups["total"].Value) * MiB;
            if (total <= 0) continue;
            devices.Add(new GpuDevice(match.Groups["id"].Value, match.Groups["name"].Value.Trim(), total,
                long.Parse(match.Groups["free"].Value) * MiB));
        }

        return devices;
    }

    private void Probe()
    {
        IReadOnlyList<GpuDevice> devices = [];
        try
        {
            if (_executable() is { } path && File.Exists(path)) devices = Parse(Run(path));
        }
        catch (Exception e)
        {
            Log.Warning($"GPU device probe failed: {e.Message}");
        }

        lock (_gate)
        {
            _devices = devices;
            _probedAt = Stopwatch.GetTimestamp();
            _probing = null;
        }
    }

    private static string Run(string path)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--list-devices --offline",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(ProbeTimeout))
        {
            process.Kill();
            return "";
        }

        return stdout.Result + "\n" + stderr.Result;
    }

    [GeneratedRegex(@"^\s*(?<id>[^\s:]+): (?<name>.+) \((?<total>\d+) MiB, (?<free>\d+) MiB free\)\s*$",
        RegexOptions.Multiline)]
    private static partial Regex DeviceLine();
}
