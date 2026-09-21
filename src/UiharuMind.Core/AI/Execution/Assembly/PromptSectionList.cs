/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 一串按顺序登记的提示词段落。<b>存在的理由是让"段序"成为一张可读的表</b>，
/// 而不是两处 <see cref="StringBuilder"/> 的调用顺序。
///
/// 从前主代理与子代理各手写一套编排，于是同一条设计原则在两边说了两件事：
/// 主代理侧注释写着"工作目录排在最前，后面每一段纪律都以路径怎么写为前提"，
/// 子代理侧却把工作目录放在整段 shell 纪律<b>之后</b>。这种漂移没有任何测试拦得住——
/// 两段散文谁也不知道对方说了什么。现在两档共用 <see cref="ToolDisciplineSections"/>
/// 那一张清单，顺序只有一处定义。
///
/// 段与段之间空一行；条件不成立、正文为空的段直接不登记（空标题是纯噪声）。
/// </summary>
internal sealed class PromptSectionList
{
    private readonly StringBuilder _sb = new();

    /// <summary>还没有任何段落登记进来</summary>
    public bool IsEmpty => _sb.Length == 0;

    /// <summary>
    /// 登记一段带标题的段落。
    /// </summary>
    /// <param name="when">出现条件；为 false 则整段跳过</param>
    /// <param name="heading">段落标题（含级别前缀）</param>
    /// <param name="body">段落正文；空白则整段跳过</param>
    /// <returns>自身，便于链式登记</returns>
    public PromptSectionList Section(bool when, string heading, string? body)
    {
        if (!when || string.IsNullOrWhiteSpace(body)) return this;
        return Raw(true, $"{heading}\n{body.TrimEnd()}");
    }

    /// <summary>
    /// 登记一段带标题的段落，正文<b>延迟求值</b>——条件不成立时连拼都不拼。
    /// 正文里含路径拼接、URI 转换这类开销时用这个重载。
    /// </summary>
    /// <param name="when">出现条件；为 false 则整段跳过，且不调用 <paramref name="body"/></param>
    /// <param name="heading">段落标题（含级别前缀）</param>
    /// <param name="body">取正文的委托</param>
    /// <returns>自身，便于链式登记</returns>
    public PromptSectionList Section(bool when, string heading, Func<string> body)
    {
        return !when ? this : Section(true, heading, body());
    }

    /// <summary>
    /// 登记一段<b>没有标题</b>的文本（护栏句、身份段这类）。
    /// </summary>
    /// <param name="when">出现条件；为 false 则整段跳过</param>
    /// <param name="text">整段文本；空白则跳过</param>
    /// <returns>自身，便于链式登记</returns>
    public PromptSectionList Raw(bool when, string? text)
    {
        if (!when || string.IsNullOrWhiteSpace(text)) return this;
        if (_sb.Length > 0) _sb.Append("\n\n");
        _sb.Append(text.TrimEnd());
        return this;
    }

    /// <summary>拼出整串</summary>
    /// <returns>各段以空行分隔的整段文本；一段都没有时为空串</returns>
    public override string ToString() => _sb.ToString();
}
