using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Features.Settings;
using UiharuMind.Shared.Data;

namespace UiharuMind.App.Tests.Settings;

/// <summary>
/// 本地模型参数页：llama-server 选项改动即写回、恢复默认；GPU 层数三种取法与配置值互相对得上
/// </summary>
public class LLamaCppSettingsTests : IDisposable
{
    private readonly int _gpuLayers = ModelRuntimeSettingConfig.Current.GpuLayers;
    private readonly bool? _flashAttention = ModelRuntimeSettingConfig.Current.FlashAttention;

    public void Dispose()
    {
        ModelRuntimeSettingConfig.Current.GpuLayers = _gpuLayers;
        ModelRuntimeSettingConfig.Current.FlashAttention = _flashAttention;
    }

    [Fact]
    public void ServerOptions_WriteBack_AndReset()
    {
        LLamaCppSettingConfig config = new();
        LLamaCppServerSettingsViewData data = new(config);

        data.LoadMode = data.LoadModeOptions.Single(x => x.Value == "mlock");
        data.CacheTypeK = data.CacheTypeOptions.Single(x => x.Value == "q8_0");
        data.CacheRamMiB = -1;
        data.ExtraArguments = "  --temp 0.7 ";

        Assert.Equal("mlock", config.Server.LoadMode);
        Assert.Equal("q8_0", config.Server.CacheTypeK);
        Assert.Equal(-1, config.Server.CacheRamMiB);
        Assert.Equal("--temp 0.7", config.Server.ExtraArguments);

        data.ResetToDefaults();

        Assert.Equal(LLamaCppServerOptions.DefaultLoadMode, config.Server.LoadMode);
        Assert.Equal(LLamaCppServerOptions.DefaultCacheType, config.Server.CacheTypeK);
        Assert.Equal("auto", data.LoadMode.Value);
        Assert.Equal(8192m, data.CacheRamMiB);
    }

    [Fact]
    public void GpuLayerMode_MapsToConfig_AndRemembersCustomCount()
    {
        ModelRuntimeSettingConfig.Current.GpuLayers = 24;
        ModelRuntimeBasicSettingsData data = new();
        Assert.Equal(EGpuLayerMode.Custom, data.SelectedGpuLayerMode.Value);

        data.SelectedGpuLayerMode = data.GpuLayerModeOptions.Single(x => x.Value == EGpuLayerMode.Auto);
        Assert.Equal(-1, ModelRuntimeSettingConfig.Current.GpuLayers);
        Assert.False(data.IsCustomGpuLayers);

        data.SelectedGpuLayerMode = data.GpuLayerModeOptions.Single(x => x.Value == EGpuLayerMode.CpuOnly);
        Assert.Equal(0, ModelRuntimeSettingConfig.Current.GpuLayers);

        data.SelectedGpuLayerMode = data.GpuLayerModeOptions.Single(x => x.Value == EGpuLayerMode.Custom);
        Assert.Equal(24, ModelRuntimeSettingConfig.Current.GpuLayers);
        Assert.True(data.IsCustomGpuLayers);

        data.SelectedFlashAttention = data.FlashAttentionOptions.Single(x => x.Value == false);
        Assert.False(ModelRuntimeSettingConfig.Current.FlashAttention);
    }

    [Theory]
    [InlineData("llama-b11443-bin-macos-arm64", "https://github.com/ggml-org/llama.cpp/releases/tag/b11443")]
    [InlineData("llama-b6000-bin-win-vulkan-x64.zip", "https://github.com/ggml-org/llama.cpp/releases/tag/b6000")]
    [InlineData("custom-build", null)]
    public void ReleaseUrl_FromBuildTag(string name, string? expected)
    {
        Assert.Equal(expected, RuntimeEngineSettingData.LLamaCppReleaseUrl(name));
    }
}
