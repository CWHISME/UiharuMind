using UiharuMind.Core.AI.Runtime.Backends;
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
using UiharuMind.Shared.Controls;
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
            AssertRuntimeSectionsRendered(window);
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

    [Fact]
    public void EngineUpdate_ShowsBannerAndNavDot()
    {
        HeadlessUi.Run(() =>
        {
            string? suffix = new[] { "-bin-macos-arm64", "-bin-macos-x64", "-bin-win-vulkan-x64", "-bin-ubuntu-vulkan-x64" }
                .FirstOrDefault(x => LLamaCppVariants.IsRecommended("llama-b1" + x + ".zip"));
            if (suffix == null) return;
            SettingsWindow window = BuildWindow();
            _window = window;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                LLamaCppEngineInstaller.Shared.ApplyVersions(
                [
                    new VersionInfo { Name = $"llama-b100{suffix}", Version = new Version(100, 0), IsInstalled = true },
                    new VersionInfo { Name = $"llama-b200{suffix}", Version = new Version(200, 0), DownloadUrl = "http://127.0.0.1:1/x.zip" }
                ]);
                Dispatcher.UIThread.RunJobs();
                window.FindControl<Button>("RuntimeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.True(window.FindControl<Border>("EngineUpdateDot")!.IsVisible);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text?.Contains("b200") == true);
                Capture(window, SettingsShotsDir(), "runtime-engine-update");
            }
            finally
            {
                LLamaCppEngineInstaller.Shared.ApplyVersions([]);
                window.Close();
                _window = null;
                ConfigManager.Instance.Setting.SettingsScrollPositions.Remove("RuntimeEngineSetting");
            }
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

    // 本地模型页按 Jan 的口径分节：引擎卡 + 常用 / 性能 / 内存与缓存 / 高级 + 下载源，每节都得真渲染出来
    private static void AssertRuntimeSectionsRendered(SettingsWindow window)
    {
        List<SettingsSection> sections = window.GetVisualDescendants().OfType<SettingsSection>()
            .Where(x => x.FindAncestorOfType<RuntimeEngineSettingView>() != null)
            .ToList();
        Assert.True(sections.Count >= 6, $"本地模型页应有至少 6 节，实际 {sections.Count}");
        Assert.True(window.GetVisualDescendants().OfType<SettingsRow>()
            .Count(x => x.FindAncestorOfType<RuntimeEngineSettingView>() != null) >= 20, "本地模型页的设置行少了");
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
