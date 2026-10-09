using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.Tests.Core;

/// <summary>
/// 读进程占用：macOS 取物理占用（活动监视器那一列），不在的进程为 null
/// </summary>
public class ProcessUsageTests
{
    [Fact]
    public void Sample_OwnProcess_HasMemoryAndCpu()
    {
        ProcessUsageSample? sample = ProcessUsage.TrySample(Environment.ProcessId);

        Assert.True(sample is { MemoryBytes: > 1 << 20 }, $"{sample}");
        Assert.True(sample!.Value.CpuTime > TimeSpan.Zero);
    }

    [Fact]
    public void Sample_MissingProcess_IsNull()
    {
        Assert.Null(ProcessUsage.TrySample(int.MaxValue));
    }
}
