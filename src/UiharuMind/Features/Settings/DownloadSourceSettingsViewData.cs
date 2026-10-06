using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 模型源下拉的一项
/// </summary>
/// <param name="Key">配置里存的值</param>
/// <param name="DisplayName">显示名</param>
public sealed record ModelSourceOption(string Key, string DisplayName);

/// <summary>
/// 「本地模型」页的下载源一节：模型源、自定义地址、两个令牌、GitHub 代理前缀，变更即存
/// </summary>
public partial class DownloadSourceSettingsViewData : ObservableObject
{
    private readonly DownloadSourceSettingConfig _config;
    private readonly SettingsWriteBack _writeBack;

    [ObservableProperty] private ModelSourceOption _selectedSource;
    [ObservableProperty] private string _customEndpoint;
    [ObservableProperty] private string _huggingFaceToken;
    [ObservableProperty] private string _modelScopeToken;
    [ObservableProperty] private string _gitHubProxyPrefix;

    public DownloadSourceSettingsViewData() : this(DownloadSourceSettingConfig.Current)
    {
    }

    /// <param name="config">下载源设置（测试传独立实例）</param>
    public DownloadSourceSettingsViewData(DownloadSourceSettingConfig config)
    {
        _config = config;
        _writeBack = new SettingsWriteBack(config.Save);
        SourceOptions =
        [
            new(DownloadSourceSettingConfig.SourceHuggingFace, "HuggingFace"),
            new(DownloadSourceSettingConfig.SourceHfMirror, Loc.Text(LangKey.DownloadSourceHfMirror)),
            new(DownloadSourceSettingConfig.SourceModelScope, Loc.Text(LangKey.ModelSourceModelScope)),
            new(DownloadSourceSettingConfig.SourceCustom, Loc.Text(LangKey.DownloadSourceCustom))
        ];
        using (_writeBack.BeginLoad())
        {
            _selectedSource = SourceOptions.FirstOrDefault(x => x.Key == config.ModelSource) ?? SourceOptions[0];
            _customEndpoint = config.CustomEndpoint;
            _huggingFaceToken = config.HuggingFaceToken;
            _modelScopeToken = config.ModelScopeToken;
            _gitHubProxyPrefix = config.GitHubProxyPrefix;
        }
    }

    /// <summary>
    /// 可选的模型源
    /// </summary>
    public IReadOnlyList<ModelSourceOption> SourceOptions { get; }

    /// <summary>
    /// 选了自定义才显示地址行
    /// </summary>
    public bool IsCustomSource => SelectedSource.Key == DownloadSourceSettingConfig.SourceCustom;

    /// <summary>
    /// 自定义地址填了但不是合法网址（此时实际回落到 HuggingFace）
    /// </summary>
    public bool IsCustomEndpointInvalid => IsCustomSource && CustomEndpoint.Trim().Length > 0 &&
                                           !Uri.TryCreate(CustomEndpoint.Trim(), UriKind.Absolute, out _);

    partial void OnSelectedSourceChanged(ModelSourceOption value)
    {
        _config.ModelSource = value.Key;
        _writeBack.Save();
        OnPropertyChanged(nameof(IsCustomSource));
        OnPropertyChanged(nameof(IsCustomEndpointInvalid));
    }

    partial void OnCustomEndpointChanged(string value)
    {
        _config.CustomEndpoint = value.Trim();
        _writeBack.Save();
        OnPropertyChanged(nameof(IsCustomEndpointInvalid));
    }

    partial void OnHuggingFaceTokenChanged(string value)
    {
        _config.HuggingFaceToken = value.Trim();
        _writeBack.Save();
    }

    partial void OnModelScopeTokenChanged(string value)
    {
        _config.ModelScopeToken = value.Trim();
        _writeBack.Save();
    }

    partial void OnGitHubProxyPrefixChanged(string value)
    {
        _config.GitHubProxyPrefix = value.Trim();
        _writeBack.Save();
    }
}
