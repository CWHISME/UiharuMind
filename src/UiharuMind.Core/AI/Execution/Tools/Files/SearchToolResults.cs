/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 搜索工具回给模型的形状：<b>两个都是纯文本</b>（Glob 与 Grep 同口径）。
///
/// 结果要落历史、被序列化再读回，对象会在重放时变成 <c>JsonElement</c>，
/// 其 ToString 吐出紧凑 JSON——换行变 <c>\n</c> 字面量、列表被逗号挤成一行，
/// 模型与界面都读不出原来的行结构。字符串则还原成真实换行。
///
/// 正文与说明因此拼进同一段文本，按 <see cref="ToolResultText.Compose"/> 的排布。
/// 失败、0 命中、自动降级、截断这些说明绝不混进条目本体——从前它是混在命中列表里的假条目
/// （<c>"[Error] ..."</c>、<c>FileName = "[truncated]"</c>），模型分不清"这是一条命中"
/// 还是"这是一句话"（ADR 0017）。"0 条命中"与"没搜成"在文本上依旧能区分。
/// </summary>

/// <summary>
/// 搜索结果文本的公共排布（Glob/Grep 同口径）：规模说明在前，正文居中，收窄建议殿后，段间空一行。
/// 模型先知道搜到多少、是不是正文，再读条目
/// </summary>
internal static class ToolResultText
{
    /// <summary>过宽提示里统一给模型的收窄指引（Glob/Grep 共用，措辞刻意一致，别各写一套）</summary>
    public const string NarrowGuidance = "换更精确的匹配模式，或用 path 参数限定范围";

    /// <summary>
    /// 按「说明、正文、建议」拼成一段，空的段跳过
    /// </summary>
    /// <param name="notice">规模与查询说明</param>
    /// <param name="body">条目正文</param>
    /// <param name="guidance">收窄建议</param>
    /// <returns>拼好的工具结果</returns>
    public static string Compose(string? notice, string body, string? guidance = null) =>
        string.Join("\n\n", new[] { notice, body, guidance }.Where(part => !string.IsNullOrEmpty(part)));
}

/// <summary>
/// 把结构化的失败原因渲染成给<b>模型</b>看的一段话。
///
/// 措辞与提示词散文统一中文（ADR 0017 的口径）：面向中文语料为主的本地小模型，
/// 英文说明最容易被略读，而这里解释的恰是"为什么没搜到/搜得不对"。
/// 工具名（<c>`Read`</c>/<c>`Grep`</c>）与当次真实路径保留原文/原样。
///
/// 每一句里的路径都是<b>当次真实的路径</b>，不是占位符。这是有意的：
/// 提示词里写通用示例会被小模型照抄（实机见过它编出 <c>root: "/path/to/project"</c>），
/// 而这里的"示例"由真实的工作目录与真实的入参拼成，没有占位符可抄；
/// 而且它只在出错那一次出现，常驻成本为零——这正是不把调用示例写进系统提示的原因。
/// </summary>
internal static class SearchFailureRenderer
{
    /// <summary>
    /// 渲染失败原因
    /// </summary>
    /// <param name="failure">结构化失败原因</param>
    /// <param name="literalToolName">"没有通配符"时该改用的字面查找工具名</param>
    /// <returns>给模型的说明文本</returns>
    public static string Render(SearchFailure failure, string literalToolName)
    {
        return failure.Kind switch
        {
            ESearchFailureKind.PathNotFound => PathNotFound(failure),
            ESearchFailureKind.InvalidPath => failure.Detail,
            ESearchFailureKind.InvalidGlobPattern =>
                $"无效的 glob 模式 \"{failure.Pattern}\"：{failure.Detail} "
                + "模式形如 \"**/*.cs\" 或 \"src/**/Foo*\"。",
            ESearchFailureKind.GlobHasNoWildcard =>
                $"\"{failure.Pattern}\" 没有通配符，所以无可 glob，"
                + $"并且 \"{failure.ResolvedDirectory}\" 下没有这个路径。"
                + $"读已知路径用 `{FileToolNames.Read}`；"
                + $"在文件里找文字用 `{literalToolName}`；"
                + "要 glob 就在模式里加通配符，如 \"**/"
                + $"{Path.GetFileName(failure.Pattern)}\"。",
            ESearchFailureKind.EngineFailed =>
                $"搜索引擎在处理 \"{failure.Pattern}\" 时失败：{failure.Detail}",
            _ => failure.Detail,
        };
    }

    /// <summary>
    /// 搜索范围（目录或单文件）不存在。<b>必须回显解析后的绝对路径</b>：模型看不到自己那个相对路径
    /// 被拼成了哪里，就只能再猜一次——这正是"试几次才找到正确用法"的机制。
    /// </summary>
    private static string PathNotFound(SearchFailure failure)
    {
        if (string.IsNullOrWhiteSpace(failure.RequestedDirectory))
        {
            return $"工作目录 \"{failure.WorkingDirectory}\" 不存在。";
        }

        // 有最近存活祖先就点一句：让模型从"整个路径都不对"收敛到"断在某一段"，不猜候选。
        // 不回显工作目录：resolved 已是拼好的绝对路径，隐含了拼接基准。
        string ancestor = string.IsNullOrEmpty(failure.NearestExistingDirectory)
            ? string.Empty
            : $" 最近的存活祖先目录是 \"{failure.NearestExistingDirectory}\"。";

        return $"找不到路径。你传的路径 \"{failure.RequestedDirectory}\" 解析为 \"{failure.ResolvedDirectory}\"。"
               + ancestor
               + " 传目录（在其下搜索）、单文件（只搜该文件），或省略 path 搜索整个工作目录。";
    }
}
