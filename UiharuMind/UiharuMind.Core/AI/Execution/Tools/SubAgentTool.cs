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
using UiharuMind.Core.AI.Execution.Prompts;
using UiharuMind.Core.Configs;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 子代理工具:主代理把大范围的探查/调研任务委派出去,结论以报告回到工具返回值,
/// 过程不吃主上下文。
///
/// <b>一次委派 = 一个真会话</b>(子会话):建 <see cref="ChatSession"/>、进索引、正常落盘,
/// 然后跑<b>它自己的</b> <see cref="TurnDriver"/>——与定时任务的无头轮次同一套编排。
/// 于是落盘、续跑、再对话三件事全部沿用会话的既有能力,不另立实体(见 ADR 0021)。
///
/// <b>一律后台执行</b>(见 ADR 0025):工具当场返回一句「已派出、尚无结果」——那是一条合法且
/// 已配对的工具结果,派活者那一轮照常自洽地封存。子代理跑完之后,结论走
/// <see cref="Chat.SubAgentReportHandoff"/> 落进父会话历史,再由一轮<b>没有用户消息</b>的
/// 唤醒轮交给模型。编排归 <see cref="BackgroundSubAgentDispatcher"/>,本类只管跑那一轮。
///
/// <b>审批通道</b>:请求登记到 <see cref="ToolCall.SubSessionApprovalRegistry"/>,由子会话窗口
/// 画出卡片。<b>不再先问派活者那一轮</b>——它在子代理开跑前就结束了,恒定接不住。
/// 于是那条「有审批在等你」的提示是承重的:<see cref="NestedApprovalTimeout"/> 到期按拒绝收口。
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
    public const string ToolGeneralName = "RunAgent";

    /// <summary>
    /// 只读子代理的工具名。<b>限制写进名字里</b>:这正是要让模型看见的那一点——
    /// 它改不了任何东西,派错了只会白跑一趟。
    /// </summary>
    public const string ToolExplorerName = "RunReadOnlyAgent";

    /// <summary>追问/续跑工具名。两档子代理共用一个——它认的是那次运行的编号,与当初派的是哪一档无关</summary>
    public const string ToolContinueName = "ContinueAgent";

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

    /// <summary>嵌套审批连续被拒造成的追加轮次上限,学无头路径的 <c>MaxApprovalRounds</c>——
    /// 模型若执意重试同一动作,无限拒绝等于无限烧轮次。用户批准一次即清零(见
    /// <see cref="ToolCall.NestedApprovalResolver"/>),掐的只是空转</summary>
    private const int MaxDeniedApprovalRounds = 4;

    /// <summary>
    /// 嵌套审批等用户点选的上限。只发生在有人看着时：无人值守上游当场拒绝，轮不到等待。
    /// 到期/取消按拒绝收口、轮次继续，报告里点名哪些活没干成。
    ///
    /// ⚠️ 后台化之后<b>没人陪着等</b>了（派活者那一轮早已结束）。这个值刻意维持 5 分钟，
    /// 代价是那条通知变成承重的——弹不出来，这次委派基本等于白跑。见 ADR 0025。
    /// </summary>
    public static readonly TimeSpan NestedApprovalTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 完全自动档下嵌套审批自动放行的理由(送给模型,也进日志)。措辞三件事:谁决定的、
    /// 凭什么、用户去哪看——放行即落子,不说清等于偷偷改盘。
    /// </summary>
    private const string FullAutoApprovalReason =
        "Auto-approved without asking: the delegating session runs in FullAuto mode. "
        + "This decision is disclosed to the user in the run report.";

    /// <summary>
    /// 嵌套审批是否该自动放行：有人守着 + 完全自动档。无人值守不放（没人看着时静默改盘比停下更糟），
    /// 低档位照旧问人（越界写入的例外登记见 ADR 0032）。
    /// </summary>
    internal static bool ShouldAutoApproveNestedApprovals(bool attended, int permissionModeIndex) =>
        attended && permissionModeIndex == (int)EAgentPermissionMode.FullAuto;

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

        /// <summary>派活者会话的产出目录名(相对 AgentOutputLayout.RootPath);子代理的产出落这里</summary>
        public string? ParentOutputFolderName { get; init; }

        /// <summary>本工具装配成哪一档子代理</summary>
        public required SubAgentProfile Profile { get; init; }

        /// <summary>可点名的子智能体名单;为空则只有通用匿名子代理</summary>
        public required IReadOnlyList<SubAgentChoice> Roster { get; init; }

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
            sb.AppendLine(SubAgentToolPrompts.RosterHeading);
            foreach (SubAgentChoice choice in context.Roster)
            {
                sb.AppendLine($"- {choice.Name}: {choice.Description}");
            }

            description = sb.ToString().TrimEnd();
        }

        return AIFunctionFactory.Create(
            ([Description(SubAgentToolPrompts.TaskParam)]
                string task,
                [Description(SubAgentToolPrompts.AgentParam)]
                string? agent = null,
                [Description(SubAgentToolPrompts.RoleParam)]
                string? role = null,
                [Description(SubAgentToolPrompts.ModelParam)]
                string? model = null) => Launch(context, task, agent, role, model),
            context.Profile.ToolName,
            description);
    }

    /// <summary>
    /// 派活方插话的前缀。子代理提示词明确区分「用户在窗口说话」与「派活方追问」，
    /// 插话以 user 身份进流时必须自报家门，否则子代理会把它当成用户的话。
    /// 落盘带前缀：它本来就是派活方说的，原样留痕才是实话。
    /// </summary>
    private const string ParentInterjectionPrefix = "【派活方】";

    /// <summary>
    /// 创建续跑/追问工具。两档共用一个:它认的是子会话标识,与当初派的是哪一档无关
    /// （那一档已经落在子会话上了，重建时照它装配）。
    /// </summary>
    /// <param name="context">派活上下文(取其中的过程上报口与审批通道)</param>
    /// <returns>工具实例</returns>
    public static AITool CreateContinueTool(LaunchContext context)
    {
        return AIFunctionFactory.Create(
            ([Description(SubAgentToolPrompts.ContinueSubSessionParam)]
                string subSession,
                [Description(SubAgentToolPrompts.ContinueMessageParam)]
                string message) => ContinueAsync(context, subSession, message),
            ToolContinueName,
            SubAgentToolPrompts.ContinueDescription);
    }

    private static string Launch(LaunchContext context, string task, string? agent, string? role = null,
        string? model = null)
    {
        if (string.IsNullOrWhiteSpace(task)) return "Error: task must not be empty.";

        SubAgentChoice? choice = agent == null
            ? null
            : context.Roster.FirstOrDefault(x => string.Equals(x.Name, agent, StringComparison.OrdinalIgnoreCase));
        if (agent != null && choice == null)
        {
            return $"Error: no agent named '{agent}'. "
                   + (context.Roster.Count == 0
                       ? "No named agents are mounted; omit `agent` for the default agent."
                       : $"Available: {string.Join(", ", context.Roster.Select(x => x.Name))}.");
        }

        string? normalizedRole = NormalizeRole(role);
        ModelChoice modelChoice = ResolveSubAgentModelName(context.Profile, model);
        ChatSession session = new()
        {
            // 匿名子代理用内置的身份角色,不沿用派活者的——否则子会话窗口会顶着派活者的
            // 名字和头像,看起来像在跟主代理说话。两档各有一张:探索档恒定只读、另配模型,
            // 顶同一个名字用户分不清这次委派能不能改东西。
            // 能力仍然直接取派活者那一份(不经交集,见 SubAgentAssembly.BuildFromPlan)
            CharacterId = choice?.CharacterId ?? AnonymousCharacterOf(context.Profile.Type).ToString(),
            Title = BuildTitle(task, normalizedRole),
            Description = task,
            WorkspacePath = context.WorkspacePath,
            PermissionModeIndex = context.PermissionModeIndex,
            PreAuthorizedShellPatterns = context.PreAuthorizedShellPatterns,
            ParentSessionId = context.ParentSessionId,
            ParentOutputFolderName = context.ParentOutputFolderName,
            SubAgentType = context.Profile.Type,
            SubAgentName = choice?.Name ?? string.Empty,
            SubAgentRole = normalizedRole ?? string.Empty,
            SessionModelName = modelChoice.Name,
        };
        SessionManager.Instance.Add(session);
        NoteStarted(context, session.SessionId);

        return DispatchToBackground(context, session, task, modelChoice.Notice);
    }

    private static async Task<string> ContinueAsync(LaunchContext context, string subSessionId, string message)
    {
        if (string.IsNullOrWhiteSpace(subSessionId)) return "Error: subSession must not be empty.";
        if (string.IsNullOrWhiteSpace(message)) return "Error: message must not be empty.";

        ChatSession? session = SessionManager.Instance.Load(subSessionId);
        if (session == null) return $"Error: no such run '{subSessionId}'.";
        // 只允许续自己派出去的那些:子会话是按派活者归属的,跨会话续跑等于绕过能力交集
        if (!string.Equals(session.ParentSessionId, context.ParentSessionId, StringComparison.Ordinal))
        {
            return $"Error: run '{subSessionId}' was not started by this session.";
        }

        NoteStarted(context, session.SessionId);

        // 跑着就实时插话:新起一轮要等执行者闸门放行(它整轮持有),纠偏得等整轮跑完。
        // 插话走注入队列,在下一次模型请求前消费,效果并入当前轮的报告,不另交报告。
        if (SessionManager.Instance.Running.IsBusy(session.SessionId))
        {
            ChatMessage injection = session.CreateMessage(ChatRole.User, ParentInterjectionPrefix + message);
            ChatMessageAnnotations.MarkParentInterjection(injection);
            bool injected = false;
            try
            {
                injected = await session.Runner.TryInjectAsync(new[] { injection }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // 入队失败就当没成功:回落到排队续跑,消息不丢
                Log.Warning($"Parent interjection failed, falling back to queued turn: "
                            + $"session={session.SessionId}: {e.Message}");
            }

            if (injected) return BuildInjectedReceipt(session.SessionId);
        }

        return DispatchToBackground(context, session, message);
    }

    /// <summary>
    /// 实时插话的回执(纯函数,可单测)。标记行必须是最后一行——回放历史时卡片靠
    /// <c>ToolCallItem.ParseSubSessionId</c> 的正则认出「这是一次委派」并挂出「查看过程」入口。
    /// </summary>
    /// <param name="subSessionId">子会话标识</param>
    /// <returns>当场返回给模型的工具结果</returns>
    public static string BuildInjectedReceipt(string subSessionId) =>
        "Injected into the running session — it reads this on its next model request "
        + "within the current run. No new turn was started and no separate report arrives "
        + "for this message; its effect is folded into the current run's upcoming report. "
        + "Do not poll for it.\n"
        + $"[sub-session: {subSessionId}]";

    /// <summary>
    /// 把这次委派转入后台并当场给出工具结果。
    ///
    /// <b>「有没有人看着」在此刻定死</b>：<c>IsAttendedSource</c> 是派活者<b>那一轮</b>的
    /// 现取委托，而那一轮马上就要结束了，跑到一半再问它答案已经不作数。
    ///
    /// <b>派活者的审批回应口不再尝试</b>：那一轮已经结束，恒定接不住（见 ADR 0025）。
    /// 嵌套审批直接登记到子会话，等用户去那边点选。
    /// </summary>
    private static string DispatchToBackground(LaunchContext context, ChatSession session, string message,
        string notice = "")
    {
        bool attended = context.IsAttended;
        return BackgroundSubAgentDispatcher.Dispatch(session,
            token => RunTurnAsync(session, message, attended, token), notice);
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
    private static async Task<string> RunTurnAsync(ChatSession session, string message, bool attended,
        CancellationToken cancellationToken)
    {
        TimeSpan limit = attended ? Timeout : UnattendedTimeout;
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(limit);

        SubAgentTurnSink turnSink = new();
        bool timedOut = false;
        bool stopped = false; //被用户/外部停掉。派活者的令牌回答不了,只有 TurnDriver 知道
        int unansweredClosed = 0; //审批未决封掉的孤儿调用数(正常结束路径用,见下)
        // 委派期间主代理那一头是同步阻塞的,日志里不留痕就只剩一段无法解释的沉默——
        // 用户看着像卡死(实际是子代理在跑)。起止各一条,带上子会话标识便于对到那个窗口
        long startedAt = Environment.TickCount64;
        Log.Debug($"Sub-agent turn started: session={session.SessionId} title=\"{session.Title}\"");
        // 嵌套审批的回应口:先问派活者那一轮,接不住时登记到子会话等用户去那边点选(见 NestedApprovalResolver)
        // 派活者那一轮已经结束(工具当场返回了),它的回应口恒定接不住,不再尝试——直接登记到子会话。
        // 于是那条「有审批在等你」的提示是**承重**的:5 分钟没人点就按拒绝收口(见 ADR 0025)
        //
        // 完全自动档是个例外:卡只能弹在子会话窗口,盯着主会话的用户看不见,问了也白问——
        // 5 分钟一到按拒绝收口、整轮白跑(实机:派到工作区外的活全灭)。有人看着时直接放行,
        // 无人值守仍拒绝(没人看着时静默改盘比停下来更糟)。放行要认账:理由送给模型、
        // 警告记进日志、路径点名进报告,三处缺一不可。
        bool fullAuto = ShouldAutoApproveNestedApprovals(attended, session.PermissionModeIndex);
        List<string> autoApprovedCalls = new();
        ApprovalResolver? resolver = NestedApprovalResolver.Create(attended,
            session.SessionId, SubSessionApprovalRegistry.Instance, NestedApprovalTimeout,
            MaxDeniedApprovalRounds, timeoutSource.Token,
            () => BackgroundSubAgentDispatcher.Notifier?.Invoke(
                ESubAgentNotice.ApprovalWaiting, session.SessionId),
            autoApprove: fullAuto ? _ => FullAutoApprovalReason : null,
            onAutoApproved: call =>
            {
                lock (autoApprovedCalls) autoApprovedCalls.Add(NestedApprovalResolver.Describe(call));
            });

        try
        {
            // 同一轮里多次读 session.Runner 会拿到不同实例：前一轮 finally 释放后属性置 null、
            // 新一轮惰性重建——Attach 与 Run 各读一次就可能在中间换实例，拿到个没挂接的新 runner
            // （实机「尚未挂接会话」）。整轮只捕获一次，Attach/Run/总结共用同一实例。
            ICharacterRunner runner = session.Runner;
            await runner.AttachAsync(session, timeoutSource.Token).ConfigureAwait(false);

            using TurnDriver driver = new(turnSink, new TurnUsageLedger());
            // 令牌串的是本次委派自己的超时源。停止走 TurnDriver.CancelSession(子会话标识),
            // 它取消的是 driver 内部那个链接源——所以「有没有被停」只能问 driver,
            // 问我们手里这个令牌永远得到"没有"(见 TurnDriver.WasCancelled)
            await driver.RunAsync(session, runner, new ChatMessage(ChatRole.User, message),
                resolver, timeoutSource.Token).ConfigureAwait(false);
            stopped = driver.WasCancelled && !timeoutSource.IsCancellationRequested;

            // 兜底网:正常结束时理论上不应再有孤儿(嵌套审批已按拒绝收口),
            // 留着防其他漏网路径。先封再总结:总结那一轮要看到"没跑成",
            // 否则模型会把没干的活写进报告
            unansweredClosed = ToolCallCancellation.CloseUnansweredAtTail(session,
                ToolCallCancellation.ApprovalUnansweredResultText);

            // 代码兜底:模型以工具调用结束、之后没产出文本(没写收尾总结)。
            // 提示层硬约束挡住大多数,这里兜漏网的——追加一轮"请总结"让模型补上报告
            // 被停掉时**不追加这一轮**:那是一条自动发给子代理的「请总结」,
            // 用户刚按下停止,紧接着又让它跑一轮,既违背那一下的意思,也会把报告变成一份
            // 看起来完整的总结,主代理更难看出这次委派没干完。
            // 守卫不能只看 timeoutSource:CancelSession 取消的是 driver 内部那个令牌,
            // 这一个一动不动(见 TurnDriver.WasCancelled)
            if (turnSink.Report.NeedsSummary && !stopped && !timeoutSource.Token.IsCancellationRequested)
            {
                using TurnDriver summaryDriver = new(turnSink, new TurnUsageLedger());
                //总结那一轮也可能被停,别把半截总结当成完整报告
                await summaryDriver.RunAsync(session, runner,
                        new ChatMessage(ChatRole.User, SubAgentPrompts.SummaryPrompt),
                        resolver, timeoutSource.Token)
                    .ConfigureAwait(false);
                stopped |= summaryDriver.WasCancelled && !timeoutSource.IsCancellationRequested;

                // 总结那一轮同样可能撞上审批未决(同一条断路),再封一次——无孤儿时是空操作
                unansweredClosed += ToolCallCancellation.CloseUnansweredAtTail(session,
                    ToolCallCancellation.ApprovalUnansweredResultText);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 撞在 TurnDriver 之外的取消(挂接阶段等)。分清是哪一种:只有本次委派自己的
            // 墙钟超时才算超时,否则是外部停止——两者在报告里的措辞完全不同,
            // 混成「超时」会让主代理以为是意外而不是用户的决定
            timedOut = timeoutSource.IsCancellationRequested;
            stopped = !timedOut;
        }
        finally
        {
            // 执行者(含 shell executor)随这次委派释放,不挂到应用退出;
            // 之后用户打开该子会话会重新惰性创建——按同一份持久化身份重建。
            // 但此刻若还有别的轮次在跑(用户直接开的子会话窗口前台轮刚赶上),放了它
            // 前台那一轮就拿到已释放实例——没有别的轮次才释放。
            if (!SessionManager.Instance.Running.IsBusy(session.SessionId))
            {
                await session.DisposeRunnerAsync().ConfigureAwait(false);
            }
        }

        string report = turnSink.Report.Build(timedOut, limit, session.SessionId,
            stopped || cancellationToken.IsCancellationRequested, turnSink.SawUserInterjection);
        // 派活方中途插过话:报告里可能答了原任务之外的东西,主代理该知道这份结论不全是它要的。
        // 与用户插话分开交代——两者来源不同,混成一句会让主代理误判是谁改了方向。
        if (turnSink.SawParentInterjection)
        {
            report += "\n(note: the delegating agent sent additional instructions "
                      + "during this run, so this report may reflect directions beyond the original task.)";
        }
        if (unansweredClosed > 0)
        {
            // 有调用因审批未决根本没跑成,必须点名——否则主代理会把没干的活当成干完了
            report += $"\n(note: {unansweredClosed} tool call(s) in the run never ran - "
                      + "their approvals were not answered before the turn ended. "
                      + "Do not assume that work was done.)";
        }
        if (autoApprovedCalls.Count > 0)
        {
            // 完全自动档下放行的那些:放行即落子,主代理(和用户)必须知道动了界外的哪几处。
            // 只点名前几个——一轮写几十个文件时全列出来等于把报告撑成清单。
            string[] shown;
            lock (autoApprovedCalls) shown = autoApprovedCalls.Distinct().Take(5).ToArray();
            string more = autoApprovedCalls.Count > shown.Length ? ", …" : string.Empty;
            report += $"\n(note: {autoApprovedCalls.Count} action(s) that normally need approval "
                      + "were auto-approved because the delegating session runs in FullAuto mode: "
                      + string.Join("; ", shown) + more + ".)";
        }
        string outcome = timedOut ? "timed out" : stopped || cancellationToken.IsCancellationRequested ? "stopped" : "done";
        Log.Debug($"Sub-agent turn {outcome}: session={session.SessionId} "
                  + $"elapsed={(Environment.TickCount64 - startedAt) / 1000}s report={report.Length} chars");
        return report;
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
    /// <param name="requested">派活方这一趟点名的模型；空则用设置页配的那档默认</param>
    /// <returns>钉在子会话上的模型名，以及要随回执交代的那句话</returns>
    private static ModelChoice ResolveSubAgentModelName(SubAgentProfile profile, string? requested = null)
    {
        string? asked = NormalizeModel(requested);
        if (asked == null) return new ModelChoice(PinnableModelName(profile.ResolveModelName(AgentSettingConfig.Current)),
            string.Empty);

        // 点名的压过设置页那档默认:那两项的语义是"默认用哪个",而这一趟点名是"这一趟用哪个"
        if (PinnableModelName(asked) is { } pinned) return new ModelChoice(pinned, string.Empty);

        // 回退了就必须说。这是派活方唯一能知道"我指定的模型没生效"的渠道——
        // 模型名解析从不抛异常(查不到就静默回落全局),不说的话它会一直以为跑的是它点的那个
        string reason = LlmManager.Instance.CacheModelDictionary.ContainsKey(asked)
            ? $"model '{asked}' is not running"
            : $"there is no model named '{asked}'";
        return new ModelChoice(PinnableModelName(profile.ResolveModelName(AgentSettingConfig.Current)),
            $"Note: {reason}, so this run uses the default model instead.");
    }

    /// <summary>
    /// 一个模型名能不能钉上去：查得到、跑得起来、客户端已就绪才算。
    ///
    /// <b>绝不把一个永远不会就绪的模型钉上去</b>，那会让惰性客户端死等，
    /// 表现是 "Model is not running"。本地模型无法热切换（一次只能跑一个，端口写死，
    /// 且 <c>EnsureModelStarted</c> 对本地模型直接 return 不自动加载），于是天然落进这一支。
    /// </summary>
    /// <param name="name">模型名；空白直接算不可钉</param>
    /// <returns>可钉时返回规范化后的模型名，否则 null</returns>
    private static string? PinnableModelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!LlmManager.Instance.CacheModelDictionary.TryGetValue(name, out ModelRunningData? found)) return null;

        ModelRunningData? candidate = found;
        if (!LlmManager.Instance.TryCheckModelRunning(false, ref candidate)) return null;
        return candidate is { ChatClient: not null } ? candidate.ModelName : null;
    }

    /// <summary>模型名按 <c>ModelName</c> 全局唯一 key 认，只去掉空白与换行；空白视为未指定</summary>
    private static string? NormalizeModel(string? model)
    {
        string? trimmed = model?.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>派活时的模型决定</summary>
    /// <param name="Name">钉在子会话上的模型名；跟随派活者/全局时为 null</param>
    /// <param name="Notice">要随回执交代给派活方的那句话；无需交代时为空串</param>
    private readonly record struct ModelChoice(string? Name, string Notice);

    /// <summary>匿名子代理用哪张身份卡</summary>
    private static DefaultCharacter AnonymousCharacterOf(ESubAgentType type) =>
        type == ESubAgentType.Explorer ? DefaultCharacter.ExploreSubAgent : DefaultCharacter.GeneralSubAgent;

    /// <summary>子会话标题:有 role 用 role,否则取任务首行,都截 40 字。标题纯显示、落盘、改不了名</summary>
    private static string BuildTitle(string task, string? role = null)
    {
        string source = string.IsNullOrWhiteSpace(role) ? task.Trim().Split('\n', 2)[0].Trim() : role;
        const int max = 40;
        return source.Length <= max ? source : source[..max] + "…";
    }

    /// <summary>role 清洗:trim、剥掉换行与反引号(防注入工具名)、截 40 字。返回 null 表示未设定</summary>
    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        StringBuilder sb = new(role.Length);
        foreach (char c in role.Trim())
        {
            if (c is '\r' or '\n' or '`') continue;
            sb.Append(c);
        }
        string result = sb.ToString().Trim();
        return result.Length > 40 ? result[..40] : result;
    }

    /// <summary>
    /// 子代理这一轮的渲染落点。它只攒交给主代理的报告，<b>自己不认识界面</b>——
    /// 打开着的子会话窗口看到的实时内容，来自 <c>TurnDriver</c> 在
    /// <c>ChatSession.LiveTurn</c> 上开的那个分岔口（同一条流的另一个订阅者），
    /// 与本类无关。
    /// </summary>
    private sealed class SubAgentTurnSink : ITurnSink
    {
        private readonly StringBuilder _streaming = new(); //正在流的那一段正文,取消时由 TurnDriver 取走落库

        /// <summary>报告累加器</summary>
        public ReportAccumulator Report { get; } = new();

        /// <summary>本轮是否出现过用户插话(报告里要交代,否则主代理会把它当成自己的委派结果)</summary>
        public bool SawUserInterjection { get; private set; }

        /// <summary>本轮是否出现过派活方插话(同上,但来源不同,报告里分开交代)</summary>
        public bool SawParentInterjection { get; private set; }

        public void Apply(AIContent content)
        {
            Report.Add(content);
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

    /// <summary>
    /// 从子代理的内容流里提取报告。
    ///
    /// 报告 = <b>最后一次工具调用之后</b>的正文,而非全程正文拼接。
    /// 框架默认工作循环明确要求 agent "explain what you learned and what you are going to do next
    /// between tool calls",于是全程正文里绝大部分是"我接下来去看 X"这类旁白。
    /// 把它们拼起来交给主代理有两个坏处:等于把子代理的思考过程塞回主上下文
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
                // 只取正文:思考段属过程,永不进主代理的上下文
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
        /// 生成交给主代理的报告
        /// </summary>
        /// <param name="timedOut">本次运行是否因超时被掐断</param>
        /// <returns>报告文本</returns>
        public string Build(bool timedOut) => Build(timedOut, Timeout, string.Empty, false, false);

        /// <summary>
        /// 生成交给主代理的报告
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
                // 否则主代理会把中间猜测当成子代理的判断
                result.AppendLine("(No final report - the agent stopped before summarizing. "
                                  + "Below is its running commentary, not a conclusion.)");
                result.Append(_allText.ToString().Trim());
            }

            if (result.Length == 0) result.Append("(agent returned no report)");

            if (timedOut)
            {
                result.AppendLine();
                result.Append($"(agent stopped: exceeded its {limit.TotalMinutes:0} minute time limit)");
            }

            // 被用户中止与超时是两回事:后者是意外,前者是**用户的决定**。
            // 只说「还能接着跑」不够——实机见过主代理读完就自己重新派了一个,
            // 等于把用户刚按下的停止撤销掉
            if (stoppedByUser)
            {
                result.AppendLine();
                result.Append("(agent stopped: the USER deliberately interrupted it. "
                              + "This was their decision, not a failure. Do NOT re-dispatch this task, "
                              + "and do NOT work around it by doing the work yourself. "
                              + "Report that it was stopped and ask what they want to do next. "
                              + "The run is intact if they ask you to resume it.)");
            }

            // 用户插话改变了这次委派的性质,主代理该知道自己拿到的不全是它自己要的东西
            if (userInterjected)
            {
                result.AppendLine();
                result.Append("(note: the user sent additional instructions to the agent "
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
