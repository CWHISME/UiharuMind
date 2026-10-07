using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Features.About;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 本地模型页引擎卡头：标题（徽标）左、操作按钮右。窄窗口下版本徽标应该省略，
/// 不能跟按钮叠在一起（DockPanel 时代这里会重叠）。按窗口最小宽度验收。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class RuntimeEngineHeaderLayoutTests : IDisposable
{
    private SettingsWindow? _window;

    public void Dispose() => HeadlessUi.Run(() => _window?.Close());

    [Fact]
    public void NarrowWindow_HeaderTitleAndActions_DoNotOverlap()
    {
        HeadlessUi.Run(() =>
        {
            double savedWidth = ConfigManager.Instance.Setting.SettingsWindowWidth;
            double savedHeight = ConfigManager.Instance.Setting.SettingsWindowHeight;
            try
            {
                RecordingMessageService messages = new();
                RuntimeEngineSettingData data = new(messages);
                ApplicationUpdateService updateService = new() { HasChecked = true };
                ServiceProvider services = new ServiceCollection()
                    .AddSingleton<IMessageService>(messages)
                    .AddSingleton(updateService)
                    .BuildServiceProvider();

                SettingsWindow window = new(
                    ActivatorUtilities.CreateInstance<GeneralSettingViewModel>(services),
                    data,
                    new ShortcutSettingViewModel(),
                    GetPageForHeadless);
                _window = window;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                // 最挤的情形：长版本名 + 推荐徽标 + 发布说明按钮都在。
                // 构造里有个异步回填（本地版本读完会重设选中），必须在它跑完后再指定，否则会被盖掉
                string? suffix = new[] { "-bin-macos-arm64", "-bin-macos-x64", "-bin-win-vulkan-x64", "-bin-ubuntu-vulkan-x64" }
                    .FirstOrDefault(x => LLamaCppVariants.IsRecommended("llama-b1" + x + ".zip"));
                data.SelectedVersion = new VersionInfo
                    { Name = $"llama-b11443{suffix ?? "-bin-macos-arm64"}" };
                window.FindControl<Button>("RuntimeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                window.Width = window.MinWidth;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Grid title = window.GetVisualDescendants().OfType<RuntimeEngineSettingView>().Single()
                    .FindControl<Grid>("EngineHeaderTitle")
                    ?? throw new InvalidOperationException("找不到引擎卡标题区");
                StackPanel actions = window.GetVisualDescendants().OfType<RuntimeEngineSettingView>().Single()
                    .FindControl<StackPanel>("EngineHeaderActions")
                    ?? throw new InvalidOperationException("找不到引擎卡按钮区");
                // 探针要量徽标文字本尊：容器 Bounds 量不到溢出的文字（Grid/StackPanel 溢出时 Bounds 不变）
                TextBlock badge = window.GetVisualDescendants().OfType<RuntimeEngineSettingView>().Single()
                    .FindControl<TextBlock>("EngineVersionBadgeText")
                    ?? throw new InvalidOperationException("找不到版本徽标文字");
                Point titleOrigin = title.TranslatePoint(new Point(0, 0), window)!.Value;
                Point actionsOrigin = actions.TranslatePoint(new Point(0, 0), window)!.Value;
                Point badgeOrigin = badge.TranslatePoint(new Point(0, 0), window)!.Value;
                double titleRight = titleOrigin.X + title.Bounds.Width;
                double actionsRight = actionsOrigin.X + actions.Bounds.Width;
                double badgeRight = badgeOrigin.X + badge.Bounds.Width;
                // 先保证真渲染出来了，不然下面的不重叠断言是空转通过
                Assert.True(title.Bounds.Width > 100, $"标题区没渲染出来：{title.Bounds.Width}");
                Assert.True(actions.Bounds.Width > 100, $"按钮区没渲染出来：{actions.Bounds.Width}");
                Assert.True(titleRight <= actionsOrigin.X + 0.5,
                    $"标题区右缘 {titleRight} 压住了按钮区左缘 {actionsOrigin.X}");
                Assert.True(badgeRight <= actionsOrigin.X + 0.5,
                    $"版本徽标右缘 {badgeRight} 压住了按钮区左缘 {actionsOrigin.X}");
                Assert.True(actionsRight <= window.Bounds.Width + 0.5,
                    $"按钮区右缘 {actionsRight} 超出了窗口 {window.Bounds.Width}");

                // 宽窗口下推荐徽标应该跟在版本徽标后面，而不是飘到按钮区旁边
                window.Width = 960;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Border recommend = window.GetVisualDescendants().OfType<RuntimeEngineSettingView>().Single()
                    .FindControl<Border>("EngineRecommendBadge")
                    ?? throw new InvalidOperationException("找不到推荐徽标");
                Assert.True(recommend.IsVisible, "最挤的用例里推荐徽标应该可见");
                Point badgeWideOrigin = badge.TranslatePoint(new Point(0, 0), window)!.Value;
                Point recommendOrigin = recommend.TranslatePoint(new Point(0, 0), window)!.Value;
                double gap = recommendOrigin.X - (badgeWideOrigin.X + badge.Bounds.Width);
                // 列间距 8 + 徽标内边距 6 + 边框 1 ≈ 15；飘到按钮区旁边时会是几百
                Assert.InRange(gap, 12, 20);
            }
            finally
            {
                // 关窗时已按最小宽度落盘一次，这里连文件一起恢复，不影响别的测试
                ConfigManager.Instance.Setting.SettingsWindowWidth = savedWidth;
                ConfigManager.Instance.Setting.SettingsWindowHeight = savedHeight;
                ConfigManager.Instance.Setting.Save();
            }
        });
    }

    private static PageDataBase GetPageForHeadless(MenuPages key)
    {
        Assert.Equal(MenuPages.MenuHelpKey, key);
        return new HelpPageData();
    }
}
