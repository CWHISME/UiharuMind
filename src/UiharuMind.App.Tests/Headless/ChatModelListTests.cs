using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 对话模型页签：一个模型都没有时出空状态，有模型时出列表行。
/// 顺带出一张截图（目录由 <c>MODEL_DOWNLOAD_SHOTS_DIR</c> 指定，默认系统临时目录）
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ChatModelListTests
{
    private static ModelRunningData Local(string name, string summarySize, bool vision = false) =>
        new(new GGufModelInfo
        {
            ModelName = name,
            ModelPath = $"/models/unsloth/{name}.gguf",
            ModelProjPath = vision ? "/models/mmproj.gguf" : null,
            Architecture = "qwen3",
            SizeLabel = summarySize,
            ContextLength = 40960,
            LayerCount = 36,
            FileSizeBytes = 5_000_000_000
        });

    private static ModelRunningData Remote(string name) =>
        new(new RemoteModelInfo
        {
            Config = new RemoteDeepSeekModelConfig { ModelName = name, ModelId = "deepseek-chat", ContextLength = 65536 },
            ApiKey = "key"
        });

    [Fact]
    public void EmptyState_ShowsUntilAModelArrives()
    {
        HeadlessUi.Run(() =>
        {
            ObservableCollection<ModelRunningData> models = [];
            ModelPageData data = new(new RecordingMessageService(), modelSources: models);
            Window window = new() { Width = 960, Height = 560, Content = new ModelPage { DataContext = data } };
            window.Show();
            data.IsListDataReady = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(data.HasNoModels);
            Capture(window, "chat-models-empty");

            models.Add(Remote("DeepSeek-V4"));
            models.Add(Local("Qwen3-8B-Q4_K_M", "8B", vision: true));
            models.Add(Local("Qwen3-0.6B-Q8_0", "0.6B"));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(data.HasNoModels);
            Assert.Equal(3, window.GetVisualDescendants().OfType<ListBoxItem>().Count());
            Capture(window, "chat-models");
            window.Close();
        });
    }

    private static void Capture(Window window, string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        window.UpdateLayout();
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
                             ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"));
    }
}
