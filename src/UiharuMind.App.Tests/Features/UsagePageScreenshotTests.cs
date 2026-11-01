using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Features.Models.Usage;

namespace UiharuMind.App.Tests.Features;

/// <summary>
/// 用量页的验收截图：无头真 Skia 渲染，把整页截成 PNG 供人看。
///
/// <para>
/// 输出目录由 <c>USAGE_SHOTS_DIR</c> 指定（不设时落系统临时目录）。看的是四件事：
/// 布局有没有挤/错位、KPI 卡间距、图表与明细对齐、中文有没有豆腐块；另外深/浅色与窄窗口各一张。
/// </para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class UsagePageScreenshotTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-usage-shots-{Guid.NewGuid():N}");
    private Window? _window;
    private UsageJournal? _journal;

    public void Dispose()
    {
        HeadlessUi.Run(() => _window?.Close());
        _journal?.Dispose();
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            //截图已落在目标目录，临时库删不掉不影响结论
        }
    }

    [Fact]
    public void UsagePage_CapturesLightDarkAndNarrow()
    {
        HeadlessUi.Run(() =>
        {
            UsageChartSetup.EnsureConfigured();
            string outDir = ShotsDir();
            Directory.CreateDirectory(outDir);

            //一份样本一个库：四张截图共用它，否则每张��会再种一遍，数字对不上
            string path = Path.Combine(_directory, "Usage.db");
            Seed(path);
            _journal = new UsageJournal(path);
            //样本库留一份在输出目录：给真实窗口拍图、手动看界面时直接用
            File.Copy(path, Path.Combine(outDir, "sample-usage.db"), overwrite: true);

            string light = CaptureAt(outDir, "usage-light", ThemeVariant.Light, 1280, 1700);
            string dark = CaptureAt(outDir, "usage-dark", ThemeVariant.Dark, 1280, 1700);
            //窄窗口：验证明细表列不挤爆、筛选栏换行、KPI 不被挤扁
            string narrow = CaptureAt(outDir, "usage-narrow", ThemeVariant.Light, 900, 1700);
            //会话视图：命中率时间线 + 骤降点
            string sessionShot = CaptureAt(outDir, "usage-session", ThemeVariant.Light, 1280, 1000, "sess-drop");

            Assert.True(new FileInfo(light).Length > 20_000, $"浅色截图太小，可能没渲染出内容：{light}");
            Assert.True(new FileInfo(dark).Length > 20_000, $"深色截图太小：{dark}");
            Assert.True(new FileInfo(narrow).Length > 20_000, $"窄窗口截图太小：{narrow}");
            Assert.True(new FileInfo(sessionShot).Length > 20_000, $"会话视图截图太小：{sessionShot}");
        });
    }

    /// <summary>
    /// 一份样本、一个主题、一次截图。
    ///
    /// <b>图表必须在「建窗口之前」就有最终 series</b>：LiveCharts 的重绘挂在
    /// <c>CompositionTarget.Rendering</c> 上，无头会话不推这一拍，建好之后再换 series 画不出来。
    /// 所以每次截图都重新造一份页面，把主题与会话筛选先定好再挂控件
    /// </summary>
    private string CaptureAt(string outDir, string name, ThemeVariant variant, double width, double height,
        string? session = null)
    {
        UseTheme(variant);
        UsagePageData data = OpenPage();
        Assert.False(data.IsEmpty); //截图必须真的有一页数据，否则看不出排版问题
        Assert.Equal(6, data.Kpis.Count);

        if (session != null)
        {
            data.SessionOption = data.SessionOptions.First(x => x.Value == session);
            Pump(1200);
            Assert.True(data.IsSessionView);
        }

        Window window = Host(data, width, height);
        _window = window;
        Pump(900); //等 LiveCharts 把这一帧画完
        string path = Path.Combine(outDir, name + ".png");
        Capture(window, path);
        window.Close();
        _window = null;
        return path;
    }

    private UsagePageData OpenPage()
    {
        UsagePageData data = new(null, _journal!, null);
        data.OnEnable();
        Pump(800);
        return data;
    }

    private static Window Host(UsagePageData data, double width, double height)
    {
        Window window = new()
        {
            Width = width,
            Height = height,
            DataContext = data,
            Content = data.View,
            Background = Application.Current?.FindResource("SemiGrey0") as Avalonia.Media.IBrush,
        };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void UseTheme(ThemeVariant variant)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = variant;
        Dispatcher.UIThread.RunJobs();
    }

    private static void Pump(int milliseconds)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(16);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using Bitmap frame = HeadlessWindowExtensions.CaptureRenderedFrame(window)
                             ?? throw new InvalidOperationException($"CaptureRenderedFrame 返回空帧：{path}");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static string ShotsDir()
    {
        string? dir = Environment.GetEnvironmentVariable("USAGE_SHOTS_DIR");
        return string.IsNullOrWhiteSpace(dir) ? Path.Combine(Path.GetTempPath(), "uiharu-usage-shots") : dir;
    }

    /// <summary>造一份看得过去的样本：三家供应商、六个模型、七天、若干失败与命中率骤降</summary>
    private static void Seed(string path)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        using UsageStore store = new(path);
        (string Model, string Provider, bool Local, decimal Input, decimal Output, decimal CacheRead)[] models =
        [
            ("Deepseek-V4-Flash-A", "sensenova", false, 0.15m, 0.6m, 0.003m),
            ("CommandCode-Claude", "commandcode", false, 3m, 15m, 0.3m),
            ("Agnes-3.0-Flash", "agnes", false, 0.45m, 0.9m, 0.0038m),
            ("Qwen-3.8-Flash", "commandcode", false, 0.16m, 0.47m, 0.016m),
            ("MiniMax-M3", "commandcode", false, 0.3m, 1.2m, 0.06m),
            ("qwen-local-7b", "", true, 0m, 0m, 0m),
        ];

        for (int day = 6; day >= 0; day--)
        {
            for (int i = 0; i < 18; i++)
            {
                (string model, string provider, bool local, decimal input, decimal output, decimal cache) =
                    models[(day * 3 + i) % models.Length];
                DateTimeOffset ts = now.AddDays(-day).AddHours(-(i % 12)).AddMinutes(i * 3);
                long inputTokens = 4_000 + (i * 917 + day * 1_231) % 26_000;
                long cached = i % 5 == 0 ? inputTokens / 3 : i % 7 == 0 ? 0 : inputTokens * 4 / 5;
                long outputTokens = 180 + (i * 53 + day * 37) % 900;
                bool ok = (i + day) % 23 != 0 && (i + day) % 31 != 0;
                string? kind = ok ? null : (i + day) % 31 == 0 ? UsageErrorKinds.Timeout : UsageErrorKinds.Http;
                int? status = kind == UsageErrorKinds.Http ? 502 : null;
                decimal? cost = local
                    ? null
                    : (inputTokens - cached) * input / 1_000_000m + cached * cache / 1_000_000m +
                      outputTokens * output / 1_000_000m;
                store.Insert(new UsageRecord(
                    ts.ToUnixTimeMilliseconds(), ts.AddMilliseconds(-(600 + i * 37)).ToUnixTimeMilliseconds(),
                    $"sess-{day}-{i % 4}",
                    model, model, local ? "local" : "RemoteTestConfig", provider, local ? "" : "https://api.example.com",
                    local, inputTokens, cached, null, outputTokens, outputTokens / 3, ok ? 1_200 + i * 90 : null,
                    ok ? 320 + i * 20 : null, ok, kind, ok ? null : "upstream returned a gateway error",
                    status, i % 9 == 0 ? 1 : 0, cost, cost == null ? null : UsagePricing.SourceOfficial,
                    local ? null : input, local ? null : output, local ? null : cache, 1m));
            }
        }

        //一条用户取消：错误率里要排除
        store.Insert(new UsageRecord(now.ToUnixTimeMilliseconds(), now.AddSeconds(-2).ToUnixTimeMilliseconds(),
            "sess-drop", "Deepseek-V4-Flash-A", "Deepseek-V4-Flash-A", "RemoteTestConfig", "sensenova",
            "https://api.example.com", false, 6_000, 0, null, 0, 0, null, 200, false, UsageErrorKinds.Cancel,
            "user stopped", null, 0, null, null, null, null, null, 1m));

        //一个干净的会话样本：前 40 发几乎全命中，后几发骤降到 0 —— 命中率时间线上只有一个拐点
        for (int i = 0; i < 48; i++)
        {
            DateTimeOffset ts = now.AddHours(-8).AddMinutes(i * 3);
            long input = 12_000 + i * 260;
            bool hit = i < 40;
            long cached = hit ? input * 9 / 10 : 0;
            store.Insert(new UsageRecord(ts.ToUnixTimeMilliseconds(), ts.AddSeconds(-3).ToUnixTimeMilliseconds(),
                "sess-drop", "Deepseek-V4-Flash-A", "Deepseek-V4-Flash-A", "RemoteSensenovaModelConfig", "sensenova",
                "https://token.sensenova.cn", false, input, cached, null, 380 + i * 4, 90, hit ? 1_600 : 2_400,
                hit ? 410 : 780, true, null, null, null, 0, 0.0007m, UsagePricing.SourceOfficial, 0.15m, 0.6m,
                0.003m, 1m));
        }
    }
}
