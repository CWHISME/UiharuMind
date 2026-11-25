using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.Features.Settings;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 智能体设置页那四行模型配置的<b>顺序</b>：通用 → 探索 → 评审 → 整理。
///
/// 顺序是产品语义（前三个是「派活时能选的档位」，整理是工具内部按需起的），但 XAML 里没有任何
/// 东西拦着你把它插到中间——四行长得一模一样，调错了没人看得出来。这里按屏幕上的纵坐标取实际渲染顺序，
/// 钉的是「用户看到的顺序」，不是文件里的书写顺序。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class SettingsSubAgentRowsTests
{
    [Fact]
    public void ModelRows_AreOrderedGeneralExploreReviewBrain()
    {
        HeadlessUi.Run(() =>
        {
            AgentSettingView view = new();
            AgentSettingViewData vm = (AgentSettingViewData)view.DataContext!;
            Window window = new() { Width = 960, Height = 800, Content = view };
            window.Show();
            for (int i = 0; i < 5; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }

            try
            {
                List<SubAgentModelSlot> order = view.GetVisualDescendants()
                    .OfType<SubAgentModelPicker>()
                    .Select(picker => (picker, top: picker.TranslatePoint(new Point(0, 0), view)?.Y ?? double.MaxValue))
                    .OrderBy(x => x.top)
                    .Select(x => x.picker.Slot!)
                    .ToList();

                Assert.Equal(
                    new[]
                    {
                        vm.GeneralSubAgentModels, vm.ExplorerSubAgentModels, vm.ReviewerSubAgentModels,
                        vm.BrainSubAgentModels,
                    },
                    order);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
