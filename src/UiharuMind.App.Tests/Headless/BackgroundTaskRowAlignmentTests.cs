using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Features.Conversation;
using UiharuMind.Shared.Converters;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 输入区上方的后台任务行：描述文字与“后台任务”标签同一字号并排，
/// 两边须吃到同一份全局文字下移（TextOpticalOffsetConverter），否则描述看着比标签高一截。
/// 以前靠逐个挂 title-tweak-sm，新行漏挂过一次；现在由全局样式给，这里守住没被局部值盖掉
/// </summary>
[Collection(HeadlessCollection.Name)]
public class BackgroundTaskRowAlignmentTests
{
    [Fact]
    public void DescriptionText_GetsTheSameOpticalOffset_AsItsLabel()
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
                Assert.Equal(TextOpticalOffsetConverter.Pixels(desc.FontSize), desc.RenderTransform?.Value.M32);
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
