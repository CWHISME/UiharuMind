using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 思考模式下带 tool_calls 时,该模型是否要求原样带回思考正文;
    /// <c>null</c>=自动(历史有思考就回传),
    /// <c>true</c>/<c>false</c>=显式要求/禁止
    /// </summary>
    public bool? ReplayExistingReasoning { get; init; }

    /// <summary>
    /// 该模型是否默认不发送采样参数(temperature 等固定参数的模型,如 Kimi)
    /// </summary>
    public bool OmitSamplingParams { get; init; }

    /// <summary>
    /// 该模型声明可用的端点（目录 supported_endpoints 解析而来）；空表示没声明。
    /// 添加模型页据此列出接口地址候选
    /// </summary>
    public IReadOnlyList<EApiFormat> ApiFormats { get; init; } = [];

    /// <summary>
    /// 推荐的接口格式。<b>目录声明优先，且声明里有 chat 就走 chat</b>——
    /// chat 是全仓的基线（响应清洗、请求改写、思考票据、缓存审计都建在它上面），
    /// responses 是附加能力，两边都声明时没有理由离开跑得最熟的那条。
    /// 只有「只声明 responses」的才走 responses；声明里有 messages 的 Claude 走 messages
    /// （CommandCode 文档：Claude 发到 chat 或 responses 直接 400）。
    /// 没声明时才按模型 ID 判：Claude 固定 Messages，其余 chat。
    /// </summary>
    public EApiFormat RecommendedFormat
    {
        get
        {
            if (ApiFormats.Count > 0)
            {
                if (IsClaude && ApiFormats.Contains(EApiFormat.Messages)) return EApiFormat.Messages;
                if (ApiFormats.Contains(EApiFormat.ChatCompletions)) return EApiFormat.ChatCompletions;
                if (ApiFormats.Contains(EApiFormat.Responses)) return EApiFormat.Responses;
                if (ApiFormats.Contains(EApiFormat.Messages)) return EApiFormat.Messages;
                return ApiFormats[0];
            }

            return IsClaude ? EApiFormat.Messages : EApiFormat.ChatCompletions;
        }
    }

    /// <summary>模型 ID 是不是 Claude 系（这类模型走 Anthropic Messages 线）</summary>
    public bool IsClaude => Id.Contains("claude", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 该供应商公布的单价；<c>null</c> 表示没公布（不是免费）
    /// </summary>
    public ModelPrice? Price { get; init; }

    /// <summary>
    /// 该模型声明支持的思考档位（如 <c>low,high,max</c>），空串表示没声明
    /// </summary>
    public string Efforts { get; init; } = "";

    /// <summary>
    /// 候选行里那一小段价格（入/出/缓存读，每百万 token），放不下带单位的完整写法；
    /// 缓存读为 0（不单独计价）时不占位，免得出现 "$0.15/$0.6/$0" 这种误导
    /// </summary>
    public string PriceDisplay => Price is { IsKnown: true } price
        ? price.IsFree ? "free" : ComposePriceDisplay(price)
        : "";

    private static string ComposePriceDisplay(ModelPrice price) =>
        price.CacheRead > 0
            ? $"{ModelPrice.Format(price.Input)}/{ModelPrice.Format(price.Output)}/{ModelPrice.Format(price.CacheRead)}"
            : $"{ModelPrice.Format(price.Input)}/{ModelPrice.Format(price.Output)}";

    /// <summary>
    /// 排序键：没公布价格的排在最后（不能当 0 参与比较，否则"免费"会被排到"未知"后面之后）
    /// </summary>
    /// <param name="sort">排序维度</param>
    /// <returns>比较用的值；无价时 null</returns>
    public decimal? SortKey(EModelPriceSort sort) => Price is { IsKnown: true } price
        ? sort switch
        {
            EModelPriceSort.Input => price.Input,
            EModelPriceSort.Output => price.Output,
            EModelPriceSort.CacheRead => price.CacheRead,
            _ => null
        }
        : null;

    /// <summary>上下文长度的紧凑显示（如 1M / 256K），0 或未预设时为空</summary>
    public string ContextDisplay => ContextLength > 0 ? FormatContext(ContextLength) : "";

    /// <summary>
    /// 下拉框显示的文本
    /// </summary>
    public override string ToString() => Id;

    /// <summary>上下文长度的人类可读格式（基 1024）</summary>
    public static string FormatContext(int contextLength) => contextLength switch
    {
        >= 1_048_576 => $"{contextLength / 1_048_576.0:0.#}M",
        >= 1024 => $"{contextLength / 1024.0:0.#}K",
        > 0 => contextLength.ToString(),
        _ => "",
    };
}

/// <summary>
/// 模型候选的排序维度。<see cref="Default"/> 保持目录/预设给的顺序——
/// 那是供应商自己排的（通常按新旧或推荐度），按价格排只是为了找便宜的模型
/// </summary>
public enum EModelPriceSort
{
    /// <summary>目录原序</summary>
    Default,

    /// <summary>输入单价升序</summary>
    Input,

    /// <summary>输出单价升序</summary>
    Output,

    /// <summary>缓存读单价升序</summary>
    CacheRead
}

/// <summary>
/// 排序下拉条目
/// </summary>
public sealed class ModelSortOption
{
    /// <summary>排序维度</summary>
    public required EModelPriceSort Sort { get; init; }

    /// <summary>本地化显示名</summary>
    public required string Display { get; init; }

    /// <summary>控件没套模板时的兜底显示文本</summary>
    public override string ToString() => Display;
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

    /// <summary>
    /// 服务商的模型目录地址（/v1/models）；空表示无目录（ADR 0092）
    /// </summary>
    public string CatalogUrl { get; init; } = "";
}

/// <summary>
/// 思考档位下拉条目:携带 wire 值与本地化显示名。
/// 值来自模型声明（<see cref="BaseRemoteModelConfig.SupportedThinkingEfforts"/>），
/// 没见过的自定义值（如 <c>xhigh</c>）原样显示——服务端认什么就写什么，不翻译。
/// </summary>
public sealed class ThinkingOptionItem
{
    /// <summary>
    /// 按档位值构造
    /// </summary>
    /// <param name="value">wire 值；空串表示不干预</param>
    public ThinkingOptionItem(string value)
    {
        Value = ThinkingEfforts.Normalize(value);
        Display = ResolveDisplay(Value);
    }

    private static string ResolveDisplay(string value)
    {
        LangKey? key = value switch
        {
            ThinkingEfforts.Auto => LangKey.ThinkingModeDefault,
            ThinkingEfforts.Off => LangKey.ThinkingModeNone,
            "minimal" => LangKey.ThinkingModeMinimal,
            "low" => LangKey.ThinkingModeLight,
            "medium" => LangKey.ThinkingModeMedium,
            "high" => LangKey.ThinkingModeHigh,
            "xhigh" => LangKey.ThinkingModeXHigh,
            "max" => LangKey.ThinkingModeMax,
            _ => null,
        };
        return key is { } known ? Loc.Text(known) : value;
    }

    /// <summary>
    /// 思考档位 wire 值
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// 本地化显示名
    /// </summary>
    public string Display { get; }

    /// <summary>
    /// 下拉框默认显示的文本
    /// </summary>
    public override string ToString() => Display;
}
