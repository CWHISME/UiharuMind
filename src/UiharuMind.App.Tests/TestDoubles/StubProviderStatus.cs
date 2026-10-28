using System;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Status;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 供应商取数的替身：不连网。快照的 <c>FetchedAt</c> 按取数时刻盖章——TTL 与「数据几秒前」
/// 都按调用方给的时钟走，测试才不会跟着真时钟漂。
/// </summary>
internal sealed class StubProviderStatusSource : IProviderStatusSource
{
    private int _calls;
    private int _summaryCalls;

    /// <summary>取数次数</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>本周期统计的取数次数</summary>
    public int SummaryCalls => Volatile.Read(ref _summaryCalls);

    /// <summary>按取数时刻造快照；返回 null 表示「这家没数据」</summary>
    public Func<DateTimeOffset, ProviderStatusSnapshot?>? SnapshotFactory { get; set; }

    /// <summary>按取数时刻造本周期统计；不给表示这家没有统计接口</summary>
    public Func<DateTimeOffset, ProviderUsageSummary?>? SummaryFactory { get; set; }

    /// <summary>时钟</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

    /// <inheritdoc />
    public Task<ProviderStatusSnapshot?> FetchAsync(ProviderStatusContext context, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        ProviderStatusSnapshot? snapshot = SnapshotFactory?.Invoke(Clock());
        return Task.FromResult(snapshot is null ? null : snapshot with { FetchedAt = Clock() });
    }

    /// <inheritdoc />
    public Task<ProviderUsageSummary?> FetchSummaryAsync(ProviderStatusContext context, CancellationToken ct)
    {
        Interlocked.Increment(ref _summaryCalls);
        return Task.FromResult(SummaryFactory?.Invoke(Clock()));
    }
}

/// <summary>
/// 只认一个供应商类型的注册表：其余一律未登记（顶栏据此整块不显示）
/// </summary>
/// <param name="configType">接入的那家</param>
/// <param name="source">它的取数源</param>
internal sealed class StubProviderStatusRegistry(Type configType, IProviderStatusSource? source)
    : IProviderStatusRegistry
{
    /// <inheritdoc />
    public ProviderStatusRegistration? For(Type type) =>
        type == configType ? new ProviderStatusRegistration(source, null) : null;
}

/// <summary>测试时钟：读它、拨它</summary>
internal sealed class TestClock(DateTimeOffset start)
{
    private DateTimeOffset _now = start;

    /// <summary>当前时刻</summary>
    public DateTimeOffset Read() => _now;

    /// <summary>往前走</summary>
    /// <param name="span">跨度</param>
    public void Advance(TimeSpan span) => _now = _now.Add(span);
}
