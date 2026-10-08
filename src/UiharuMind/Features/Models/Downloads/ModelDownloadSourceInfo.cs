using System;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 当前下载源的标识与显示名（各分类列表上方的「源：…」）
/// </summary>
public static class ModelDownloadSourceInfo
{
    /// <summary>
    /// 下载源设置的指纹：变了就该换源重来
    /// </summary>
    /// <returns>指纹</returns>
    public static string Key()
    {
        DownloadSourceSettingConfig config = DownloadSourceSettingConfig.Current;
        return $"{config.ModelSource}|{config.CustomEndpoint}|{config.HuggingFaceToken}|{config.ModelScopeToken}";
    }

    /// <summary>
    /// 「源：…」文案
    /// </summary>
    /// <param name="huggingFaceRepos">只认 HuggingFace 仓库名的分类：设成魔搭时实际走 hf-mirror</param>
    /// <returns>文案</returns>
    public static string Label(bool huggingFaceRepos = false) =>
        Loc.Text(LangKey.ModelDownloadSourceLabel, DisplayName(huggingFaceRepos));

    private static string DisplayName(bool huggingFaceRepos)
    {
        DownloadSourceSettingConfig config = DownloadSourceSettingConfig.Current;
        return config.ModelSource switch
        {
            DownloadSourceSettingConfig.SourceHfMirror => "hf-mirror",
            DownloadSourceSettingConfig.SourceModelScope when huggingFaceRepos => "hf-mirror",
            DownloadSourceSettingConfig.SourceModelScope => Loc.Text(LangKey.ModelSourceModelScope),
            DownloadSourceSettingConfig.SourceCustom when Uri.TryCreate(config.CustomEndpoint.Trim(), UriKind.Absolute,
                out Uri? uri) => uri.Host,
            _ => "HuggingFace"
        };
    }
}
