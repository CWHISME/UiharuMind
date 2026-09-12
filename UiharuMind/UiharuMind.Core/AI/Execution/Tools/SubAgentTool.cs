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
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Configs;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 子代理工具:主 agent 把大范围的探查/调研任务委派出去,结论以报告回到工具返回值,
/// 过程不吃主上下文。
///
/// <b>一次委派 = 一个真会话</b>(子会话):建 <see cref="ChatSession"/>、进索引、正常落盘,
/// 然后跑<b>它自己的</b> <see cref="TurnDriver"/>——与定时任务的无头轮次同一套编排。
/// 于是落盘、续跑、再对话三件事全部沿用会话的既有能力,不另立实体(见 ADR 0021)。
///
/// <b>审批通道</b>:子代理的审批请求冒到派活者<b>这一轮</b>的回应口
/// (<see cref="ICharacterRunner.SetTurnApprovalResolver"/>)。从前没有这条通道,
/// 是因为这里跑的是没有回环的裸循环,而不是"同步阻塞做不到"——
/// <see cref="TurnDriver"/> 的审批回环从头到尾没离开过那次 await。
///
/// 仍然同步阻塞:主 agent 的这次工具调用等子代理跑完才返回。理由见 ADR 0022
/// (原理由「本地模型单 slot」已失效,现在撑着的是「轮次归属」)。
///
/// 不变量:子代理工具集<b>绝不含本工具自身</b>(无限递归),也不含主代理特有的那批
/// (技能/定时任务/记忆检索);能力取「自己的 ∩ 派活者的」。均由测试钉住。
/// </summary>
public static class SubAgentTool
{
    /// <summary>
    /// 通用子代理的工具名。提示词里提到本工具时一律引用这个常量,写死字面量迟早对不上。
    ///
    /// <b>刻意没有限定词</b>:它与 <see cref="ToolExplorerName"/> 不是两个平等选项,
    /// 而是「默认」与「特例」。无限定名天然读作"一般情况用它",带限定名读作"满足条件才用"——
    /// 这个直觉不必读描述就成立。从前叫 <c>RunGeneralSubAgent</c>,与 <c>RunExploreSubAgent</c>
    /// 一个是类别词、一个是动词,根本不在同一根轴上,模型无从比较,于是一边倒地选了后者。
    /// </summary>
    public const string ToolGeneralName = "RunSubAgent";

    /// <summary>
    /// 探索子代理的工具名。<b>限制写进名字里</b>:这正是要让模型看见的那一点——
    /// 它改不了任何东西,派错了只会白跑一趟。
    /// </summary>
    public const string ToolExplorerName = "RunReadOnlySubAgent";

    /// <summary>续跑/追问工具名。两档子代理共用一个——续跑与派哪一档无关,它认的是子会话</summary>
    public const string ToolContinueName = "ContinueSubAgent";

    /// <summary>
    /// 子代理的工具循环轮次上限(传给框架的 <c>MaximumIterationsPerRequest</c>,
    /// 到顶即停止循环并把已有进展作为响应返回,不抛异常)。
    ///
    /// 存在的理由是<b>无人值守</b>:定时任务到点后没人看着,一个跑偏的子代理会一直烧下去。
    /// 交互场景下用户看得见嵌套过程、也按得动停止,不靠这条兜底。
    /// </summary>
    public const int MaxIterations = 32768;

    /// <summary>
    /// 交互场景的墙钟上限。宽松是因为用户看得见、也按得动停止。
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromHours(24);

    /// <summary>
    /// 无人值守(定时任务)的墙钟上限。<b>与交互档分开定价</b>:那一档没人看着,
    /// 而主力模型已转为远程——本地跑偏烧的是电,远程跑偏是账单事件(见 ADR 0022)。
    /// </summary>
    public static readonly TimeSpan UnattendedTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 一次委派要用到的全部上下文。子会话的字段几乎全部从派活者继承——
    /// 工作目录、权限档、shell 预授权,以及"谁派的"。
    /// </summary>
    public sealed record LaunchContext
    {
        /// <summary>派活者的会话标识(落成子会话的 <c>ParentSessionId</c>)</summary>
        public required string ParentSessionId { get; init; }

        /// <summary>继承的工作目录</summary>
        public string? WorkspacePath { get; init; }

        /// <summary>继承的权限档序号</summary>
        public int PermissionModeIndex { get; init; }

        /// <summary>继承的 shell 预授权模式</summary>
        public IReadOnlyList<string>? PreAuthorizedShellPatterns { get; init; }

        /// <summary>本工具装配成哪一档子代理</summary>
        public required SubAgentProfile Profile { get; init; }

        /// <summary>可点名的子智能体名单;为空则只有通用匿名子代理</summary>
        public required IReadOnlyList<SubAgentChoice> Roster { get; init; }

        /// <summary>派活者本轮的审批回应通道</summary>
        public Func<ApprovalResolver?>? ApprovalSource { get; init; }

        /// <summary>本轮有没有人看着（每轮现取，见 <c>AgentBuildProfile.IsAttendedSource</c>）</summary>
        public Func<bool>? IsAttendedSource { get; init; }

        /// <summary>派活时把子会话标识交给界面</summary>
        public Action<string, string>? SubSessionStarted { get; init; }

        /// <summary>有没有人看着这一跑</summary>
        public bool IsAttended => IsAttendedSource?.Invoke() ?? false;
    }

    /// <summary>
    /// 创建派活工具
    /// </summary>
    /// <param name="context">派活上下文</param>
    /// <returns>工具实例</returns>
    public static AITool Create(LaunchContext context)
    {
        // 刻意没有"自定义子代理提示词"这个参数。曾经有过,实测本地模型往里填的是与 task 重复的
        // 泛泛套话,既没信息量又挤掉了固定段该起的作用。要给子代理换人格,
        // 请在角色上挂一个子智能体,而不是让模型现编。
        string description = context.Profile.Description;
        if (context.Roster.Count > 0)
        {
            StringBuilder sb = new(description);
            sb.AppendLine();
            sb.AppendLine("Available sub-agents (pass one of these names as `agent`, "
                          + "or omit it for a general-purpose one):");
            foreach (SubAgentChoice choice in context.Roster)
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
                await LaunchAsync(context, task, agent, cancellationToken).ConfigureAwait(false),
            context.Profile.ToolName,
            description);
    }

    /// <summary>
    /// 创建续跑/追问工具。两档共用一个:它认的是子会话标识,与当初派的是哪一档无关
    /// （那一档已经落在子会话上了，重建时照它装配）。
    /// </summary>
    /// <param name="context">派活上下文(取其中的过程上报口与审批通道)</param>
    /// <returns>工具实例</returns>
    public static AITool CreateContinueTool(LaunchContext context)
    {
        return AIFunctionFactory.Create(
            async ([Description("The sub-session id returned by a previous delegation.")]
                string subSession,
                [Description("What to ask the sub-agent next: a follow-up question, "
                             + "a correction, or simply an instruction to continue.")]
                string message,
                CancellationToken cancellationToken = default) =>
                await ContinueAsync(context, subSession, message, cancellationToken).ConfigureAwait(false),
            ToolContinueName,
            "Continue an earlier sub-agent delegation: send it another message in the same "
            + "sub-session and get an updated report. Use it to follow up on a report, to correct "
            + "course, or to resume one that stopped before finishing. "
            + "The sub-session keeps everything it did before.");
    }

    private static async Task<string> LaunchAsync(LaunchContext context, string task, string? agent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task)) return "Error: task must not be empty.";

        SubAgentChoice? choice = agent == null
            ? null
            : context.Roster.FirstOrDefault(x => string.Equals(x.Name, agent, StringComparison.OrdinalIgnoreCase));
        if (agent != null && choice == null)
        {
            return $"Error: no sub-agent named '{agent}'. "
                   + (context.Roster.Count == 0
                       ? "No named sub-agents are mounted; omit `agent` for a general-purpose one."
                       : $"Available: {string.Join(", ", context.Roster.Select(x => x.Name))}.");
        }

        ChatSession session = new()
        {
            // 匿名子代理用内置的身份角色,不沿用派活者的——否则子会话窗口会顶着派活者的
            // 名字和头像,看起来像在跟主代理说话。两档各有一张:探索档恒定只读、另配模型,
            // 顶同一个名字用户分不清这次委派能不能改东西。
            // 能力仍然直接取派活者那一份(不经交集,见 SubAgentAssembly.BuildFromPlan)
            CharacterId = choice?.CharacterId ?? AnonymousCharacterOf(context.Profile.Type).ToString(),
            Title = BuildTitle(task),
            Description = task,
            WorkspacePath = context.WorkspacePath,
            PermissionModeIndex = context.PermissionModeIndex,
            PreAuthorizedShellPatterns = context.PreAuthorizedShellPatterns,
            ParentSessionId = context.ParentSessionId,
            SubAgentType = context.Profile.Type,
            SubAgentName = choice?.Name ?? string.Empty,
            SessionModelName = ResolveSubAgentModelName(context.Profile),
        };
        SessionManager.Instance.Add(session);
        NoteStarted(context, session.SessionId);

        return await RunTurnAsync(context, session, task, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ContinueAsync(LaunchContext context, string subSessionId, string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subSessionId)) return "Error: subSession must not be empty.";
        if (string.IsNullOrWhiteSpace(message)) return "Error: message must not be empty.";

        ChatSession? session = SessionManager.Instance.Load(subSessionId);
        if (session == null) return $"Error: no sub-session '{subSessionId}'.";
        // 只允许续自己派出去的那些:子会话是按派活者归属的,跨会话续跑等于绕过能力交集
        if (!string.Equals(session.ParentSessionId, context.ParentSessionId, StringComparison.Ordinal))
        {
            return $"Error: sub-session '{subSessionId}' was not delegated by this session.";
        }

        NoteStarted(context, session.SessionId);
        return await RunTurnAsync(context, session, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把子会话标识交给界面。<b>必须在开跑之前</b>——"跑着的时候点开看看"正是这件事的重点，
    /// 而工具结果里那份标识要等跑完才有
    /// </summary>
    private static void NoteStarted(LaunchContext context, string subSessionId)
    {
        string callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId ?? string.Empty;
        if (callId.Length > 0) context.SubSessionStarted?.Invoke(callId, subSessionId);
    }

    /// <summary>
    /// 在子会话上跑一轮,并把结论收成报告。派活与续跑共用——
    /// 两者的差别只有"会话是新建的还是读回来的"
    /// </summary>
    private static async Task<string> RunTurnAsync(LaunchContext context, ChatSession session, string message,
        CancellationToken cancellationToken)
    {
        TimeSpan limit = context.IsAttended ? Timeout : UnattendedTimeout;
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(limit);

        SubAgentTurnSink turnSink = new();
        bool timedOut = false;
        try
        {
            await session.Runner.AttachAsync(session, timeoutSource.Token).ConfigureAwait(false);

            using TurnDriver driver = new(turnSink, new TurnUsageLedger());
            await driver.RunAsync(session, session.Runner, new ChatMessage(ChatRole.User, message),
                context.ApprovalSource?.Invoke()).ConfigureAwait(false);

            // 代码兜底:模型以工具调用结束、之后没产出文本(没写收尾总结)。
            // 提示层硬约束挡住大多数,这里兜漏网的——追加一轮"请总结"让模型补上报告
            if (turnSink.Report.NeedsSummary && !timeoutSource.Token.IsCancellationRequested)
            {
                using TurnDriver summaryDriver = new(turnSink, new TurnUsageLedger());
                await summaryDriver.RunAsync(session, session.Runner,
                        new ChatMessage(ChatRole.User, "请用一段话总结你的发现和结论，作为最终报告。"),
                        context.ApprovalSource?.Invoke())
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 只有超时才在此收口(外层取消是用户点了停止,应当继续向上抛)
            timedOut = true;
        }
        finally
        {
            // 执行者(含 shell executor)随这次委派释放,不挂到应用退出;
            // 之后用户打开该子会话会重新惰性创建——按同一份持久化身份重建
            await session.DisposeRunnerAsync().ConfigureAwait(false);
        }

        return turnSink.Report.Build(timedOut, limit, session.SessionId,
            cancellationToken.IsCancellationRequested, turnSink.SawUserInterjection);
    }

    /// <summary>
    /// 这次委派该用哪个模型，<b>在派活时刻定死并钉在子会话上</b>（会话覆写）。
    ///
    /// 钉住而不是每次请求现解析，换来两件事：界面显示的模型与实际问话的那个由构造保证一致
    /// （从前装配有自己一条解析链，界面另有一条，两边对不上就是截图里那个"模型显示不对"）；
    /// 以及一个子会话的模型在它整个生命里稳定，用户中途换全局模型不会让续跑换一个脑子。
    ///
    /// 配置的模型拉不起来时返回 null（跟随派活者/全局）——<b>绝不把一个永远不会就绪的模型
    /// 钉上去</b>，那会让惰性客户端死等，表现是 "Model is not running"。
    /// 本地模型无法热切换，于是天然落进这一支。
    /// </summary>
    /// <param name="profile">子代理档</param>
    /// <returns>模型名；跟随派活者时为 null</returns>
    private static string? ResolveSubAgentModelName(SubAgentProfile profile)
    {
        string name = profile.ResolveModelName(AgentSettingConfig.Current);
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!LlmManager.Instance.CacheModelDictionary.TryGetValue(name, out ModelRunningData? configured)) return null;

        ModelRunningData? candidate = configured;
        if (!LlmManager.Instance.TryCheckModelRunning(false, ref candidate)) return null;
        return candidate is { ChatClient: not null } ? candidate.ModelName : null;
    }

    /// <summary>匿名子代理用哪张身份卡</summary>
    private static DefaultCharacter AnonymousCharacterOf(ESubAgentType type) =>
        type == ESubAgentType.Explorer ? DefaultCharacter.ExploreSubAgent : DefaultCharacter.GeneralSubAgent;

    /// <summary>子会话标题:任务首行截断。改名不影响任何引用,标题纯显示</summary>
    private static string BuildTitle(string task)
    {
        string line = task.Trim().Split('\n', 2)[0].Trim();
        const int max = 40;
        return line.Length <= max ? line : line[..max] + "…";
    }

    /// <summary>
    /// 子代理这一轮的渲染落点。它只攒交给主 agent 的报告，<b>自己不认识界面</b>——
    /// 打开着的子会话窗口看到的实时内容，来自 <c>TurnDriver</c> 在
    /// <c>ChatSession.LiveTurn</c> 上开的那个分岔口（同一条流的另一个订阅者），
    /// 与本类无关。
    /// </summary>
    private sealed class SubAgentTurnSink : ITurnSink
    {
        private readonly StringBuilder _streaming = new(); //正在流的那一段正文,取消时由 TurnDriver 取走落库

        /// <summary>报告累加器</summary>
        public ReportAccumulator Report { get; } = new();

        /// <summary>本轮是否出现过用户插话(报告里要交代,否则主 agent 会把它当成自己的委派结果)</summary>
        public bool SawUserInterjection { get; private set; }

        public void Apply(AIContent content)
        {
            Report.Add(content);
            if (content is TextContent { Text.Length: > 0 } text) _streaming.Append(text.Text);
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

        /// <summary>记下用户往子会话里插了话</summary>
        public void NoteUserInterjection() => SawUserInterjection = true;
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
    public sealed class ReportAccumulator
    {
        private readonly StringBuilder _report = new(); //最后一次工具调用之后的正文
        private readonly StringBuilder _allText = new(); //全程正文,仅在报告为空时兜底
        private bool _hadToolCall; //是否出现过至少一次工具调用

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
                    _hadToolCall = true;
                    break;
                // 只取正文:思考段属过程,永不进主 agent 的上下文
                case TextContent { Text.Length: > 0 } text:
                    _report.Append(text.Text);
                    _allText.Append(text.Text);
                    break;
            }
        }

        /// <summary>
        /// 是否有旁白但缺少收尾总结——模型以工具调用结束、之后没产出文本。
        /// 用于代码兜底:追加一轮"请总结"让模型补上报告。
        /// </summary>
        public bool NeedsSummary => _hadToolCall && _report.Length == 0 && _allText.Length > 0;

        /// <summary>
        /// 生成交给主 agent 的报告
        /// </summary>
        /// <param name="timedOut">本次运行是否因超时被掐断</param>
        /// <returns>报告文本</returns>
        public string Build(bool timedOut) => Build(timedOut, Timeout, string.Empty, false, false);

        /// <summary>
        /// 生成交给主 agent 的报告
        /// </summary>
        /// <param name="timedOut">是否因超时被掐断</param>
        /// <param name="limit">本次适用的墙钟上限(交互与无人值守分档)</param>
        /// <param name="subSessionId">子会话标识;非空时缀在末尾供续跑点名</param>
        /// <param name="stoppedByUser">是否被用户中止</param>
        /// <param name="userInterjected">过程中用户是否插过话</param>
        /// <returns>报告文本</returns>
        public string Build(bool timedOut, TimeSpan limit, string subSessionId, bool stoppedByUser,
            bool userInterjected)
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

            if (result.Length == 0) result.Append("(sub-agent returned no report)");

            if (timedOut)
            {
                result.AppendLine();
                result.Append($"(sub-agent stopped: exceeded its {limit.TotalMinutes:0} minute time limit)");
            }

            // 被用户中止与超时是两回事:前者意味着还能接着跑(历史已由 ToolCallCancellation 封口),
            // 不说清楚主 agent 会把半截当成结论
            if (stoppedByUser)
            {
                result.AppendLine();
                result.Append("(sub-agent stopped: the user interrupted it. "
                              + "Its sub-session is intact and can be continued.)");
            }

            // 用户插话改变了这次委派的性质,主 agent 该知道自己拿到的不全是它自己要的东西
            if (userInterjected)
            {
                result.AppendLine();
                result.Append("(note: the user sent additional instructions to the sub-agent "
                              + "during this delegation, so this report may reflect directions you did not give.)");
            }

            if (subSessionId.Length > 0)
            {
                result.AppendLine();
                result.Append($"[sub-session: {subSessionId}]");
            }

            return result.ToString();
        }
    }
}

/// <summary>
/// 名单里的一个子智能体：给模型看的名字、一句用途，以及它是哪个角色。
/// </summary>
/// <param name="Name">子智能体名(模型按这个名字点名)</param>
/// <param name="Description">用途;空描述的子智能体模型无从判断该不该派给它</param>
/// <param name="CharacterId">对应角色标识——子会话据此在重开时装配出同一个子智能体</param>
public sealed record SubAgentChoice(string Name, string Description, string CharacterId);
