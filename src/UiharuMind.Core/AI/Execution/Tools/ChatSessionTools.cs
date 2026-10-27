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
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// chatsession 扩展背后的查询工具：list 列出本会话派出过的子代理及其状态，
/// groups 列出群聊的成员与化身状态，models / characters 给派活前选模型与点人的决策参考。
/// 全部只读——不吃审批、不改任何会话。
///
/// 状态的权威来源是 <see cref="SessionRunRegistry"/>（谁在跑、谁卡在审批上）：
/// 它与执行者、会话本体解耦，界面指示器和这里读到的是同一份。
/// 上下文占用直接读本体持久化的 <c>LastInputTokens</c>，窗口上限现读
/// 有效模型的 <c>ContextLength</c>——格式复用 <see cref="TurnUsageLedger.Format"/>，不另立账本。
/// 模型列表用 <see cref="LlmManager.GetModelList"/>（已加载、参数解析好的那份），角色列表用角色库
/// </summary>
public static class ChatSessionTools
{
    /// <summary>list：本会话派出的子代理</summary>
    public const string ListToolName = "ChatSessionList";

    /// <summary>groups：本会话建过的群聊（在跑时顺带记一份流水快照）</summary>
    public const string GroupsToolName = "ChatSessionGroups";

    /// <summary>create-group：建一个群</summary>
    public const string CreateGroupToolName = "ChatSessionCreateGroup";

    /// <summary>models：已加载的模型列表（参数已解析）</summary>
    public const string ListModelsToolName = "ChatSessionModels";

    /// <summary>characters：角色列表与其参数</summary>
    public const string ListCharactersToolName = "ChatSessionCharacters";

    /// <summary>stop：强行停止一个正在跑的子代理</summary>
    public const string StopToolName = "ChatSessionStop";

    /// <summary>list-tasks：本会话名下在跑的后台任务（含 SendMessage 自动起的等待任务）</summary>
    public const string ListTasksToolName = "ChatSessionListTasks";

    /// <summary>dump：把群或子代理的会话流水导成一份 markdown 并给路径（空闲的群也照导）</summary>
    public const string DumpToolName = "ChatSessionDump";

    // 群多了全列占上下文，最近几个够看
    private const int MaxListedGroups = 5;

    // 建群至少几位成员：一位就是单聊（与建群弹窗同口径；开发脚本那条路不受此限）。
    // 下限校验在 GroupCreation 里按解析后的非空成员数算，空串成员不会绕过
    private const int MinGroupMembers = 2;

    // 结果里提到别的命令与工具时的写法：名字改了这里跟着改，不靠各处手写字符串
    private static readonly string CreateGroupMention =
        BuiltinExtensions.Mention(BuiltinExtensions.ChatSession, BuiltinExtensions.ChatSessionCreateGroup);

    private static readonly string GroupsMention =
        BuiltinExtensions.Mention(BuiltinExtensions.ChatSession, BuiltinExtensions.ChatSessionGroups);

    private static readonly string ListCharactersMention =
        BuiltinExtensions.Mention(BuiltinExtensions.ChatSession, BuiltinExtensions.ChatSessionCharacters);

    private static readonly string SendMessageMention = $"`{SubAgentTool.MessageToolName}`";

    /// <summary>
    /// 造八把工具
    /// </summary>
    /// <param name="sessionId">当前会话标识；空串时报「不可用」（扩展装配时拦截）</param>
    /// <param name="workspacePath">当前会话的工作区；建群时默认绑它</param>
    /// <returns>八把工具，list 在前</returns>
    public static IReadOnlyList<AITool> Create(string sessionId, string? workspacePath = null) =>
    [
        ToolJson.CreateFunction(() => ListSubSessions(sessionId), ListToolName,
            "列出本会话派出的子代理及其状态。"),
        ToolJson.CreateFunction(() => ListGroups(sessionId), GroupsToolName,
            "列出本会话建的群聊及其状态；正在跑的群会立刻记一份流水快照，并给出快照路径。"),
        ToolJson.CreateFunction(
            ([Description("群名。")] string name,
                    [Description("成员的角色名或标识，顺序即发言顺序，至少两位。")]
                    string[] members,
                    [Description("调度模式：serial（成员一人一轮、一圈即停）/ parallel（被唤醒的成员并发开跑）。默认 serial。")]
                    string? mode = null,
                    [Description("并行时的停止条件：conservative（发言引起的唤醒只传一跳）/ aggressive（聊到自然停）。默认 conservative；串行忽略。")]
                    string? stop = null,
                    [Description("主持人：必须是成员之一（角色名或标识）。默认无主持人。")]
                    string? host = null,
                    [Description("成员各自的模型名，顺序与 members 一一对应；省略或空串 = 跟随全局。")]
                    string[]? models = null) =>
                CreateGroup(sessionId, workspacePath, name, members, mode, stop, host, models),
            CreateGroupToolName,
            "建一个智能体群并把成员拉进去（群绑在当前会话的工作区）。建好后用 `SendMessage`（`to` 填群名或短号）往群里说话。"),
        ToolJson.CreateFunction(() => ListModels(), ListModelsToolName,
            "列出已加载的远程模型及其参数。"),
        ToolJson.CreateFunction(() => ListCharacters(), ListCharactersToolName,
            "列出角色库。"),
        ToolJson.CreateFunction(([Description("要停止的子代理短号或完整标识")] string id) => StopSubSession(sessionId, id),
            StopToolName,
            "强行停止本会话派出的一个正在跑的子代理。"),
        ToolJson.CreateFunction(() => ListTasks(sessionId), ListTasksToolName,
            "列出本会话名下在跑的后台任务（含 SendMessage 发群/发单聊后自动起的等待任务）。"),
        ToolJson.CreateFunction(([Description("群名或短号，或本会话派出的子代理短号/完整标识")] string id,
                    [Description("是否导出模型的思考段（默认 false：只留正文；思考段可能很长）")]
                    bool reasoning = false,
                    [Description("是否导出工具调用原样参数与结果（默认 false：只留正文；排查工具参数时开）")]
                    bool tools = false) =>
                Dump(sessionId, id, reasoning, tools), DumpToolName,
            "把群或子代理的会话流水导成一份 markdown 并给出文件路径：id 填群名/短号导群流水（空闲也照导），填子代理短号/完整标识导它的会话历史（默认只留正文，reasoning/tools 可加思考与工具调用）。"),
    ];

    /// <summary>
    /// 本会话派出的子代理。按「在跑/等审批的在前、其余按最后更新」排——查状态的时刻最想知道谁还活着。
    /// 脑（ADR 0082：同步整理助手、可被 brainId 追问）不列条目——与右栏「只报数量」同口径；
    /// 探索档有名字有主题，像通用委派一样列。
    /// </summary>
    /// <param name="sessionId">派活者会话标识</param>
    /// <param name="manager">会话管理器；缺省用全局实例（测试传自建的隔离实例）</param>
    internal static string ListSubSessions(string sessionId, SessionManager? manager = null)
    {
        manager ??= SessionManager.Instance;
        List<ChatSessionMeta> subs = manager.GetSubSessions(sessionId)
            .Where(x => !x.IsBrain)
            .ToList();
        if (subs.Count == 0)
        {
            return "本会话还没有派出过子代理。";
        }

        List<ChatSessionMeta> ordered = subs
            .OrderByDescending(x => StateOf(manager, x.SessionId) != ESessionRunState.Idle)
            .ThenByDescending(x => x.LastRunStartedAt ?? x.UpdatedAt)
            .ToList();

        StringBuilder sb = new();
        sb.Append($"本会话派出过 {subs.Count} 位子代理：\n");
        foreach (ChatSessionMeta meta in ordered)
        {
            // 只读路径：热会话取已驻留本体（零加载），冷会话读索引里的占用快照——绝不 Load 全量
            // （Load 会整读历史、跑孤儿修复落盘、钉驻留，不该跟着一次免审批查询发生）
            sb.AppendLine(DescribeSub(meta, manager.GetLoaded(meta.SessionId), manager));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 本会话名下在跑的后台任务。SendMessage 发群/发单聊后自动起的等待任务也在这里——
    /// 「还差哪一封回信没来」这一问的答案就在这份清单里（回信到达即任务结束，只列在跑的）
    /// </summary>
    /// <param name="sessionId">查询方会话标识</param>
    /// <returns>任务清单文本</returns>
    internal static string ListTasks(string sessionId)
    {
        IReadOnlyList<IBackgroundTask> tasks = BackgroundTaskRegistry.RunningOf(sessionId);
        if (tasks.Count == 0)
        {
            return "本会话名下没有在跑的后台任务。";
        }

        StringBuilder sb = new();
        sb.AppendLine($"本会话名下有 {tasks.Count} 个后台任务在跑：");
        foreach (IBackgroundTask task in tasks)
        {
            string age = TimeUtils.FormatDuration(DateTimeOffset.Now - task.StartedAt);
            sb.AppendLine($"- `{task.Id}`：{task.Description} · 已跑 {age}"
                          + (task.LogPath.Length > 0 ? $" · 日志 {task.LogPath}" : string.Empty));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 强行停止本会话派出的一个正在跑的子代理。停止走 <see cref="TurnDriver.CancelSession"/>——
    /// 与右栏「子代理」面板、子会话窗口的停止按钮同一条路（见 BackgroundSubAgentDispatcher 的注释），
    /// 收尾由 <c>SettleInterruptedTurn</c> 补取消结果，停完还能续
    /// </summary>
    private static string StopSubSession(string parentSessionId, string id)
    {
        string target = id.Trim();
        if (target.Length == 0) return "Error: 要停止的子代理标识不能为空。";

        IReadOnlyList<ChatSessionMeta> runs = SubSessionIdAlias.Match(parentSessionId, target);
        return runs.Count switch
        {
            0 => $"Error: 没有 '{target}' 这个子代理。先用 {ListToolName} 看有哪些。",
            1 => StopOne(runs[0]),
            _ => $"Error: '{target}' 匹配了多个子代理，把完整标识作为 id 传入。",
        };
    }

    private static string StopOne(ChatSessionMeta meta)
    {
        string name = meta.Title.Length > 0 ? $"《{meta.Title}》" : "该子代理";
        if (!SessionManager.Instance.Running.IsBusy(meta.SessionId))
        {
            return $"{name}没有在跑，无需停止。";
        }

        return TurnDriver.CancelSession(meta.SessionId)
            ? $"已停止{name}（[sub-session: {SubSessionIdAlias.Short(meta.SessionId)}]），收尾后它会回一封说明中断的结论。"
            : $"停止没生效（它可能刚好收尾了），用 {ListToolName} 再看一眼。";
    }

    /// <summary>
    /// 群聊状态（最近的几个）。列表不是「我建过的」——群不记发起会话，列出谁都能看；
    /// 排的是最后更新，正在跑的群自然浮在上面。
    ///
    /// <b>正在跑的群当场把流水写进 <c>群流水.md</c> 并给出路径</b>：跑着的时候群历史还在长，
    /// 光看状态不够，调用方要能读到「此刻他们说到哪了」
    /// </summary>
    /// <param name="sessionId">查询方会话标识（只用来判断该不该提建群/发群提示）</param>
    /// <param name="manager">会话管理器；缺省用全局实例（测试传自建的隔离实例）</param>
    internal static string ListGroups(string sessionId, SessionManager? manager = null)
    {
        manager ??= SessionManager.Instance;
        List<ChatSessionMeta> all = manager.GetSessions().Where(x => x.IsGroup).ToList();

        // 群成员与化身没有「to 填群名」那把 SendMessage（他自己那把只收正文），
        // 只在会话这一层（主代理/单聊）才提这些提示；不在索引里的调用方也按会话层算（工具只对真会话开放）
        bool canSendToGroup = manager.GetMeta(sessionId) is not { IsGroupMember: true };
        if (all.Count == 0)
        {
            return canSendToGroup
                ? $"当前没有群聊。要建一个，用 {CreateGroupMention}；建好后用 {SendMessageMention} 往群里说话（`to` 填群名或短号）。"
                : "当前没有群聊。";
        }

        List<ChatSessionMeta> groups = all.Take(MaxListedGroups).ToList();
        StringBuilder sb = new();
        sb.AppendLine(groups.Count < all.Count
            ? $"群聊（最近 {groups.Count} 个，共 {all.Count}）："
            : $"群聊 {groups.Count} 个：");
        foreach (ChatSessionMeta group in groups) sb.Append(DescribeGroup(group, manager));
        if (canSendToGroup) sb.Append($"往群里说一句用 {SendMessageMention}，`to` 填群名或短号。");
        return sb.ToString();
    }

    /// <summary>
    /// 主动把会话流水导成一份 markdown 并给路径。目标可以是群（id 按群名/短号认，与 SendMessage 的 <c>to</c>
    /// 同一份解析）或本会话派出的子代理（短号/完整标识）。
    /// 显式请求：空闲的群也照导——list-groups 只给在跑群的流水（查询不落痕），「主动要一份当前全量」走这里。
    /// 子代理历史默认只留正文（思考/工具可能很长又占上下文），排查时用 reasoning / tools 加回过程内容
    /// </summary>
    /// <param name="sessionId">查询方会话标识（子代理范围限定在它名下的）</param>
    /// <param name="id">群名/短号，或子代理短号/完整标识</param>
    /// <param name="withReasoning">子代理历史是否导出思考段；群流水不受影响</param>
    /// <param name="withTools">子代理历史是否导出工具调用与结果；群流水不受影响</param>
    /// <param name="manager">会话管理器；缺省用全局实例（测试传自建的隔离实例）</param>
    /// <returns>给模型的回执（含流水文件路径）</returns>
    internal static string Dump(string sessionId, string id, bool withReasoning = false, bool withTools = false,
        SessionManager? manager = null)
    {
        manager ??= SessionManager.Instance;
        string target = id.Trim();
        if (target.Length == 0)
        {
            return $"Error: 要导出的会话标识不能为空。群用 {GroupsMention} 看有哪些，子代理用 {ListToolName} 看有哪些。";
        }

        // 群与子代理各自解析，再合并判断：短号可能同时撞群与子代理，两者都查才报得准
        IReadOnlyList<ChatSessionMeta> groups = GroupMessageChannel.FindGroups(manager, target);
        IReadOnlyList<ChatSessionMeta> subs = SubSessionIdAlias.Match(sessionId, target, manager);
        return (groups.Count + subs.Count) switch
        {
            0 => $"Error: 没有『{target}』这个群或子代理。群用 {GroupsMention} 看有哪些，子代理用 {ListToolName} 看有哪些。",
            1 when groups.Count == 1 => DumpGroup(groups[0], manager),
            1 => DumpSession(subs[0], manager, withReasoning, withTools),
            _ => $"Error: 『{target}』匹配了 {groups.Count} 个群和 {subs.Count} 个子代理，改用完整标识或独一份的群名再来。",
        };
    }

    private static string DumpGroup(ChatSessionMeta group, SessionManager manager)
    {
        // SnapshotOf 会按当前历史重写整份流水（离席中只给已有文件）——对空闲群重写是这次显式请求要的，不是 list-groups 那种查询副作用
        return SnapshotOf(group, manager) is { } path
            ? $"《{group.Title}》流水已写到 {path}（当前 {group.MessageCount} 条发言）。读它看谁说了什么。"
            : $"Error: 写《{group.Title}》的流水失败（临时会话不落盘/离席中/写盘失败）。";
    }

    private static string DumpSession(ChatSessionMeta meta, SessionManager manager, bool withReasoning, bool withTools)
    {
        ChatSession? body = manager.GetLoaded(meta.SessionId) ?? manager.Load(meta.SessionId);
        if (body == null)
        {
            return $"Error: 会话《{meta.Title}》打不开。";
        }

        StringBuilder text = new();
        text.Append(SessionLogText.BuildHeading(body.Title));
        foreach (ChatMessage message in body.History)
        {
            string block = SessionLogText.FormatMessage(message, withReasoning, withTools);
            if (block.Length > 0) text.Append(block);
        }

        text.Append(SessionLogText.FormatUsage(body.TotalInputTokens, body.TotalOutputTokens));

        string path = Path.Combine(AgentBuildProfile.DraftRoomOf(body),
            SessionLogText.FileNameOf(SubSessionIdAlias.Short(meta.SessionId)));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text.ToString());
        }
        catch (Exception e)
        {
            return $"Error: 写《{body.Title}》的流水失败：{e.Message}";
        }

        string hint = withReasoning || withTools
            ? "注意：本次连思考/工具内容一起导出了，文件偏长、上下文占用较高，建议先按大纲模式浏览定位，再按需读对应段。"
            : string.Empty;
        return $"《{body.Title}》流水已写到 {path}（{body.History.Count} 条消息）。读它看这一路的完整过程。{hint}";
    }

    /// <summary>
    /// 建一个群。成员用角色名或标识点名，顺序即发言顺序——调用方不必先去角色库取卡。
    /// 群是绑在<b>当前会话的工作区</b>上的智能体群（成员里的 agent 才动手，普通角色只说话）。
    /// 群成员与化身不建群：建群是会话这一层的事，与「群里的人再开一个群」不是一回事
    /// </summary>
    /// <param name="sessionId">建群会话标识（落进群的 <see cref="ChatSessionMeta.CreatorSessionId"/>）</param>
    /// <param name="workspacePath">群要绑的工作区</param>
    /// <param name="name">群名</param>
    /// <param name="members">成员的角色名或标识，顺序即发言顺序</param>
    /// <param name="mode">调度模式；null 为串行</param>
    /// <param name="stop">并行停止条件；null 为保守</param>
    /// <param name="host">主持人角色名或标识；null 为无</param>
    /// <param name="models">成员各自的模型名，与 members 一一对应；null 或空串 = 跟随全局</param>
    /// <returns>给模型的回执</returns>
    internal static string CreateGroup(string sessionId, string? workspacePath, string name, string[] members,
        string? mode = null, string? stop = null, string? host = null, string[]? models = null)
    {
        if (members == null)
        {
            return $"Error: 还没点名成员——用 {ListCharactersMention} 看有哪些角色。";
        }

        string modeValue = (mode ?? "serial").ToLowerInvariant();
        if (modeValue is not ("serial" or "parallel"))
        {
            return $"Error: mode 只认 serial 或 parallel，拿到「{mode}」。";
        }

        EGroupScheduleMode scheduleMode = modeValue == "serial"
            ? EGroupScheduleMode.Serial
            : EGroupScheduleMode.Parallel;

        string stopValue = (stop ?? "conservative").ToLowerInvariant();
        if (stopValue is not ("conservative" or "aggressive"))
        {
            return $"Error: stop 只认 conservative 或 aggressive，拿到「{stop}」。";
        }

        EGroupStopPolicy stopPolicy = stopValue == "aggressive"
            ? EGroupStopPolicy.Aggressive
            : EGroupStopPolicy.Conservative;

        ChatSessionMeta? caller = SessionManager.Instance.GetMeta(sessionId);
        if (caller == null) return "Error: 当前会话不在会话索引里，建不了群。";
        if (caller.IsGroupMember)
        {
            return "Error: 你是群成员/化身——建群要在主代理或单聊会话里做（群成员的那把 SendMessage 只收正文，发不了群）。";
        }

        try
        {
            ChatSession group = GroupCreation.Create(new GroupCreationSpec
            {
                Name = name,
                Members = members,
                WorkspacePath = workspacePath,
                MinMembers = MinGroupMembers,
                Mode = scheduleMode,
                StopPolicy = stopPolicy,
                Host = string.IsNullOrWhiteSpace(host) ? null : host,
                ModelNames = models is { Length: > 0 } ? models : null,
            });
            return DescribeCreatedGroup(group);
        }
        catch (ArgumentException e)
        {
            // 找成员的错（人不够、点名了不认识的）给模型指条路；其余错误文案已自足，不额外加尾巴
            string hint = e.Message.Contains("至少", StringComparison.Ordinal)
                          || e.Message.Contains("角色库里没有", StringComparison.Ordinal)
                ? $"（用 {ListCharactersMention} 看有哪些角色）"
                : string.Empty;
            return $"Error: {e.Message}{hint}";
        }
    }

    private static string DescribeCreatedGroup(ChatSession group)
    {
        string members = string.Join(" → ", GroupRoster.Of(group).Present.Select(x => x.Name));
        StringBuilder sb = new();
        sb.AppendLine($"已建群《{group.Title}》[{SubSessionIdAlias.Short(group.SessionId)}] · " +
                      $"{(group.IsAgentGroup ? "智能体群" : "普通群")} · 成员 {members}");
        sb.AppendLine(string.IsNullOrWhiteSpace(group.WorkspacePath)
            ? "工作区：未绑（成员写文件落在各自的草稿目录）"
            : $"工作区：{group.WorkspacePath}");
        sb.Append(
            $"往群里说一句用 {SendMessageMention}，`to` 填《{group.Title}》或短号 {SubSessionIdAlias.Short(group.SessionId)}；" +
            $"跑起来后用 {GroupsMention} 看进度（正在跑的群会给你流水快照路径）。");
        return sb.ToString();
    }

    /// <summary>
    /// 已加载的远程模型列表。用 <see cref="LlmManager.GetModelList"/>——参数（窗口、视觉）已经解析好，
    /// 不用自己碰配置；只列远程：本地跑模型消耗大、派活不该用。标出当前模型，
    /// 派子代理时知道默认会落到谁头上。列表可能被模型页重载就地改写，枚举打滑时让你重试
    /// </summary>
    private static string ListModels()
    {
        List<ModelRunningData> models;
        try
        {
            models = LlmManager.Instance.GetModelList().Where(x => x.IsRemoteModel).ToList();
        }
        catch (InvalidOperationException)
        {
            return "模型列表正在刷新，稍后再查一次。";
        }

        if (models.Count == 0) return "当前没有已加载的远程模型（本地模型不列出；在模型页加载或刷新后再查）。";

        string current = LlmManager.Instance.CurrentRunningModel?.ModelName ?? string.Empty;
        StringBuilder sb = new();
        sb.Append($"已加载远程模型 {models.Count} 个：\n");
        foreach (ModelRunningData model in models)
        {
            sb.Append("- ").Append(model.ModelName)
                .Append(" · 上下文 ").Append(TurnUsageLedger.Format(model.ContextLength))
                .Append(" · ").Append(model.IsVisionModel ? "原生视觉" : "非视觉");
            if (string.Equals(model.ModelName, current, StringComparison.Ordinal)) sb.Append(" · 当前");
            if (model.IsLoading) sb.Append(" · 加载中");
            else if (model.IsRunning) sb.Append(" · 在跑");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 角色库里的全部角色及其参数：名字（标识）、agent/普通、内置、一句描述。
    /// 内部角色不列（工具卡不是点名的对象），屏蔽角色按解锁闸门走（选项器同口径）——
    /// 开发者网址解锁后，屏蔽的内置智能体也会出现在这里。全库只是候选参考，
    /// 能点名的以 CreateAgent.character 提示的名单为准。角色库并发增删时枚举可能打滑，让你重试
    /// </summary>
    private static string ListCharacters()
    {
        List<CharacterData> all;
        try
        {
            all = CharacterManager.Instance.CharacterDataDictionary.Values
                .Where(x => CharacterVisibility.IsVisibleCandidate(x))
                .OrderBy(x => x.CharacterName)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return "角色库正在变化，稍后再查一次。";
        }

        if (all.Count == 0) return "当前没有可用角色。";

        StringBuilder sb = new();
        sb.Append($"角色 {all.Count} 个：\n");
        foreach (CharacterData character in all)
        {
            sb.Append("- ").Append(character.CharacterName)
                .Append("（").Append(character.CharacterId).Append("）")
                .Append(" · ").Append(character.IsAgent ? "agent" : "普通");
            if (character.IsBuiltIn) sb.Append(" · 内置");
            if (!string.IsNullOrWhiteSpace(character.Description))
                sb.Append(" · ").Append(Clip(character.Description, MaxDescriptionLength));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>角色用途描述的截断长度：够主代理判断「适不适合这趟活」即可</summary>
    private const int MaxDescriptionLength = 60;

    private static string Clip(string text, int max)
    {
        if (text.Length <= max) return text;
        return text[..(max - 1)].TrimEnd() + "…";
    }

    private static string DescribeSub(ChatSessionMeta meta, ChatSession? body, SessionManager manager)
    {
        // 身份行（主题/短号/点名/只读档）与记忆压缩、回信信头共用同一份，见 SubAgentDescriptor
        StringBuilder sb = new(SubAgentDescriptor.Describe(meta));
        var state = StateOf(manager, meta.SessionId);
        sb.Append(" · ").Append(StateLabel(state));
        string usage = SubAgentDescriptor.DescribeUsage(meta, body);
        if (usage.Length > 0) sb.Append(" · 上下文 ").Append(usage);
        if (meta.BackgroundReportPending) sb.Append(" · 报告未交");
        if (state == ESessionRunState.Idle)
            sb.Append(" · 末轮 ").Append(string.IsNullOrWhiteSpace(FriendlyAge(meta.LastRunStartedAt))
                ? "从未跑过"
                : FriendlyAge(meta.LastRunStartedAt));
        sb.Append(" · ").Append(meta.MessageCount).Append(" 条消息");
        return sb.ToString();
    }

    /// <summary>
    /// 一个群在结果里的样子：一行概况 + 成员状态。
    /// 群在跑时顺带记一份流水快照并把路径写在概况里——跑着的时候「谁说到哪了」只能从流水看
    /// </summary>
    private static string DescribeGroup(ChatSessionMeta group, SessionManager manager)
    {
        List<ChatSessionMeta> members = manager.GetGroupMembers(group.SessionId)
            .Where(x => !x.HasLeftGroup)
            .ToList();

        bool running = StateOf(manager, group.SessionId) != ESessionRunState.Idle;
        StringBuilder sb = new($"- 《{group.Title}》[{SubSessionIdAlias.Short(group.SessionId)}] " +
                               $"· {(group.IsAgentGroup ? "智能体群" : "普通群")} · 在场 {members.Count(x => !x.IsGroupAvatar)} 人 · " +
                               $"在跑 {members.Count(x => StateOf(manager, x.SessionId) != ESessionRunState.Idle)}");
        // 跑着时刷新一份快照并给路径：跑着的时候「谁说到哪了」只能从流水看。
        // 空闲时不给——那份文件停在最后一次写入那一刻，当结论读会少掉后半截
        if (running && SnapshotOf(group, manager) is { } path) sb.Append(" · 流水 ").Append(path);
        if (members.Count == 0) return sb.AppendLine().ToString();

        sb.AppendLine();
        GroupRoster roster = GroupRoster.Of(group);
        foreach (ChatSessionMeta member in members)
        {
            // 认得出名字就写名字（只给编号没人看得懂是谁）；认不出就只剩括号里的短号
            string who = member.IsGroupAvatar
                ? "化身"
                : roster.Everyone.FirstOrDefault(x => x.Meta.SessionId == member.SessionId)?.Name ?? "";
            sb.Append("  - ")
                .Append(who)
                .Append("（").Append(SubSessionIdAlias.Short(member.SessionId)).Append("）：")
                .Append(StateLabel(StateOf(manager, member.SessionId)))
                .AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把流水写进标准的 <c>群流水.md</c>（草稿目录那一份，与离席给化身读的同一份）并返回路径；
    /// 写不出来为 null（告警在 <see cref="GroupLogFile"/> 里）。
    /// 为它得加载群壳本体——历史就是那份流水，而群正在跑时正是要读它的时候。
    ///
    /// ⚠️ 调用方自己把关重写时机：<c>list-groups</c> 只在群<b>在跑</b>时调（空闲时给一份「上次写到哪」的
    /// 旧文件，模型会把它当成结论读——见 <see cref="GroupLogFile.TrySnapshot"/> 的注释）；
    /// <c>chatsession.dump</c> 显式请求时对空闲群也调——主动要一份当前全量，重写不是查询副作用。
    /// 离席中的重写由 <see cref="GroupLogFile.TrySnapshot"/> 自己挡（行号水位），这里不需要再判断
    /// </summary>
    private static string? SnapshotOf(ChatSessionMeta group, SessionManager manager)
    {
        // 群在跑 = 热会话：先取缓存，避免 Load 附带 UnloadColdHistories 的同步写盘
        ChatSession? body = manager.GetLoaded(group.SessionId) ?? manager.Load(group.SessionId);
        return body is { IsGroup: true } ? GroupLogFile.TrySnapshot(body) : null;
    }

    private static ESessionRunState StateOf(SessionManager manager, string sessionId) =>
        manager.Running.StateOf(sessionId);

    private static string StateLabel(ESessionRunState state) => state switch
    {
        ESessionRunState.Running => "工作中",
        ESessionRunState.AwaitingApproval => "等审批",
        _ => "空闲",
    };

    /// <summary>相对时间：刚刚 / N 分钟前 / N 小时前 / N 天前；null 返回空串</summary>
    internal static string FriendlyAge(DateTimeOffset? time)
    {
        if (time == null) return string.Empty;
        TimeSpan age = DateTimeOffset.Now - time.Value;
        string result = age switch
        {
            { TotalMinutes: < 1 } => $"{Math.Max(1, (int)age.TotalSeconds)} 秒前",
            { TotalHours: < 1 } => $"{Math.Max(1, (int)age.TotalMinutes)} 分钟前",
            { TotalDays: < 1 } => $"{Math.Max(1, (int)age.TotalHours)} 小时前",
            _ => $"{Math.Max(1, (int)age.TotalDays)} 天前",
        };
        return result;
    }
}