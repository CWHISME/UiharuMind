using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// Command Code（OpenAI/Anthropic 兼容聚合网关）。候选不写死变体表，
/// 走运行时模型目录（<see cref="ModelCatalogUrl"/>，ADR 0092）。
/// </summary>
[SettingConfigDesc("Command Code")]
[SettingConfigDesc("Command Code", LanguageUtils.ChineseSimplified)]
public class RemoteCommandCodeModelConfig : BaseRemoteModelConfig, IRemoteModelConfig
{
    public override string ModelName { get; set; } = "CommandCode";

    public override string ModelPath { get; set; } = "https://api.commandcode.ai/provider/v1/chat/completions";

    public override string WebsiteUrl { get; set; } = "https://commandcode.ai";

    public override string ModelCatalogUrl { get; set; } = "https://api.commandcode.ai/provider/v1/models";

    public override string ModelDescription { get; set; } = "";

    /// <summary>无默认模型：目录拉下来让用户挑</summary>
    public override string ModelId { get; set; } = "";

    public override int Port { get; set; }
}
