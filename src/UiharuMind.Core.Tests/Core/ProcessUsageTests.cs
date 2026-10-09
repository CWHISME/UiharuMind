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

/// <summary>
/// 整机 CPU：头一回没有比较对象为 null，之后给出 0~100
/// </summary>
public class SystemCpuMeterTests
{
    [Fact]
    public async Task Sample_SecondTime_IsPercent()
    {
        SystemCpuMeter meter = new();

        Assert.Null(meter.Sample());
        // 转一会儿，让两次之间的 tick 一定有变化
        long until = Environment.TickCount64 + 200;
        while (Environment.TickCount64 < until) await Task.Yield();
        double? percent = meter.Sample();

        Assert.True(percent is >= 0 and <= 100, $"{percent}");
    }
}

/// <summary>
/// 系统内存压力：macOS 读得到内核的压力档，压缩与交换区不为负
/// </summary>
public class SystemMemoryStatusTests
{
    [Fact]
    public void Read_OnMac_HasPressureLevel()
    {
        SystemMemoryStatus status = SystemMemoryStatusReader.Read();

        Assert.True(status.CompressedBytes >= 0 && status.SwapUsedBytes >= 0, $"{status}");
        if (OperatingSystem.IsMacOS()) Assert.NotEqual(ESystemMemoryPressure.Unknown, status.Pressure);
    }
}
