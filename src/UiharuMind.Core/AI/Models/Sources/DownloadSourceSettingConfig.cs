using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 下载源设置：模型从哪个模型源拿，引擎包走不走 GitHub 代理
/// </summary>
public class DownloadSourceSettingConfig : TConfigBase<DownloadSourceSettingConfig>
{
    public const string SourceHuggingFace = "HuggingFace";
    public const string SourceHfMirror = "HfMirror";
    public const string SourceCustom = "Custom";
    public const string SourceModelScope = "ModelScope";

    public const string HuggingFaceEndpoint = "https://huggingface.co";
    public const string HfMirrorEndpoint = "https://hf-mirror.com";

    /// <summary>
    /// 当前模型源
    /// </summary>
    public string ModelSource { get; set; } = SourceHuggingFace;

    /// <summary>
    /// 自定义的 HF 兼容地址，ModelSource 为 Custom 时用
    /// </summary>
    public string CustomEndpoint { get; set; } = "";

    /// <summary>
    /// HuggingFace 令牌，HF 与其镜像共用
    /// </summary>
    public string HuggingFaceToken { get; set; } = "";

    /// <summary>
    /// ModelScope 令牌
    /// </summary>
    public string ModelScopeToken { get; set; } = "";

    /// <summary>
    /// GitHub 下载代理前缀（如 https://ghproxy.example/），空为直连；只改写发布包下载地址
    /// </summary>
    public string GitHubProxyPrefix { get; set; } = "";
}
