/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 委派时点档位的字面量：<c>general</c> / <c>explore</c> / <c>review</c>（ADR 0112）。
///
/// 从前这件事是一个 <c>isExplore: bool</c>：加第三档时 bool 就没地方放了（要么再塞一个 bool，
/// 要么让模型去猜组合）。档位名直接给模型看，模型训练里也见过这组词，比布尔更容易用对。
///
/// <b>与工具参数共用一个出处</b>：工具描述里那句「可选值有哪些」由 <see cref="All"/> 拼出来，
/// 解析用的也是同一张表——两边分开写，加档位时必漏一处，而漏的那处不会报错：
/// 模型照旧传老词，落进默认档，行为看起来只是「参数没生效」。
///
/// 别名收中文：模型偶尔把用户原话（「派个评审」）直接塞进参数，直接认比让它重试便宜。
/// 脑不在表里——它有自己的一把工具（<c>Brain</c>），委派不该能派出一个脑。
/// </summary>
public static class SubAgentMode
{
    /// <summary>能干活的通用子代理。默认档</summary>
    public const string General = "general";

    /// <summary>调研/摸底（工具集与权限边界跟通用档一样，见 ADR 0112 修订）</summary>
    public const string Explore = "explore";

    /// <summary>评审（同上：档位只管分工，不管能不能改）</summary>
    public const string Review = "review";

    /// <summary>给模型看的可选值清单，按「常用 → 专用」排</summary>
    public static IReadOnlyList<string> All { get; } = [General, Explore, Review];

    /// <summary>
    /// 把参数值解析成档位。空白 = 没填，落默认档（<see cref="ESubAgentType.General"/>）。
    /// </summary>
    /// <param name="text">模型传进来的值，可能为空</param>
    /// <param name="type">解析出的档位；失败时为 General</param>
    /// <returns>认得出来为 true；空白也算认得（按默认档）</returns>
    public static bool TryParse(string? text, out ESubAgentType type)
    {
        type = ESubAgentType.General;
        string? value = text?.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value)) return true;

        switch (value)
        {
            case General or "通用" or "通用子代理":
                type = ESubAgentType.General;
                return true;
            case Explore or "explorer" or "探索" or "探索档":
                type = ESubAgentType.Explorer;
                return true;
            case Review or "reviewer" or "评审" or "评审档" or "审阅":
                type = ESubAgentType.Reviewer;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 解析失败时回给模型的话。把可选值与别名都写出来——模型看到确切写法才会改，
    /// 只说「参数不合法」它会原样再试一次。
    /// </summary>
    /// <param name="text">它传进来的那个值</param>
    /// <returns>工具结果文本</returns>
    public static string Reject(string? text) =>
        $"Error: 未知的 mode '{text}'。可选：{string.Join(" / ", All)}"
        + "（general 干活、explore 做调研、review 做评审；三档的工具与边界一样）。"
        + "也认中文：通用 / 探索 / 评审。留空等同 general。";
}
