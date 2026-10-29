using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Status;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Models.Usage;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 供应商额度读数的<b>位置</b>：它挂在<b>会话头</b>里、模型名的右边——
/// 它报的是「这个会话用的那家供应商」的额度，所以必须挨着那个模型名；
/// 挨着顶栏那个全局模型下拉就是名不副实（那个下拉是全局选择，与本会话未必是一家）。
///
/// 这里量的不是「控件有没有显示」而是「它在不在会话头里、有没有把模型名挤走」。
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ProviderStatusTopBarTests
{
    private const double MaxReadoutHeight = 18;

    [Fact]
    public void ConversationHeader_CarriesTheIndicator() => HeadlessUi.Run(() =>
    {
        ConversationView view = Show(out Window window);

        // 会话头里有且只有一个读数控件
        ProviderStatusIndicator[] indicators = view.GetVisualDescendants().OfType<ProviderStatusIndicator>().ToArray();
        Assert.Single(indicators);

        window.Close();
    });

    [Fact]
    public void WithoutData_IndicatorTakesNoSpace() => HeadlessUi.Run(() =>
    {
        ConversationView view = Show(out Window window);

        ProviderStatusIndicator indicator = view.GetVisualDescendants().OfType<ProviderStatusIndicator>().Single();
        Assert.False(indicator.FindControl<Button>("Trigger")!.IsVisible);
        Assert.Equal(0, indicator.Bounds.Width, 1); // 没数据就不占位

        window.Close();
    });

    [Fact]
    public void WithData_ReadoutStaysOnOneLine() => HeadlessUi.Run(() =>
    {
        ConversationView view = Show(out Window window);

        ProviderStatusIndicator indicator = view.GetVisualDescendants().OfType<ProviderStatusIndicator>().Single();
        indicator.Data = CreateData();
        Dispatcher.UIThread.RunJobs();
        view.UpdateLayout();

        Assert.True(indicator.FindControl<Button>("Trigger")!.IsVisible);
        TextBlock readout = indicator.FindControl<TextBlock>("Readout")!;
        Assert.True(readout.Bounds.Height <= MaxReadoutHeight, $"读数换行了：{readout.Bounds.Height}");
        Capture(window, "provider-status-header");

        window.Close();
    });

    private static void Capture(Window window, string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        window.UpdateLayout();
        using Avalonia.Media.Imaging.Bitmap frame =
            Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
            ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static ConversationView Show(out Window window)
    {
        ConversationView view = new();
        window = new Window { Width = 900, Height = 200, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        view.UpdateLayout();
        return view;
    }

    [Fact]
    public void OpeningThePanel_FetchesFreshData() => HeadlessUi.Run(() =>
    {
        ConversationView view = Show(out Window window);
        ProviderStatusIndicator indicator = view.GetVisualDescendants().OfType<ProviderStatusIndicator>().Single();

        StubProviderStatusSource source = new() { Clock = () => DateTimeOffset.Now };
        source.SnapshotFactory = now => new ProviderStatusSnapshot("Command Code",
            [new ProviderUsageMeter(EProviderMeterSlot.FiveHour, 1, 10, "$", null, false)], null, [], now);
        ProviderStatusService status = new(
            new StubProviderStatusRegistry(typeof(RemoteCommandCodeModelConfig), source), () => DateTimeOffset.Now);
        indicator.Data = new ProviderStatusViewData(
            () => new ProviderStatusContext(typeof(RemoteCommandCodeModelConfig), "https://api.commandcode.ai",
                "key-a", "deepseek-v4-flash", "DeepSeek V4 Flash"),
            status, () => DateTimeOffset.Now, action => action());

        Button trigger = indicator.FindControl<Button>("Trigger")!;
        trigger.Flyout!.ShowAt(trigger);
        Dispatcher.UIThread.RunJobs();

        // 点开明细就拉一次：面板里的数字得是此刻的
        Assert.True(source.Calls >= 1, $"点开没有触发取数（calls={source.Calls}）");

        trigger.Flyout.Hide();
        window.Close();
    });

    /// <summary>
    /// 顶栏读数后面跟一段缓存读单价——长会话里真正吃钱的是缓存命中那一档，
    /// 输入/输出留在明细面板里。价格不能把「5h 32% · 周 41%」挤换行。
    /// </summary>
    [Fact]
    public void WithPricedModel_CacheReadPriceFollowsReadout() => HeadlessUi.Run(() =>
    {
        ConversationView view = Show(out Window window);
        ProviderStatusIndicator indicator = view.GetVisualDescendants().OfType<ProviderStatusIndicator>().Single();
        indicator.Data = CreateData("deepseek/deepseek-v4.1-flash");
        Dispatcher.UIThread.RunJobs();
        view.UpdateLayout();

        TextBlock cache = indicator.FindControl<TextBlock>("CachePrice")!;
        Assert.True(cache.IsVisible);
        Assert.Contains("$0.003", cache.Text);

        TextBlock readout = indicator.FindControl<TextBlock>("Readout")!;
        Assert.True(readout.Bounds.Height <= MaxReadoutHeight, $"读数换行了：{readout.Bounds.Height}");
        Capture(window, "provider-status-cache-price");

        window.Close();
    });

    private static ProviderStatusViewData CreateData(string modelId = "deepseek-v4-flash")
    {
        DateTimeOffset now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        StubProviderStatusSource source = new()
        {
            Clock = () => now,
            SnapshotFactory = _ => new ProviderStatusSnapshot("Command Code",
                [
                    new ProviderUsageMeter(EProviderMeterSlot.FiveHour, 3.2, 10, "$", now.AddHours(3), false),
                    new ProviderUsageMeter(EProviderMeterSlot.Weekly, 41, 100, "$", now.AddDays(2), false),
                ],
                new ProviderBalance(12.4, "$", "Pro"), [], now)
        };
        ProviderStatusService status = new(
            new StubProviderStatusRegistry(typeof(RemoteCommandCodeModelConfig), source), () => now);
        ProviderStatusViewData data = new(
            () => new ProviderStatusContext(typeof(RemoteCommandCodeModelConfig), "https://api.commandcode.ai",
                "key-a", modelId, "DeepSeek V4 Flash"),
            status, () => now, action => action());
        data.Pump();
        return data;
    }
}
