/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 子代理工具:主 agent 把大范围的探查/调研任务委派给一个只读子代理,
/// 结论以报告回到工具返回值,过程不吃主上下文。
///
/// 同步阻塞而非后台并发,原因有三:本地模型是单 slot(llama-server 未传 <c>-np</c>),
/// 并发只会在服务端排队;同步天然满足"等它跑完"这条,不需要框架的
/// <c>LoopEvaluators</c> 与任务台账;取消令牌能一路传进子代理,用户点停止能真停。
///
/// 权限继承主代理的档位:完全自动档下子代理拿到写文件/shell/MCP 并全自动放行,
/// 只读与自动编辑档下只拿只读工具。理由是子代理<b>没有审批通道</b>——它在主 agent
/// 的一次工具调用内部无头运行,而现有审批往返靠"结束本轮再带回应重跑",
/// 同步阻塞在工具里做不到。给它一个必然要问用户的工具,等于给一把静默失效的工具:
/// 框架遇到 <c>ApprovalRequiredAIFunction</c> 不会执行它,只产出一条无人回应的审批请求。
///
/// 不变量:子代理工具集<b>绝不含本工具自身</b>(无限递归,本地模型下直接卡死),
/// 也不含主代理特有的那批(技能/定时任务/记忆检索);非完全自动档下必须只读。均由测试钉住。
/// </summary>
public static class SubAgentTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量,写死字面量迟早对不上</summary>
    public const string ToolName = "RunSubAgent";

    /// <summary>
    /// 子代理的工具循环轮次上限(传给框架的 <c>MaximumIterationsPerRequest</c>,
    /// 到顶即停止循环并把已有进展作为响应返回,不抛异常)。
    ///
    /// 存在的理由是<b>无人值守</b>:定时任务到点后没人看着,一个死循环的子代理会一直烧本地模型。
    /// 交互场景下用户看得见嵌套过程、也按得动停止,不靠这条兜底。
    /// </summary>
    public const int MaxIterations = 16;

    /// <summary>子代理单次运行的墙钟上限,同为无人值守兜底</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 创建子代理 AIFunction
    /// </summary>
    /// <param name="handleFactory">
    /// 构建一个全新子代理。每次调用都重新构建:装配是纯内存组装代价可忽略,
    /// 而 shell 执行器是有生命周期的资源——句柄用 <see cref="AgentHandle"/>
    /// 正是为了让本工具在 finally 里把它释放掉,漏了就是每次委派泄一个 shell 进程。
    /// </param>
    /// <param name="roster">可点名的子智能体(角色挂的那份名单);为空则只有通用子代理</param>
    /// <param name="activitySink">过程上报口(挂到执行者本轮的输出通道);为空则过程不外显</param>
    /// <returns>工具实例</returns>
    public static AITool Create(Func<string?, AgentHandle> handleFactory,
        IReadOnlyList<SubAgentChoice> roster, Action<AIContent>? activitySink)
    {
        // 刻意没有"自定义子代理提示词"这个参数。曾经有过,实测本地模型往里填的是与 task 重复的
        // 泛泛套话(给一个代码项目写"调查团队成员、截止日期、资源分配"),既没信息量又挤掉了
        // 固定段该起的作用。要给子代理换人格,请在角色上挂一个子智能体,而不是让模型现编。
        string description =
            "Delegate an investigation to a sub-agent and get back a focused report. "
            + "Use it for broad exploration (surveying many files, researching a topic on the web) "
            + "so the raw material never enters your own context. The sub-agent runs to completion "
            + "before this returns. It has the same permissions as you, minus anything that would "
            + "need approval — it cannot stop to ask, so put everything it needs in the task.";
        if (roster.Count > 0)
        {
            StringBuilder sb = new(description);
            sb.AppendLine();
            sb.AppendLine("Available sub-agents (pass one of these names as `agent`, "
                          + "or omit it for a general-purpose one):");
            foreach (SubAgentChoice choice in roster)
            {
                sb.AppendLine($"- {choice.Name}: {choice.Description}");
            }

            description = sb.ToString().TrimEnd();
        }

        return AIFunctionFactory.Create(
            async ([Description("The task for the sub-agent: what to find out, over what scope, "
                                + "and what the report should contain.")]
                string task,
                [Description("Which sub-agent to delegate to. Omit for a general-purpose one.")]
                string? agent = null,
                CancellationToken cancellationToken = default) =>
                await RunAsync(handleFactory, activitySink, task, agent, cancellationToken)
                    .ConfigureAwait(false),
            ToolName,
            description);
    }

    private static async Task<string> RunAsync(Func<string?, AgentHandle> handleFactory,
        Action<AIContent>? activitySink, string task, string? agent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task)) return "Error: task must not be empty.";

        // 工具体内取自己的调用标识:过程内容据此挂到界面上对应的那张工具卡片下
        string callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId ?? string.Empty;
        Action<AIContent>? sink = callId.Length > 0 ? activitySink : null;

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Timeout);

        // shell 执行器随句柄释放:子代理在完全自动档下会拿到 run_shell
        await using AgentHandle handle = handleFactory(agent);
        AgentSession session = await handle.Agent.CreateSessionAsync(timeoutSource.Token).ConfigureAwait(false);

        ReportAccumulator report = new();
        bool timedOut = false;
        try
        {
            await foreach (AgentResponseUpdate update in handle.Agent
                               .RunStreamingAsync(task, session, cancellationToken: timeoutSource.Token)
                               .ConfigureAwait(false))
            {
                foreach (AIContent content in update.Contents)
                {
                    sink?.Invoke(new ToolActivityContent(callId, content));
                    report.Add(content);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 只有超时才在此收口(外层取消是用户点了停止,应当继续向上抛)
            timedOut = true;
        }

        return report.Build(timedOut);
    }

    /// <summary>
    /// 从子代理的内容流里提取报告。
    ///
    /// 报告 = <b>最后一次工具调用之后</b>的正文,而非全程正文拼接。
    /// 框架默认工作循环明确要求 agent "explain what you learned and what you are going to do next
    /// between tool calls",于是全程正文里绝大部分是"我接下来去看 X"这类旁白。
    /// 把它们拼起来交给主 agent 有两个坏处:等于把子代理的思考过程塞回主上下文
    /// (正是委派要避免的那件事);旁白里的中间猜测常与最终结论相反,读起来自相矛盾。
    ///
    /// 抽成独立类型是为了能不起模型地单测——这段取舍不写测试就会在下次重构里被"顺手简化"掉。
    /// </summary>
    internal sealed class ReportAccumulator
    {
        private readonly StringBuilder _report = new(); //最后一次工具调用之后的正文
        private readonly StringBuilder _allText = new(); //全程正文,仅在报告为空时兜底

        /// <summary>
        /// 喂入一段内容
        /// </summary>
        /// <param name="content">来自子代理内容流的一段</param>
        public void Add(AIContent content)
        {
            switch (content)
            {
                case FunctionCallContent:
                    // 到此为止的正文都是"我接下来要查什么"的旁白,不是报告
                    _report.Clear();
                    break;
                // 只取正文:思考段属过程,永不进主 agent 的上下文
                case TextContent { Text.Length: > 0 } text:
                    _report.Append(text.Text);
                    _allText.Append(text.Text);
                    break;
            }
        }

        /// <summary>
        /// 生成交给主 agent 的报告
        /// </summary>
        /// <param name="timedOut">本次运行是否因超时被掐断</param>
        /// <returns>报告文本</returns>
        public string Build(bool timedOut)
        {
            StringBuilder result = new();
            if (_report.Length > 0)
            {
                result.Append(_report.ToString().Trim());
            }
            else if (_allText.Length > 0)
            {
                // 收尾总结缺失(轮次到顶/超时/被截断):给出全程旁白,但要说清它不是结论,
                // 否则主 agent 会把中间猜测当成子代理的判断
                result.AppendLine("(No final report - the sub-agent stopped before summarizing. "
                                  + "Below is its running commentary, not a conclusion.)");
                result.Append(_allText.ToString().Trim());
            }

            if (timedOut)
            {
                if (result.Length > 0) result.AppendLine();
                result.Append($"(sub-agent stopped: exceeded its {Timeout.TotalMinutes:0} minute time limit)");
            }

            return result.Length == 0 ? "(sub-agent returned no report)" : result.ToString();
        }
    }
}

/// <summary>
/// 名单里的一个子智能体：给模型看的名字与一句用途。
/// </summary>
/// <param name="Name">子智能体名(模型按这个名字点名)</param>
/// <param name="Description">用途;空描述的子智能体模型无从判断该不该派给它</param>
public sealed record SubAgentChoice(string Name, string Description);
