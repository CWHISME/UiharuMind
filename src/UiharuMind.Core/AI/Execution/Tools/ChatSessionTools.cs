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
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

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

    /// <summary>groups：群聊状态</summary>
    public const string GroupsToolName = "ChatSessionGroups";

    /// <summary>models：已加载的模型列表（参数已解析）</summary>
    public const string ListModelsToolName = "ChatSessionModels";

    /// <summary>characters：角色列表与其参数</summary>
    public const string ListCharactersToolName = "ChatSessionCharacters";

    /// <summary>stop：强行停止一个正在跑的子代理</summary>
    public const string StopToolName = "ChatSessionStop";

    /// <summary>
    /// 造五把工具
    /// </summary>
    /// <param name="sessionId">当前会话标识；空串时报「不可用」（扩展装配时拦截）</param>
    /// <returns>五把工具，list 在前</returns>
    public static IReadOnlyList<AITool> Create(string sessionId) =>
    [
        ToolJson.CreateFunction(() => ListSubSessions(sessionId), ListToolName,
            "列出本会话派出的子代理及其状态。"),
        ToolJson.CreateFunction(() => ListGroups(false), GroupsToolName,
            "列出群聊及其状态。"),
        ToolJson.CreateFunction(() => ListModels(), ListModelsToolName,
            "列出已加载的远程模型及其参数。"),
        ToolJson.CreateFunction(() => ListCharacters(), ListCharactersToolName,
            "列出角色库。"),
        ToolJson.CreateFunction(([Description("要停止的子代理短号或完整标识")] string id) => StopSubSession(sessionId, id),
            StopToolName,
            "强行停止本会话派出的一个正在跑的子代理。"),
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
    /// 群聊状态。「群里谁在跑」由群壳的成员名单与运行登记聚合；化身单独报（不在名单里）
    /// </summary>
    internal static string ListGroups(bool mine, SessionManager? manager = null)
    {
        manager ??= SessionManager.Instance;
        List<ChatSessionMeta> all = manager.GetSessions().Where(x => x.IsGroup).ToList();
        List<ChatSessionMeta> groups = all.Take(5).ToList(); // 群可能很多，全列占上下文，最近 5 个够看
        if (groups.Count == 0) return "当前没有群聊。";

        StringBuilder sb = new();
        if (mine)
        {
            sb.AppendLine("当前还无法按「本会话发起的群聊」过滤——群的创建尚未记录发起会话，以下先列出最近的：");
        }

        sb.AppendLine(groups.Count < all.Count
            ? $"群聊（最近 {groups.Count} 个，共 {all.Count}）："
            : $"群聊 {groups.Count} 个：");
        foreach (ChatSessionMeta group in groups) sb.AppendLine(DescribeGroup(group, manager));
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
    /// 内部角色与屏蔽角色不列（选项器同口径）；全库只是候选参考，能点名的以
    /// CreateAgent.character 提示的名单为准。角色库并发增删时枚举可能打滑，让你重试
    /// </summary>
    private static string ListCharacters()
    {
        List<CharacterData> all;
        try
        {
            all = CharacterManager.Instance.CharacterDataDictionary.Values
                .Where(x => !x.IsInternal && !x.IsShielded)
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
        // 索引没有主题字段；标题就是 BuildTitle 的产物（派活时给了主题用主题，否则任务首行）
        string topic = meta.Title;
        StringBuilder sb = new(topic.Length > 0
            ? $"- {topic}[{SubSessionIdAlias.Short(meta.SessionId)}]"
            : $"- [{SubSessionIdAlias.Short(meta.SessionId)}]");
        if (meta.SubAgentName.Length > 0) sb.Append(" · 点名：").Append(meta.SubAgentName);
        // 探索档恒定只读（CreateAgent 置 readOnly 派出）：主代理续用前要知道这位改不了东西
        if (meta.SubAgentType == ESubAgentType.Explorer) sb.Append(" · 只读");
        var state = StateOf(manager, meta.SessionId);
        sb.Append(" · ").Append(StateLabel(state));
        string usage = UsageOf(meta, body);
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
    /// 上下文占用行（k/M 简写）：主代理判断还能不能往这位身上派/续用。
    /// 热会话（已驻留本体）占用读本体、窗口与模型名精确；冷会话只有索引里的占用快照，
    /// 窗口/模型不猜（与界面「上限未知只给占用」同一口径）。没跑过为空串，整段省略
    /// </summary>
    private static string UsageOf(ChatSessionMeta meta, ChatSession? body)
    {
        long usage = body != null ? body.LastInputTokens : meta.LastInputTokens;
        if (usage <= 0) return string.Empty;
        if (body == null) return TurnUsageLedger.Format(usage);

        int contextLength = body.ChatModelRunningData?.ContextLength ?? 0;
        string line = contextLength > 0
            ? $"{TurnUsageLedger.Format(usage)} / {TurnUsageLedger.Format(contextLength)}"
            : TurnUsageLedger.Format(usage);
        string? modelName = body.ChatModelRunningData?.ModelName;
        return string.IsNullOrEmpty(modelName) ? line : $"{line}（{modelName}）";
    }

    private static string DescribeGroup(ChatSessionMeta group, SessionManager manager)
    {
        List<ChatSessionMeta> members = manager.GetGroupMembers(group.SessionId)
            .Where(x => !x.HasLeftGroup)
            .ToList();

        StringBuilder sb = new($"- 《{group.Title}》[群壳 {SubSessionIdAlias.Short(group.SessionId)}] " +
                               $"· {(group.IsAgentGroup ? "智能体群" : "普通群")} · 在场 {members.Count(x => !x.IsGroupAvatar)} 人 · 在跑 {members.Count(x => StateOf(manager, x.SessionId) != ESessionRunState.Idle)}");
        if (members.Count == 0) return sb.AppendLine().ToString();

        sb.AppendLine();
        foreach (ChatSessionMeta member in members)
        {
            string who = member.IsGroupAvatar ? "化身" :
                !string.IsNullOrWhiteSpace(member.Title) ? member.Title : member.CharacterId;
            sb.Append("  - ")
                .Append(who)
                .Append("（").Append(SubSessionIdAlias.Short(member.SessionId)).Append("）：")
                .Append(StateLabel(StateOf(manager, member.SessionId)))
                .AppendLine();
        }

        return sb.ToString();
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