using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Features.Models;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 模型运行参数窗口：六行设置在真实模板下排得开，改过的行才冒出撤销按钮。
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
            config.SetOverrides("Qwen3-8B-Q4_K_M", new ModelRuntimeOverrides { GpuLayers = 20, KvCacheType = "q8_0" });
            ModelRuntimeOverridesWindow window = new()
            {
                DataContext = new ModelRuntimeOverridesViewData("Qwen3-8B-Q4_K_M", config, () => { },
                    () => RuntimeLoadRisk.Low, true, 36, () => "f16")
            };
            window.Show();
            window.UpdateLayout();

            List<SettingsRow> rows = window.GetVisualDescendants().OfType<SettingsRow>().ToList();
            Assert.Equal(6, rows.Count);
            Assert.Equal([false, true, false, false, false, true], rows.Select(x => x.ResetVisible).ToArray());
            Assert.Equal("q8_0", rows[5].GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem);

            string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
                                 ?? throw new InvalidOperationException("空帧");
            frame.Save(Path.Combine(dir, "model-params.png"), PngBitmapEncoderOptions.Default);
            window.Close();
        });
    }

    [Fact]
    public void KvCacheType_SavedByName_AndResetFollowsGlobal()
    {
        ModelRuntimeSettingConfig config = new();
        ModelRuntimeOverridesViewData data = new("m", config, () => { }, () => RuntimeLoadRisk.Low, false, 36,
            () => "f16");

        Assert.Equal("f16", data.KvCacheTypeOptions[data.KvCacheType.Value]);
        data.KvCacheType.Value = data.KvCacheTypeOptions.ToList().IndexOf("q4_0");
        Assert.Equal("q4_0", config.GetOverrides("m")?.KvCacheType);
        Assert.Equal("q4_0", config.ForModel("m").KvCacheType);

        data.KvCacheType.Reset();
        Assert.Null(config.GetOverrides("m"));
    }
}
