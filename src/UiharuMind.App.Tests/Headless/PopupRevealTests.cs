using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 弹出层出现动画。首次展开要建弹出层、套模板、生成下拉项，这段耗时曾把 Semi 自带的 100ms 关键帧动画吃光，
/// 表现为「首次展开没动画，第二次才有」——所以这里专门盯首次
/// </summary>
[Collection(HeadlessCollection.Name)]
public class PopupRevealTests
{
    [Fact]
    public void FirstOpen_IsObservedMidAnimation_AndEndsFullyOpaque() => HeadlessUi.Run(() =>
    {
        (Window window, ComboBox combo) = Show();

        List<double> opacities = Open(window, combo);

        string trace = string.Join(" ", opacities.Select(o => o.ToString("F2")));
        Assert.True(opacities[0] < 1, $"首个可见帧应仍在动画中，实际不透明度 {opacities[0]:F2}\n{trace}");
        Assert.Equal(opacities.OrderBy(o => o), opacities);
        Assert.True(opacities[^1] == 1, $"动画应走完，实际采样：{trace}");
        window.Close();
    });

    [Fact]
    public void SecondOpen_AnimatesAgain() => HeadlessUi.Run(() =>
    {
        (Window window, ComboBox combo) = Show();
        Open(window, combo);
        combo.IsDropDownOpen = false;
        Pump(window);

        List<double> opacities = Open(window, combo);

        string trace = string.Join(" ", opacities.Select(o => o.ToString("F2")));
        Assert.True(opacities[0] < 1, $"第二次打开首帧应仍在动画中，实际不透明度 {opacities[0]:F2}\n{trace}");
        Assert.True(opacities[^1] == 1, $"动画应走完，实际采样：{trace}");
        window.Close();
    });

    private static (Window, ComboBox) Show()
    {
        ComboBox combo = new() { ItemsSource = new[] { "a", "b", "c", "d" }, SelectedIndex = 0, Width = 200 };
        Window window = new() { Width = 400, Height = 300, Content = combo };
        window.Show();
        Pump(window);
        // 动画时钟由共享的合成器帧循环驱动：同进程里前面的测试（透明窗口反复显示/关闭）可能让它有批次没落定，
        // 直接开弹出层会遇到时钟不走。先泵几帧让它空闲下来再测
        for (int i = 0; i < 12; i++)
        {
            Thread.Sleep(16);
            Pump(window);
        }

        return (window, combo);
    }

    private static List<double> Open(Window window, ComboBox combo)
    {
        List<double> opacities = [];
        combo.IsDropDownOpen = true;
        // 首个采样在任何等待之前，读到的必是动画中途。之后每轮等 50ms、至多 250 轮，总预算约 12 秒：
        // 整套并行跑时合成器时钟偶发停走（见 Show 的说明），实测曾卡在 0.88 超过 3 秒，旧的 12ms 步长只给 3 秒
        for (int i = 0; i < 250; i++)
        {
            Pump(window);
            LayoutTransformControl? content = FindPopupContent(combo);
            if (content != null) opacities.Add(content.Opacity);
            if (opacities.Count > 0 && opacities[^1] >= 1) break;
            Thread.Sleep(50);
        }

        Assert.NotEmpty(opacities);
        return opacities;
    }

    private static LayoutTransformControl? FindPopupContent(ComboBox combo)
    {
        Popup? popup = combo.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
        if (popup?.Child is not { } child || TopLevel.GetTopLevel(child) is not { } root) return null;
        return root.GetVisualDescendants().OfType<LayoutTransformControl>()
            .FirstOrDefault(x => x.Name == "PART_LayoutTransform");
    }

    private static void Pump(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
