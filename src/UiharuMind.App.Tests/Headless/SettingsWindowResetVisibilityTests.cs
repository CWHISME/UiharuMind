using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.Core;
using UiharuMind.Features.About;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 恢复默认按钮的可见性规则（Aico 口径）：值在出厂值上时按钮不显示，改了才冒出来。
/// 钉法：Shortcut 页五行的值先全部拨回出厂值 → 可见的 ↺ 应为 0；改一项为非默认 → 恰好 1；
/// 改回默认 → 0。出厂值一律走 <see cref="SettingConfig"/> 常量，不手抄数字。
/// 定位用 SettingsRow 里的 <see cref="ThemedSvgIconButton"/>（IconName=undo），静默空白/换装没落地都会在这里红。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SettingsWindowResetVisibilityTests : IDisposable
{
    private SettingsWindow? _window;

    public void Dispose() => HeadlessUi.Run(() => _window?.Close());

    [Fact]
    public void ResetButton_HiddenAtDefault_AppearsAfterChange()
    {
        HeadlessUi.Run(() =>
        {
            RecordingMessageService messages = new();
            ApplicationUpdateService updateService = new() { HasChecked = true }; // 跳过联网检查
            ServiceProvider services = new ServiceCollection()
                .AddSingleton<IMessageService>(messages)
                .AddSingleton(updateService)
                .BuildServiceProvider();
            var generalVm = ActivatorUtilities.CreateInstance<GeneralSettingViewModel>(services);
            var shortcutVm = new ShortcutSettingViewModel();
            var window = new SettingsWindow(
                generalVm,
                new RuntimeEngineSettingData(messages),
                shortcutVm,
                GetPageForHeadless);
            _window = window;
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // 起始页就是 General：出厂值下两行不应有恢复默认按钮，改一项冒一颗，改回消失。
            // 只动 ClipboardRetentionDays——全屏输入开关改值会弹确认框（RecordingMessageService 默认应 true 还会触发重启）。
            generalVm.ClipboardRetentionDays = SettingConfig.FactoryDefaultClipboardRetentionDays;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(VisibleResetButtons(window) == 0, "General 出厂值下不应显示任何恢复默认按钮");

            generalVm.ClipboardRetentionDays = 7;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(VisibleResetButtons(window) == 1, "剪贴板保留天数改非出厂后应恰好冒出一颗恢复默认按钮");

            generalVm.ClipboardRetentionDays = SettingConfig.FactoryDefaultClipboardRetentionDays;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(VisibleResetButtons(window) == 0, "剪贴板保留天数改回出厂值后按钮应消失");

            window.FindControl<Button>("ShortcutsButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // 先把五行全部拨回出厂值（防同进程其它测试在 SettingConfig 里改过内存值），不落盘
            shortcutVm.CaptureScreenShortcut = SettingConfig.DefaultCaptureScreenShortcut;
            shortcutVm.QuickStartChatShortcut = SettingConfig.DefaultQuickStartChatShortcut;
            shortcutVm.ClipboardHistoryShortcut = SettingConfig.DefaultClipboardHistoryShortcut;
            shortcutVm.QuickTranslationShortcut = SettingConfig.DefaultQuickTranslationShortcut;
            shortcutVm.QuickAutoClickShortcut = SettingConfig.DefaultQuickAutoClickShortcut;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(VisibleResetButtons(window) == 0,
                "全部值在出厂值上时不应显示任何恢复默认按钮");

            shortcutVm.CaptureScreenShortcut = "Alt+Shift+TEST";
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(VisibleResetButtons(window) == 1,
                "改一项为非默认后应恰好冒出一颗恢复默认按钮");

            shortcutVm.CaptureScreenShortcut = SettingConfig.DefaultCaptureScreenShortcut;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(VisibleResetButtons(window) == 0,
                "改回出厂值后恢复默认按钮应消失");
        });
    }

    private static int VisibleResetButtons(SettingsWindow window) =>
        window.GetVisualDescendants()
            .OfType<ThemedSvgIconButton>()
            .Count(b => b.IconName == "undo" && b.IsEffectivelyVisible);

    // 无头下取页路径由测试提供；SettingsWindow 只取 MenuHelpKey，Help 页可真实构造（无 App 依赖）
    private static PageDataBase GetPageForHeadless(MenuPages key)
    {
        Assert.Equal(MenuPages.MenuHelpKey, key);
        return new HelpPageData();
    }
}
