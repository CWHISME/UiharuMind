using System;
using UiharuMind.Core.AI.Models;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models;

/// <summary>
/// 模型 ID 候选条目,携带该模型的预设能力(是否支持视觉、默认上下文)
/// </summary>
public sealed class ModelIdOptionItem
{
    /// <summary>
    /// 模型 ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 显示别名;为空时下拉框回退显示 <see cref="Id"/>
    /// </summary>
    public string Alias { get; init; } = "";

    /// <summary>
    /// 下拉框显示的文本
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(Alias) ? Id : Alias;

    /// <summary>
    /// 是否支持视觉
    /// </summary>
    public bool IsVision { get; init; }

    /// <summary>
    /// 默认上下文长度,0 表示未预设
    /// </summary>
    public int ContextLength { get; init; }

    /// <summary>
    /// 默认最大输出预算,0 表示未预设
    /// </summary>
    public int MaxTokens { get; init; }

    /// <summary>
    /// 思考模式下带 tool_calls 时,该模型是否要求原样带回 reasoning_content
    /// </summary>
    public bool RequiresReasoningContentRoundtrip { get; init; }

    /// <summary>
    /// 该模型是否默认不发送采样参数(temperature 等固定参数的模型,如 Kimi)
    /// </summary>
    public bool OmitSamplingParams { get; init; }

    /// <summary>
    /// 下拉框显示的文本
    /// </summary>
    public override string ToString() => Id;
}

/// <summary>
/// 服务商选择列表条目
/// </summary>
public sealed class ProviderItem
{
    /// <summary>
    /// 显示名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 默认接口地址
    /// </summary>
    public required string DefaultEndpoint { get; init; }

    /// <summary>
    /// 服务商官网/开放平台地址,空表示无固定官网(如自定义配置)
    /// </summary>
    public string WebsiteUrl { get; init; } = string.Empty;

    /// <summary>
    /// 对应的配置类型
    /// </summary>
    public required Type ConfigType { get; init; }
}

/// <summary>
/// 思考档位下拉条目:携带枚举值与本地化显示名
/// </summary>
public sealed class ThinkingModeOptionItem
{
    public ThinkingModeOptionItem(EThinkingMode mode)
    {
        Mode = mode;
        Display = Loc.Text(mode switch
        {
            EThinkingMode.None => LangKey.ThinkingModeNone,
            EThinkingMode.Light => LangKey.ThinkingModeLight,
            EThinkingMode.Medium => LangKey.ThinkingModeMedium,
            EThinkingMode.High => LangKey.ThinkingModeHigh,
            EThinkingMode.Max => LangKey.ThinkingModeMax,
            _ => LangKey.ThinkingModeDefault,
        });
    }

    /// <summary>
    /// 思考档位
    /// </summary>
    public EThinkingMode Mode { get; }

    /// <summary>
    /// 本地化显示名
    /// </summary>
    public string Display { get; }

    /// <summary>
    /// 下拉框默认显示的文本
    /// </summary>
    public override string ToString() => Display;
}
