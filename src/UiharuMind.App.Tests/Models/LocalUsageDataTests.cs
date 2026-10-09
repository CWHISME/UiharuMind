using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.Process;
using UiharuMind.Features.Models.Usage;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 本机资源一览：整机余量与 CPU 的读数、余量分档，各本地模型一行（加载中给几成、在跑给占用），停一个立刻重取
/// </summary>
public class LocalUsageDataTests
{
    private const long Gib = 1L << 30;

    [Theory]
    [InlineData(8, 16, ESystemMemoryPressure.Normal)]
    [InlineData(3, 16, ESystemMemoryPressure.Warning)]
    [InlineData(1, 16, ESystemMemoryPressure.Critical)]
    [InlineData(1, 0, ESystemMemoryPressure.Normal)]
    public void Classify_ByShareOfTotal(long availableGb, long totalGb, ESystemMemoryPressure expected)
    {
        Assert.Equal(expected, LocalUsageData.Classify(availableGb * Gib, totalGb * Gib));
    }

    [Fact]
    public async Task Refresh_FillsMachineAndRows()
    {
        int released = 0;
        List<LocalEngineEntry> entries =
        [
            new(ELocalEngineKind.Chat, "qwen", null, () => new EngineUsage(5 * Gib, 3.4), () => released++),
            new(ELocalEngineKind.Image, "image", 0.62f, () => throw new InvalidOperationException("加载中不该取"),
                () => { })
        ];
        LocalUsageData data = new(() => entries, () => Device(3, 16), () => 23.4);

        await data.RefreshAsync();

        Assert.Equal(Loc.Text(LangKey.LocalUsageSummary, "3.0", "16.0", 23), data.Summary);
        Assert.Equal(ESystemMemoryPressure.Warning, data.Pressure);
        Assert.Equal("Warning", data.PressureTag);
        Assert.True(data.IsMemoryLow);
        Assert.Equal(2, data.Rows.Count);
        Assert.Equal(Loc.Text(LangKey.EngineStatusUsage, "5.0", 3), data.Rows[0].StateText);
        Assert.Equal(Loc.Text(LangKey.LocalUsageLoading, 62), data.Rows[1].StateText);
        Assert.Same(data.Rows[0], data.RowOf("qwen"));
        Assert.Null(data.RowOf("image"));

        entries.RemoveAt(0);
        data.Rows[0].StopCommand.Execute(null);
        await WaitUntil(() => data.Rows.Count == 1);
        Assert.Equal(1, released);
    }

    [Fact]
    public async Task SystemPressure_WinsOverFreeShare_AndShowsSwap()
    {
        // 余量看着有一半，但系统已经在换页
        LocalUsageData data = new(() => [], () => Device(8, 16), () => null,
            () => new SystemMemoryStatus(ESystemMemoryPressure.Warning, 7 * Gib, 9 * Gib));

        await data.RefreshAsync();

        Assert.Equal(ESystemMemoryPressure.Warning, data.Pressure);
        Assert.True(data.IsMemoryLow);
        Assert.Equal(Loc.Text(LangKey.LocalUsageSwapLine, "7.0", "9.0"), data.SwapLine);
    }

    [Fact]
    public async Task Refresh_RightAfterAnother_IsSkippedUnlessForced()
    {
        int captures = 0;
        LocalUsageData data = new(() => [], () =>
        {
            captures++;
            return Device(8, 16);
        }, () => null);

        await data.RefreshAsync();
        await data.RefreshAsync();
        Assert.Equal(1, captures);
        Assert.Equal(Loc.Text(LangKey.LocalUsageSummaryMemory, "8.0", "16.0"), data.Summary);

        await data.RefreshAsync(true);
        Assert.Equal(2, captures);
    }

    private static RuntimeDeviceInfo Device(long availableGb, long totalGb) =>
        new(totalGb * Gib, availableGb * Gib, 0, 0, 0, "", "", "", DateTimeOffset.Now);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
