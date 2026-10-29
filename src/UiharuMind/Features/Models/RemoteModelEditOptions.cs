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
    /// 该模型的接口格式（wire），由目录 supported_endpoints 解析（ADR 0092）
    /// </summary>
    public EApiFormat ApiFormat { get; init; }

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
