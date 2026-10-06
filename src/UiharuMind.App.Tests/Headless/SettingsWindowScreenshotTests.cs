using Avalonia;
using UiharuMind.Core.Configs;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Features.About;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 设置模块验收截图：无头真 Skia 渲染下，把设置窗口每一页各截一张 PNG。
///
/// 输出目录由环境变量 <c>SETTINGS_SHOTS_DIR</c> 指定（验收时 before/after 各跑一次，
/// 基线放 before、改完放 after），未设置时落到系统临时目录，测试本身只保证「能渲染、能出图」。
///
/// 组装走注入路径：无头 App 下 <c>App.ViewModel</c> 是 null，页数据与取页路径由这里自备，
/// 生产路径不变（仍是 App.ViewModel 那份缓存）。切页走真实按钮点击，不碰私有方法。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SettingsWindowScreenshotTests : IDisposable
{
    private SettingsWindow? _window;

    public void Dispose() => HeadlessUi.Run(() => _window?.Close());

    [Fact]
    public void EverySettingsPage_CapturesRenderedPng()
    {
        HeadlessUi.Run(() =>
        {
            string outDir = SettingsShotsDir();
            Directory.CreateDirectory(outDir);

            SettingsWindow window = BuildWindow();
            _window = window;
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Capture(window, outDir, "general");

            ClickAndCapture(window, "RuntimeButton", outDir, "runtime");
            AssertRuntimeSliderThumbsVisible(window);
            ClickAndCapture(window, "AgentButton", outDir, "agent");
            ClickAndCapture(window, "QuickToolButton", outDir, "quicktool");
            ClickAndCapture(window, "ShortcutsButton", outDir, "shortcut");
            ClickAndCapture(window, "HelpButton", outDir, "help");
            ClickAndCapture(window, "AboutButton", outDir, "about");
            ScrollAboutToBottomAndCapture(window, outDir);
        });
    }

    [Fact]
    public void JumpToDownloadSource_ScrollsSectionIntoView()
    {
        HeadlessUi.Run(() =>
        {
            SettingsWindow window = BuildWindow();
            _window = window;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ShowDownloadSourceSettings();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            DownloadSourceSettingsView section = window.GetVisualDescendants().OfType<DownloadSourceSettingsView>().Single();
            Point? topLeft = section.TranslatePoint(new Point(0, 0), window);
            Assert.True(topLeft is { } p && p.Y >= 0 && p.Y < window.Bounds.Height, $"下载源一节不在视野内：{topLeft}");
            Capture(window, SettingsShotsDir(), "runtime-download-source");

            // 滚动位置是全局记忆（关窗时也会记一次），先关窗再清，不然同类里后跑的截图从半页开始
            window.Close();
            _window = null;
            ConfigManager.Instance.Setting.SettingsScrollPositions.Remove("RuntimeEngineSetting");
        });
    }

    private static SettingsWindow BuildWindow()
    {
        RecordingMessageService messages = new();
        ApplicationUpdateService updateService = new() { HasChecked = true }; // 跳过联网检查，测试不碰网络
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IMessageService>(messages)
            .AddSingleton(updateService)
            .BuildServiceProvider();

        return new SettingsWindow(
            ActivatorUtilities.CreateInstance<GeneralSettingViewModel>(services),
            new RuntimeEngineSettingData(messages),
            new ShortcutSettingViewModel(),
            GetPageForHeadless);
    }

    private static void ClickAndCapture(SettingsWindow window, string buttonName, string outDir, string pageName)
    {
        Button button = window.FindControl<Button>(buttonName)
            ?? throw new InvalidOperationException($"找不到导航按钮 {buttonName}");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Capture(window, outDir, pageName);
    }

    private static void AssertRuntimeSliderThumbsVisible(SettingsWindow window)
    {
        // Aico 像素级打回：轨道在但 Thumb 没外观，图上只剩 4px 灰条。钉死：轨道行里必须有 >4px 的色块
        List<Slider> sliders = window.GetVisualDescendants().OfType<Slider>().ToList();
        Assert.True(sliders.Count == 2, "Runtime 页应有 2 个 Slider（上下文长度 / GPU 层数）");

        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
            ?? throw new InvalidOperationException("CaptureRenderedFrame 返回空帧（Runtime 滑块检查）");
        int stride = frame.PixelSize.Width * 4;
        byte[] pixels = new byte[stride * frame.PixelSize.Height];
        System.Runtime.InteropServices.GCHandle handle =
            System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height),
                handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        for (int i = 0; i < sliders.Count; i++)
        {
            Slider slider = sliders[i];
            Point topLeft = slider.TranslatePoint(new Point(0, 0), window)
                ?? throw new InvalidOperationException($"Slider[{i}] 不在窗口坐标内");
            int x = (int)topLeft.X;
            int y = (int)topLeft.Y;
            int w = (int)slider.Bounds.Width;
            int h = (int)slider.Bounds.Height;
            int maxRun = MaxVerticalContentRun(pixels, frame.PixelSize.Width, x, y, w, h);
            Assert.True(maxRun > 4,
                $"Runtime Slider[{i}] 轨道行最高色块 {maxRun}px，应有 >4px（Thumb 没外观时只剩 4px 轨道）");
        }
    }

    // 在 (x,y,w,h) 区域内量「与背景主色不同的像素」的纵向最长连续块；轨道 4px、Thumb 16px 时该值约 16
    private static int MaxVerticalContentRun(byte[] pixels, int width, int x, int y, int w, int h)
    {
        int stride = width * 4;
        // 背景主色：区域内 2px 采样众数（页面浅灰背景占绝大多数）
        Dictionary<int, int> counts = new();
        for (int yy = y; yy < y + h; yy += 2)
        for (int xx = x; xx < x + w; xx += 2)
        {
            int off = yy * stride + xx * 4;
            int key = (pixels[off] << 16) | (pixels[off + 1] << 8) | pixels[off + 2];
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        int bg = counts.MaxBy(kv => kv.Value).Key;
        int br = (bg >> 16) & 0xFF, bgG = (bg >> 8) & 0xFF, bb = bg & 0xFF;

        int best = 0;
        for (int xx = x; xx < x + w; xx++)
        {
            int run = 0;
            for (int yy = y; yy < y + h; yy++)
            {
                int off = yy * stride + xx * 4;
                int diff = Math.Abs(pixels[off] - br) + Math.Abs(pixels[off + 1] - bgG) + Math.Abs(pixels[off + 2] - bb);
                if (diff > 90)
                {
                    run++;
                    best = Math.Max(best, run);
                }
                else
                {
                    run = 0;
                }
            }
        }

        return best;
    }

    private static void Capture(SettingsWindow window, string outDir, string pageName)
    {
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
            ?? throw new InvalidOperationException($"CaptureRenderedFrame 返回空帧：{pageName}");
        Assert.True(frame.PixelSize.Width >= 760 && frame.PixelSize.Height >= 520,
            $"截图尺寸异常：{pageName} {frame.PixelSize}");

        string path = Path.Combine(outDir, $"{pageName}.png");
        frame.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Assert.True(new FileInfo(path).Length > 0, $"截图落盘为空：{path}");
    }

    /// <summary>
    /// About 页内容包在唯一的 ScrollViewer 里，顶部视口露半行是「下面还有」的正常信号（Aico 口径）；
    /// 滚到底、最后一条完整可见、Offset 顶到最大，才证明不是真截断。产出 about-bottom.png。
    /// </summary>
    private static void ScrollAboutToBottomAndCapture(SettingsWindow window, string outDir)
    {
        // 外壳里可能另有 ScrollViewer（如搜索框 TextBox 模板自带的），按祖先链锁定 About 页内容所在的那个
        List<ScrollViewer> aboutScrollers = window.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Where(s => s.GetVisualAncestors().OfType<AboutPage>().Any())
            .ToList();
        Assert.True(aboutScrollers.Count == 1, "About 页内容应恰有一个 ScrollViewer");
        ScrollViewer scroller = aboutScrollers[0];

        double maxY = MaxVerticalOffset(scroller);
        scroller.Offset = new Vector(0, maxY);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        maxY = MaxVerticalOffset(scroller);
        scroller.Offset = new Vector(0, maxY);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.True(scroller.Offset.Y >= maxY - 1,
            $"About 应能滚到底（Offset {scroller.Offset.Y} vs 最大 {maxY}）");
        Capture(window, outDir, "about-bottom");
    }

    // Avalonia 12 的 ScrollViewer 没有 MaximumOffset；垂直最大偏移 = Extent - Viewport，内容矮于视口时为 0
    private static double MaxVerticalOffset(ScrollViewer scroller) =>
        Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);

    // 无头下取页路径由测试提供；SettingsWindow 只取 MenuHelpKey，Help 页可真实构造（无 App 依赖）
    private static PageDataBase GetPageForHeadless(MenuPages key)
    {
        Assert.Equal(MenuPages.MenuHelpKey, key);
        return new HelpPageData();
    }

    private static string SettingsShotsDir()
    {
        string? dir = Environment.GetEnvironmentVariable("SETTINGS_SHOTS_DIR");
        return string.IsNullOrWhiteSpace(dir) ? Path.Combine(Path.GetTempPath(), "uiharu-settings-shots") : dir;
    }
}
