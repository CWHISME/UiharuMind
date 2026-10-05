using System.Diagnostics;
using System.Diagnostics.Tracing;
using UiharuMind.Core.Core.SimpleLog;

namespace HttpAllocBench;

/// <summary>每项先预热三次，再按 GC.GetTotalAllocatedBytes(precise) 前后差量平均分配；日志写线程的分配也算在内</summary>
internal static class Measure
{
    public static (double Bytes, double Micros, double LohBytes) Run(AllocListener alloc, int iterations, Action body)
    {
        for (int i = 0; i < Math.Min(3, iterations); i++) body();
        LogManager.Instance.Flush();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Thread.Sleep(200);
        alloc.Reset();
        long before = GC.GetTotalAllocatedBytes(true);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) body();
        double micros = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
        LogManager.Instance.Flush();
        long after = GC.GetTotalAllocatedBytes(true);
        Thread.Sleep(300); //AllocationTick 事件异步派发，等它到齐
        return ((after - before) / (double)iterations, micros, alloc.Large / (double)iterations);
    }

    public static string Kb(double bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024 / 1024:0.00} MB"
        : $"{bytes / 1024:0.0} KB";
}

/// <summary>按 GC AllocationTick 采样估算大对象堆分配（约每 100KB 一次采样，只看量级）</summary>
internal sealed class AllocListener : EventListener
{
    private long _large;

    public long Large => Interlocked.Read(ref _large);

    public void Reset() => Interlocked.Exchange(ref _large, 0);

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal)) return;
        if (e.PayloadNames == null || e.Payload == null) return;
        int kindIndex = e.PayloadNames.IndexOf("AllocationKind");
        int amountIndex = e.PayloadNames.IndexOf("AllocationAmount64");
        if (kindIndex < 0 || amountIndex < 0 || Convert.ToUInt32(e.Payload[kindIndex]) != 1) return;
        Interlocked.Add(ref _large, Convert.ToInt64(e.Payload[amountIndex]));
    }
}
