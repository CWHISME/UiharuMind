/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 内容流里的思考边界：每一段内容对思考段而言是延续、终结还是无关。
///
/// <para><b>全仓唯一的分类入口</b>：渲染（转录器）、计时器、快捷窗口、存档回放与搜索都走这里。
/// 判定只认 <c>AIContent</c> 的类型——SDK 把 <c>reasoning_content</c> 解成
/// <see cref="TextReasoningContent"/>、把 <c>content</c> 解成 <see cref="TextContent"/>，
/// 这就是唯一的事实。<b>不从文本内容猜「这段看起来像不像思考」</b>：推理续句与正常回答
/// 在字节上无从区分，任何启发式都会在某个形状上把整段回答吞进思考卡。</para>
///
/// <para>parser 由调用方持有（流式扣留状态不能共享，各转各的），但规则只有这一份；
/// 段边界统一在这里冲刷扣留内容并复位，各消费方的 <paramref name="onBreak"/> 只管收段。</para>
/// </summary>
public static class ThinkingBoundary
{
    /// <summary>
    /// 这一段内容是否把当前的思考段收了口。
    ///
    /// <b>名单只有这一份</b>：流式侧（转录器、计时器、快捷窗口）与存档侧
    /// （<see cref="ThinkingSegments"/>、<see cref="Search.SessionSearch"/>）都问它。
    /// 名单不一致的话，同一条消息会被切出与卡片对不上的段数——存档说有 2 段、
    /// 界面只画了 1 张卡，那张卡就永远认不到耗时。
    ///
    /// <para><c>MessageBoundaryContent</c> 是「一次服务调用收口」的信号，可能异步迟到
    /// （<c>HarnessCharacterRunner</c> 实测落盘晚于下一次调用首帧）。它只收段：段边界的
    /// <c>parser.Complete</c> 不复位「本发已给出结构化思考」这个调用级事实，所以迟到也不会
    /// 把下一发正文里的标签引用重新当成控制标签。</para>
    ///
    /// <para><c>ServiceCallStartedContent</c> 是发请求前同步给出的新发起点，不迟到，
    /// 是全仓唯一完整复位解析器的地方——不清状态会跨发残留，靠落盘信号清又会清掉下一发的状态。</para>
    /// </summary>
    /// <param name="content">内容流里的一段</param>
    /// <returns>收口为 true</returns>
    public static bool EndsThinking(AIContent content) =>
        content is FunctionCallContent or MessageBoundaryContent or ServiceCallStartedContent
            or ToolApprovalRequestContent or UserMessageContent;

    /// <summary>
    /// 按思考边界分发一段内容：思考增量走 <paramref name="onThinking"/>，
    /// 正文原文走 <paramref name="onText"/>，终结思考段的走 <paramref name="onBreak"/>，
    /// 其余（用量/工具结果/子会话通知/错误）三方都不叫。
    ///
    /// <para>结构化的非空思考到达时关掉<b>本发</b>的内联标签解析：思考已由框架分好类，
    /// 正文里再出现 &lt;think&gt; 只能是引用。段边界只收段、不复位这个开关，工具调用之后
    /// 与迟到的落盘信号之后也一样。空思考与只带密文的加密思考不携带文本，
    /// 不代表「框架已用文本表达思考」，不改变开关。</para>
    /// </summary>
    /// <param name="content">内容流里的一段</param>
    /// <param name="parser">&lt;think&gt; 解析器（调用方持有）</param>
    /// <param name="onText">正文回调（转录器拿去上屏，计时器直接终结思考段）</param>
    /// <param name="onThinking">思考回调</param>
    /// <param name="onBreak">终结思考段（转录器是收段，计时器是收段计时）</param>
    public static void Dispatch(AIContent content, ThinkTagStreamParser parser,
        Action<string> onText, Action<string> onThinking, Action onBreak)
    {
        switch (content)
        {
            case TextReasoningContent { Text.Length: > 0 } reasoning:
                parser.DisableInlineTags();
                onThinking(reasoning.Text);
                break;
            case TextContent { Text.Length: > 0 } text:
                // 本地/部分远程模型把 <think> 混在正文流里，经解析器分离
                parser.Feed(text.Text, onText, onThinking);
                break;
            // 可靠的新发起点：先把上一发扣留的半截标签定夺掉，再完整复位调用级状态。
            // 落盘信号会迟到，不能拿它重开内联标签，也不能不清（换模型后会永久残留）
            case ServiceCallStartedContent:
                parser.Complete(onText, onThinking);
                parser.Reset();
                onBreak();
                break;
            case var ending when EndsThinking(ending):
                // 先把扣留的半截标签定夺掉（只收段，不复位调用级开关），所有消费方同一时点
                parser.Complete(onText, onThinking);
                onBreak();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 收尾当前流段：先把解析器扣留的半截标签定夺，再终结思考段。
    /// 供流末尾/失败路径调用；<see cref="Dispatch"/> 遇到段边界时已内部完成同样两步。
    /// </summary>
    /// <param name="parser">&lt;think&gt; 解析器（调用方持有）</param>
    /// <param name="onText">正文回调</param>
    /// <param name="onThinking">思考回调</param>
    /// <param name="onBreak">终结思考段</param>
    public static void Complete(ThinkTagStreamParser parser,
        Action<string> onText, Action<string> onThinking, Action onBreak)
    {
        parser.Complete(onText, onThinking);
        onBreak();
    }
}
