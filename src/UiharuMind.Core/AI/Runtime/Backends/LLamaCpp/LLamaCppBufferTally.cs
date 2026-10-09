using System.Globalization;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// 从 llama-server 加载日志里累计各设备的缓冲（权重、KV、计算），形如
/// <c>load_tensors:  CUDA0 model buffer size =  2375.91 MiB</c>。独立显卡的显存不算进进程内存，实际占了多少只有这里看得到
/// </summary>
public sealed partial class LLamaCppBufferTally
{
    private const double MiB = 1024d * 1024d;

    private readonly Lock _gate = new();
    private double _deviceMiB;
    private bool _seenDevice;

    /// <summary>
    /// 显卡上的缓冲合计（字节）；还没看到或全在 CPU 上为 null
    /// </summary>
    public long? DeviceBytes
    {
        get
        {
            lock (_gate) return _seenDevice ? (long)(_deviceMiB * MiB) : null;
        }
    }

    /// <summary>
    /// 看一行输出
    /// </summary>
    /// <param name="line">日志行</param>
    public void Observe(string line)
    {
        if (!line.Contains("buffer size", StringComparison.Ordinal)) return;
        Match match = BufferLine().Match(line);
        if (!match.Success || !IsDevice(match.Groups["buffer"].Value)) return;
        double mib = double.Parse(match.Groups["mib"].Value, CultureInfo.InvariantCulture);
        lock (_gate)
        {
            _deviceMiB += mib;
            _seenDevice = true;
        }
    }

    // 设备缓冲名是后端名加序号（CUDA0、ROCm0、Vulkan0、MTL0，映射的带 _Mapped）；CPU、CPU_Mapped、CUDA_Host 都在内存里
    private static bool IsDevice(string buffer) =>
        DeviceBuffer().IsMatch(buffer.EndsWith("_Mapped", StringComparison.Ordinal) ? buffer[..^7] : buffer);

    [GeneratedRegex(@"(?<buffer>\S+)\s+(?:\S+\s+)?buffer size\s*=\s*(?<mib>[\d.]+) MiB")]
    private static partial Regex BufferLine();

    [GeneratedRegex(@"^[A-Za-z]+\d+$")]
    private static partial Regex DeviceBuffer();
}
