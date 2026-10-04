using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Features.About;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 设置窗键盘走通的验收（截图验不了键盘，这条单独补）：
/// 1) 每个设置页的交互控件都能聚焦（键盘摸得到）；
/// 2) Tab 能走到内容区带命令的按钮（保存/整体重置/单项恢复默认）；
/// 3) Enter 能激活按钮（用切页导航按钮验证，无副作用）。
///
/// 切页走真实按钮点击、组装置用截图测试那套注入路径（无头 App 下 App.ViewModel 为 null）。
/// 刻意<b>不触发</b>会写盘的命令（保存/恢复默认/改值）：设置落盘会写到真实配置路径，
/// 测试不该污染。键盘触发设置命令的链路 = Button.Command，由 2) 的可达 + 3) 的 Enter 激活共同保证。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SettingsWindowKeyboardTests : IDisposable
{
    private SettingsWindow? _window;

    public void Dispose() => HeadlessUi.Run(() => _window?.Close());

    [Fact]
    public void EverySettingsPage_InteractiveControls_AreFocusable()
    {
        HeadlessUi.Run(() =>
        {
            SettingsWindow window = BuildWindow();
            _window = window;
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            CheckPage(window, "GeneralButton", "General");
            CheckPage(window, "RuntimeButton", "Runtime");
            CheckPage(window, "AgentButton", "Agent");

            // Agent 页是 5 个 Tab：逐个切过去，内嵌的 MCP/Python/WebSearch 也在可视树里。
            // 此时内容必须是 Agent 页（下面还要切 QuickTool/Shortcuts，得先查完）
            ContentControl agentContent = window.FindControl<ContentControl>("SettingsContent")!;
            Assert.IsType<AgentSettingView>(agentContent.Content);
            TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            for (int i = 0; i < tabs.ItemCount; i++)
            {
                tabs.SelectedIndex = i;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                CheckInteractiveFocusable(window, $"Agent Tab {i}");
            }

            CheckPage(window, "QuickToolButton", "QuickTool");
            CheckPage(window, "ShortcutsButton", "Shortcuts");
        });
    }

    [Fact]
    public void Tab_ReachesCommandButtons_OnShortcutPage()
    {
        HeadlessUi.Run(() =>
        {
            SettingsWindow window = BuildWindow();
            _window = window;
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            window.FindControl<Button>("ShortcutsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // 左栏导航按钮没有 Command；Shortcut 页带 Command 的按钮（保存/整体重置/单项恢复）
            // 只在内容区。Tab 走到任意一个，就算键盘能摸到操作面。
            bool reached = false;
            for (int i = 0; i < 80 && !reached; i++)
            {
                window.KeyPress(Key.Tab, RawInputModifiers.None, default, "");
                Dispatcher.UIThread.RunJobs();
                reached = window.FocusManager?.GetFocusedElement() is Button { Command: not null };
            }
            Assert.True(reached, "Tab 走完没碰到 Shortcut 页任何带命令的按钮（保存/恢复默认）");
        });
    }

    [Fact]
    public void Enter_ActivatesButton_OnNavigationButton()
    {
        HeadlessUi.Run(() =>
        {
            SettingsWindow window = BuildWindow();
            _window = window;
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            ContentControl settingsContent = window.FindControl<ContentControl>("SettingsContent")
                ?? throw new InvalidOperationException("找不到 SettingsContent");
            object? before = settingsContent.Content;
            Button helpButton = window.FindControl<Button>("HelpButton")
                ?? throw new InvalidOperationException("找不到 HelpButton");
            helpButton.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, default, "");
            Dispatcher.UIThread.RunJobs();

            Assert.NotSame(before, settingsContent.Content); // Enter 触发了切页（按钮 Click 生效）
        });
    }

    private static void CheckPage(SettingsWindow window, string buttonName, string pageName)
    {
        window.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        CheckInteractiveFocusable(window, pageName);
    }

    private static void CheckInteractiveFocusable(SettingsWindow window, string where)
    {
        List<Control> interactive = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => IsInteractive(c) && c.IsVisible && c.IsEffectivelyEnabled)
            .ToList();
        Assert.NotEmpty(interactive);
        foreach (Control c in interactive)
        {
            Assert.True(c.Focusable, $"{where}: 交互控件不可聚焦 {c.GetType().Name}");
        }
    }

    private static bool IsInteractive(Control c) => c switch
    {
        // RepeatButton 是 NumericUpDown/ScrollBar 模板内的内部微调钮，默认不聚焦是设计
        // （键盘用上下箭头调值，Tab 不该卡在微调钮上），它不是交互入口
        RepeatButton => false,
        Button or ToggleSwitch or CheckBox or ComboBox or TextBox or NumericUpDown => true,
        _ => false,
    };

    private static SettingsWindow BuildWindow()
    {
        RecordingMessageService messages = new();
        ApplicationUpdateService updateService = new() { HasChecked = true }; // 跳过联网检查
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

    private static PageDataBase GetPageForHeadless(MenuPages key)
    {
        Assert.Equal(MenuPages.MenuHelpKey, key);
        return new HelpPageData();
    }
}
