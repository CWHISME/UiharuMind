/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 子代理这一轮的渲染落点。它只攒交给主代理的报告，<b>自己不认识界面</b>——
/// 打开着的子会话窗口看到的实时内容，来自 <c>TurnDriver</c> 在
/// <c>ChatSession.LiveTurn</c> 上开的那个分岔口（同一条流的另一个订阅者），
/// 与本类无关。
///
/// <para><b>两条职责</b>：一是把交给主代理的报告攒出来；二是记住<b>本轮还没落盘的正文</b>，
/// 取消时交给 <c>TurnDriver</c> 补进历史。</para>
///
/// <para>第二条<b>不靠服务调用边界收口</b>：从前只在 <see cref="CloseSegment"/> 里清，
/// 而驱动者的 <c>CloseSegment</c> 一整个 turn 才来一次（工具往返都在框架的函数调用循环里跑），
/// 缓冲于是攒着整轮的正文；取消时 <c>TurnDriver.SettleInterruptedTurn</c> 取走的语义是
/// 「正在流的那一段」，攒了整轮就等于往历史末尾补写一条把几十条正文拼在一起的假回复
/// ——实机 5075 字、68 段，重开会话看着像一条真实回复，还会被当成真实回复回填给模型。</para>
///
/// <para>但改成「服务调用边界处清空」同样不行：本仓对边界的到达位置有两种互相矛盾的记载
/// （<c>SessionChatHistoryProvider</c> 是先落盘再发边界，而 <c>TurnDriver</c> 与
/// <c>UsageCallTracker</c> 都记着「实测可能晚于下一次调用的首条内容」）。真晚到时，
/// 清空会把下一发已经流出来的首段正文丢掉，取消后交上去的就是一句从中间开始的半截——
/// 那是丢内容，比多写一条重复更糟。</para>
///
/// <para>所以这里是<b>按累积语义</b>：正文只追加，服务调用边界处不清（重发与轮末收段会清，
/// 那两处都是「这一发已作废」或「本轮结束了」）；去重交给写历史前那道
/// <c>TrimPersistedPrefix</c>——它按本轮已落盘的助手正文把前面削掉，失错方向是
/// 「多写」（能看出来、能再删），不是「少写」。
/// 顺带一句：缓冲里可能带着<b>本轮之前</b>就没落盘的残留（下层重发过、而重发标记没传到这里的那些），
/// 时间闸门拦不住它们，同样归到「多写」那一侧。</para>
/// </summary>
internal sealed class SubAgentTurnSink : ITurnSink
{
    private readonly StringBuilder _streaming = new(); //本轮攒下的正文,取消时由 TurnDriver 取走并削掉已落盘的那截
    private SubAgentTool.ReportAccumulator.ReportMark _mark; //本次服务调用开始时报告的位置,重发要退回它

    /// <summary>报告累加器</summary>
    public SubAgentTool.ReportAccumulator Report { get; } = new();

    /// <summary>本轮是否出现过用户插话(报告里要交代,否则主代理会把它当成自己的委派结果)</summary>
    public bool SawUserInterjection { get; private set; }

    /// <summary>本轮是否出现过派活方插话(同上,但来源不同,报告里分开交代)</summary>
    public bool SawParentInterjection { get; private set; }

    public void Apply(AIContent content)
    {
        // 这一发整段重来：把它写进报告的那截退回去，正在流的正文也丢掉，
        // 否则报告里同一段话会出现两遍
        if (content is StreamRestartContent)
        {
            Report.RollbackTo(_mark);
            _streaming.Clear();
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
        // 正文缓冲不动——见类注释：边界不一定长在这一发正文的尾巴上

        if (content is TextContent { Text.Length: > 0 } text) _streaming.Append(text.Text);
        if (content is UserMessageContent { IsInterjection: true, Message: { } incoming })
        {
            if (ChatMessageAnnotations.IsParentInterjection(incoming)) SawParentInterjection = true;
            else SawUserInterjection = true;
        }
    }

    public void CloseSegment() => _streaming.Clear();

    public void StopRunningToolCalls(string note)
    {
    }

    public string? TakeStreamingText()
    {
        if (_streaming.Length == 0) return null;
        string text = _streaming.ToString();
        _streaming.Clear();
        return text;
    }
}
