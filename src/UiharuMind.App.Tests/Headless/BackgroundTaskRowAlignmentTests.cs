using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 输入区上方的后台任务行：描述文字与“后台任务”标签同一字号并排，
/// 两边须挂同一份视觉微调（title-tweak-sm，下移 1px 对齐 12px 图标），
/// 否则描述看着比标签高一截。子代理行与审批行早就是这个口径，新行漏挂过一次
/// </summary>
[Collection(HeadlessCollection.Name)]
public class BackgroundTaskRowAlignmentTests
{
    [Fact]
    public void DescriptionText_UsesSmallTitleTweak_LikeItsLabel()
    {
        HeadlessUi.Run(() =>
        {
            using ConversationViewModel vm = new(new RecordingMessageService());
            ConversationView view = new() { DataContext = vm };
            Window window = new() { Width = 900, Height = 700, Content = view };
            window.Show();
            Pump(window);
            try
            {
                vm.BackgroundTasks.Items.Add(new BackgroundTaskStatusViewData("t1", "对齐回归描述", "cmd"));
                Pump(window);

                TextBlock desc = window.GetVisualDescendants().OfType<TextBlock>()
                    .First(x => x.Text == "对齐回归描述");
                Assert.Contains("title-tweak-sm", desc.Classes);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Pump(Window window)
    {
        for (int i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
