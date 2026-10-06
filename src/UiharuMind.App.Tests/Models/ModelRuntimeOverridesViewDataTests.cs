using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Configs;
using UiharuMind.Features.Models;
using UiharuMind.Shared.Data;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 按模型的运行参数：没改的跟随全局，改了才记成这个模型自己的，恢复后从配置里消失
/// </summary>
public class ModelRuntimeOverridesViewDataTests
{
    private static ModelRuntimeOverridesViewData Create(ModelRuntimeSettingConfig config, Action? save = null) =>
        new("m", config, save ?? (() => { }), () => RuntimeLoadRisk.Low, false, 40);

    [Fact]
    public void Untouched_ShowsGlobalValues_AndWritesNothing()
    {
        ModelRuntimeSettingConfig config = new() { ContextSize = 8192, GpuLayers = -1 };
        ModelRuntimeOverridesViewData data = Create(config);

        Assert.Equal(8192, data.ContextSize.Value);
        Assert.False(data.ContextSize.IsOverridden);
        Assert.Equal(EGpuLayerMode.Auto, data.SelectedGpuLayerMode.Value);
        Assert.Empty(config.ModelOverrides);
    }

    [Fact]
    public void EditingValue_OverridesOnlyThatField_AndSaves()
    {
        ModelRuntimeSettingConfig config = new() { ContextSize = 8192, Threads = 4 };
        int saves = 0;
        ModelRuntimeOverridesViewData data = Create(config, () => saves++);

        data.ContextSize.Value = 32768;

        Assert.True(data.ContextSize.IsOverridden);
        Assert.Equal(1, saves);
        Assert.Equal(32768, config.ForModel("m").ContextSize);
        Assert.Equal(4, config.ForModel("m").Threads);
        Assert.Null(config.GetOverrides("m")!.Threads);
    }

    [Fact]
    public void Reset_FollowsGlobalAgain_AndDropsEntry()
    {
        ModelRuntimeSettingConfig config = new() { ContextSize = 8192 };
        ModelRuntimeOverridesViewData data = Create(config);
        data.ContextSize.Value = 4096;

        data.ContextSize.Reset();

        Assert.False(data.ContextSize.IsOverridden);
        Assert.Equal(8192, data.ContextSize.Value);
        Assert.Empty(config.ModelOverrides);
    }

    [Fact]
    public void GpuMode_PicksLayerValue_AndResetSyncsMode()
    {
        ModelRuntimeSettingConfig config = new() { GpuLayers = -1 };
        ModelRuntimeOverridesViewData data = Create(config);

        data.SelectedGpuLayerMode = data.GpuLayerModeOptions[(int)EGpuLayerMode.CpuOnly];
        Assert.Equal(0, config.ForModel("m").GpuLayers);

        data.SelectedGpuLayerMode = data.GpuLayerModeOptions[(int)EGpuLayerMode.Custom];
        Assert.Equal(40, config.ForModel("m").GpuLayers);
        Assert.True(data.IsCustomGpuLayers);

        data.GpuLayers.Reset();
        Assert.Equal(EGpuLayerMode.Auto, data.SelectedGpuLayerMode.Value);
        Assert.Empty(config.ModelOverrides);
    }

    [Fact]
    public void ExistingOverrides_AreLoaded()
    {
        ModelRuntimeSettingConfig config = new() { BatchSize = 0 };
        config.SetOverrides("m", new ModelRuntimeOverrides { BatchSize = 1024, GpuLayers = 12 });

        ModelRuntimeOverridesViewData data = Create(config);

        Assert.True(data.BatchSize.IsOverridden);
        Assert.Equal(1024, data.BatchSize.Value);
        Assert.Equal(EGpuLayerMode.Custom, data.SelectedGpuLayerMode.Value);
        Assert.False(data.Threads.IsOverridden);
    }

    [Fact]
    public void ResetAll_ClearsEverything()
    {
        ModelRuntimeSettingConfig config = new();
        ModelRuntimeOverridesViewData data = Create(config);
        data.Threads.Value = 6;
        data.UBatchSize.Value = 256;

        data.ResetAllCommand.Execute(null);

        Assert.Empty(config.ModelOverrides);
    }
}
