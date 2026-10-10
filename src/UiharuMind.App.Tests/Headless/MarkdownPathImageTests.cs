using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 回复里写 ![说明]($DRAFT/x.png)，气泡顺着树拿到会话口径就能把图加载出来（ADR 0080），不改渲染库
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class MarkdownPathImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-md-image-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DraftShorthandImage_LoadsThroughInheritedResolver() => HeadlessUi.RunAsync(async () =>
    {
        string draft = Path.Combine(_root, "draft");
        Directory.CreateDirectory(draft);
        using (SKBitmap bitmap = new(8, 6))
        using (SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        {
            File.WriteAllBytes(Path.Combine(draft, "a.png"), data.ToArray());
        }

        SimpleMarkdownViewer viewer = new();
        Border host = new() { Child = viewer };
        MarkdownPaths.SetResolver(host, new AgentPathResolver(Path.Combine(_root, "workspace"), draft));
        Window window = new() { Width = 600, Height = 400, Content = host };
        window.Show();
        viewer.MarkdownText = "图如下：\n\n![图]($DRAFT/a.png)";
        viewer.IsPlaintext = false;

        Image? image = null;
        for (int i = 0; i < 100 && image?.Source == null; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
            image = viewer.GetVisualDescendants().OfType<Image>().FirstOrDefault(x => x.Classes.Contains("Link"));
            await Task.Delay(20);
        }

        Assert.NotNull(image?.Source);
        window.Close();
    });
}
