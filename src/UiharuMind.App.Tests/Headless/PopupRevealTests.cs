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

        Assert.True(opacities[0] < 1, $"首个可见帧应仍在动画中，实际不透明度 {opacities[0]:F2}");
        Assert.Equal(opacities.OrderBy(o => o), opacities);
        Assert.Equal(1, opacities[^1]);
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

        Assert.True(opacities[0] < 1);
        Assert.Equal(1, opacities[^1]);
        window.Close();
    });

    private static (Window, ComboBox) Show()
    {
        ComboBox combo = new() { ItemsSource = new[] { "a", "b", "c", "d" }, SelectedIndex = 0, Width = 200 };
        Window window = new() { Width = 400, Height = 300, Content = combo };
        window.Show();
        Pump(window);
        return (window, combo);
    }

    private static List<double> Open(Window window, ComboBox combo)
    {
        List<double> opacities = [];
        combo.IsDropDownOpen = true;
        for (int i = 0; i < 40; i++)
        {
            Pump(window);
            LayoutTransformControl? content = FindPopupContent(combo);
            if (content != null) opacities.Add(content.Opacity);
            if (opacities.Count > 0 && opacities[^1] >= 1) break;
            Thread.Sleep(12);
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
