using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Features.Models;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 模型运行参数窗口：五行设置在真实模板下排得开，改过的行才冒出撤销按钮。
/// 顺带出一张截图（目录由 <c>MODEL_DOWNLOAD_SHOTS_DIR</c> 指定，默认系统临时目录）
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ModelRuntimeOverridesWindowTests
{
    [Fact]
    public void Window_ShowsRows_AndResetOnlyOnOverriddenRows()
    {
        HeadlessUi.Run(() =>
        {
            ModelRuntimeSettingConfig config = new() { ContextSize = 8192 };
            config.SetOverrides("Qwen3-8B-Q4_K_M", new ModelRuntimeOverrides { GpuLayers = 20 });
            ModelRuntimeOverridesWindow window = new()
            {
                DataContext = new ModelRuntimeOverridesViewData("Qwen3-8B-Q4_K_M", config, () => { },
                    () => RuntimeLoadRisk.Low, true, 36)
            };
            window.Show();
            window.UpdateLayout();

            List<SettingsRow> rows = window.GetVisualDescendants().OfType<SettingsRow>().ToList();
            Assert.Equal(5, rows.Count);
            Assert.Equal([false, true, false, false, false], rows.Select(x => x.ResetVisible).ToArray());

            string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
                                 ?? throw new InvalidOperationException("空帧");
            frame.Save(Path.Combine(dir, "model-params.png"));
            window.Close();
        });
    }
}
