/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 子代理这一轮的渲染落点。它只攒交给主代理的报告，<b>自己不认识界面</b>——
/// 打开着的子会话窗口看到的实时内容，来自 <c>TurnDriver</c> 在
/// <c>ChatSession.LiveTurn</c> 上开的那个分岔口（同一条流的另一个订阅者），
/// 与本类无关。
///
/// <para>半截回复的补存不在本类：取消/失败收尾由 <c>TurnDriver</c> 直接用流式缓冲
/// （<c>InterruptedStreamSaver</c>）负责，这里只攒报告。</para>
/// </summary>
internal sealed class SubAgentTurnSink : ITurnSink
{
    private SubAgentTool.ReportAccumulator.ReportMark _mark; //本次服务调用开始时报告的位置,重发要退回它

    /// <summary>报告累加器</summary>
    public SubAgentTool.ReportAccumulator Report { get; } = new();

    /// <summary>本轮是否出现过用户插话(报告里要交代,否则主代理会把它当成自己的委派结果)</summary>
    public bool SawUserInterjection { get; private set; }

    /// <summary>本轮是否出现过派活方插话(同上,但来源不同,报告里分开交代)</summary>
    public bool SawParentInterjection { get; private set; }

    public void Apply(AIContent content)
    {
        // 这一发整段重来：把它写进报告的那截退回去，否则报告里同一段话会出现两遍
        if (content is StreamRestartContent)
        {
            Report.RollbackTo(_mark);
            return;
        }

        // 可靠的新发起点：报告从这里重新起算（重发时退回的就是这里）。
        // 落盘信号会迟到，拿它记位置会把重发范围推到下一发的内容之后
        if (content is ServiceCallStartedContent)
        {
            _mark = Report.Mark();
            return;
        }

        Report.Add(content);

        if (content is UserMessageContent { IsInterjection: true, Message: { } incoming })
        {
            if (ChatMessageAnnotations.IsParentInterjection(incoming)) SawParentInterjection = true;
            else SawUserInterjection = true;
        }
    }

    public void CloseSegment()
    {
    }

    public void StopRunningToolCalls(string note)
    {
    }
}
