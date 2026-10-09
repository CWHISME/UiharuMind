using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Features.Models.Usage;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 资源读数：默认显示整机余量；只看某个对话模型时，它在跑才露出、读数是它自己的占用
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class LocalUsageIndicatorTests
{
    private const long Gib = 1L << 30;

    [Fact]
    public void Machine_ShowsSummary_Model_FallsBackToMachineWhenNotRunning() => HeadlessUi.RunAsync(async () =>
    {
        List<LocalEngineEntry> entries =
            [new(ELocalEngineKind.Chat, "qwen", null, () => new EngineUsage(5 * Gib, 3), () => { })];
        LocalUsageData data = new(() => entries,
            () => new RuntimeDeviceInfo(16 * Gib, 9 * Gib, 0, 0, 0, "", "", "", DateTimeOffset.Now), () => 12);
        LocalUsageIndicator machine = new() { Data = data };
        LocalUsageIndicator model = new() { Data = data, ShowsModel = true, ModelName = "qwen" };
        LocalUsageIndicator other = new() { Data = data, ShowsModel = true, ModelName = "remote" };
        Window window = new()
        {
            Width = 420, Height = 120,
            Content = new StackPanel { Spacing = 6, Children = { machine, model, other } }
        };
        window.Show();
        await data.RefreshAsync(true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Loc.Text(LangKey.LocalUsageSummary, "9.0", "16.0", 12), Readout(machine).Text);
        Assert.Equal(Loc.Text(LangKey.LocalUsageModelOfTotalMemory, "5.0", "16.0"), Readout(model).Text);
        Assert.Equal(Readout(machine).Text, Readout(other).Text);
        Capture(window, "local-usage-indicator");

        // 点开是明细：整机一段、每个本地模型一行带停止
        Button trigger = machine.FindControl<Button>("Trigger")!;
        trigger.Flyout!.ShowAt(trigger);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(data, ((LocalUsagePanel)((Flyout)trigger.Flyout).Content!).DataContext);
        trigger.Flyout.Hide();
        Window detail = new() { Width = 400, Height = 260, Content = new LocalUsagePanel { DataContext = data } };
        detail.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(detail.GetVisualDescendants().OfType<Button>());
        Capture(detail, "local-usage-panel");
        detail.Close();

        entries.Clear();
        await data.RefreshAsync(true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(model.FindControl<Button>("Trigger")!.IsVisible);
        Assert.Equal(Readout(machine).Text, Readout(model).Text);
        window.Close();
    });

    private static TextBlock Readout(LocalUsageIndicator indicator) => indicator.FindControl<TextBlock>("Readout")!;

    private static void Capture(Window window, string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        window.UpdateLayout();
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
                             ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
