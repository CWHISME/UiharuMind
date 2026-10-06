using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Features.Settings;

namespace UiharuMind.App.Tests.Settings;

/// <summary>
/// 下载源一节：回填照配置，改动即写回；自定义地址行只在选了自定义时出现，填错了有提示
/// </summary>
public class DownloadSourceSettingsViewDataTests
{
    [Fact]
    public void Load_ReflectsConfig()
    {
        DownloadSourceSettingConfig config = new()
        {
            ModelSource = DownloadSourceSettingConfig.SourceModelScope,
            ModelScopeToken = "ms"
        };

        DownloadSourceSettingsViewData data = new(config);

        Assert.Equal(DownloadSourceSettingConfig.SourceModelScope, data.SelectedSource.Key);
        Assert.Equal("ms", data.ModelScopeToken);
        Assert.False(data.IsCustomSource);
    }

    [Fact]
    public void Changes_WriteBackTrimmed()
    {
        DownloadSourceSettingConfig config = new();
        DownloadSourceSettingsViewData data = new(config);

        data.SelectedSource = data.SourceOptions.Single(x => x.Key == DownloadSourceSettingConfig.SourceHfMirror);
        data.HuggingFaceToken = " hf_x ";
        data.GitHubProxyPrefix = "https://proxy.example/ ";

        Assert.Equal(DownloadSourceSettingConfig.SourceHfMirror, config.ModelSource);
        Assert.Equal("hf_x", config.HuggingFaceToken);
        Assert.Equal("https://proxy.example/", config.GitHubProxyPrefix);
    }

    [Fact]
    public void CustomEndpoint_OnlyForCustom_AndFlagsBadAddress()
    {
        DownloadSourceSettingsViewData data = new(new DownloadSourceSettingConfig());
        data.CustomEndpoint = "not a url";
        Assert.False(data.IsCustomEndpointInvalid);

        data.SelectedSource = data.SourceOptions.Single(x => x.Key == DownloadSourceSettingConfig.SourceCustom);
        Assert.True(data.IsCustomSource);
        Assert.True(data.IsCustomEndpointInvalid);

        data.CustomEndpoint = "https://hf.example.com";
        Assert.False(data.IsCustomEndpointInvalid);
    }
}
