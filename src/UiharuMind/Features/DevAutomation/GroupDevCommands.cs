using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Group;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 群聊冒烟用的几步：建群、往群里说话、继续一圈、等这一波跑完、导出流水。
///
/// 这是 <see cref="DevCommandRegistry"/> 注释里「替用户说话，该单独议」的那一类——议过了：
/// 真模型跑群聊只能这样驱动。口径仍是只走用户那条路：说话是填输入框再点发送，继续是点「继续」，
/// 审批是点待审批条上的按钮；唯独建群跳过弹窗，直接用弹窗交回的同一份参数建。
/// 群一律按群名指认（脚本写的时候还不知道会话标识）
/// </summary>
internal static class GroupDevCommands
{
    /// <summary>造出全部群聊步骤</summary>
    /// <returns>步骤集合</returns>
    public static IReadOnlyList<IDevCommand> CreateAll() =>
    [
        new GroupCreateCommand(),
        new GroupPostCommand(),
        new GroupContinueCommand(),
        new GroupWaitCommand(),
        new GroupDumpCommand(),
        new GroupAwayStartCommand(),
        new GroupAwayEndCommand(),
        new GroupAwayWaitCommand(),
    ];

    /// <summary>按脚本参数里的群名（<c>group</c>）找群壳会话</summary>
    /// <param name="args">脚本参数</param>
    /// <returns>群壳会话</returns>
    internal static ChatSession RequireGroup(JsonElement args) => RequireGroup(DevCommandRegistry.RequireString(args, "group"));

    /// <summary>按群名找群壳会话（重名取最新建的）</summary>
    /// <param name="name">群名</param>
    /// <returns>群壳会话</returns>
    internal static ChatSession RequireGroup(string name)
    {
        string id = SessionManager.Instance.GetSessions()
                        .Where(x => x.IsGroup && x.Title == name)
                        .OrderByDescending(x => x.CreatedAt)
                        .Select(x => x.SessionId)
                        .FirstOrDefault()
                    ?? throw new ArgumentException($"group '{name}' not found");
        return SessionManager.Instance.Load(id) ?? throw new ArgumentException($"group '{name}' failed to load");
    }

    /// <summary>
    /// 在左栏选中这个群并等它装载完——发言、继续都只对当前会话生效，与用户先点开群再打字同一条路
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>当前对话视图模型</returns>
    internal static async Task<ConversationViewModel> OpenAsync(string groupId)
    {
        ConversationPageDataBase page = DevCommandRegistry.RequireConversationPage();
        if (page.Conversation.CurrentMeta?.SessionId != groupId) page.SessionList.SelectSession(groupId);

        // 每个会话一份视图模型，切会话是换实例：每次都要重取 page.Conversation，拿着旧的会永远等不到
        Stopwatch watch = Stopwatch.StartNew();
        while (page.Conversation.CurrentMeta?.SessionId != groupId || page.Conversation.IsSessionLoading)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException($"opening group '{groupId}' timed out");
            await Task.Delay(100);
        }

        return page.Conversation;
    }

    /// <summary>点开群、取右栏离席块（只有智能体群有）</summary>
    /// <param name="group">群壳会话</param>
    /// <returns>离席块的视图数据</returns>
    internal static async Task<GroupAwayViewData> AwayOf(ChatSession group)
    {
        ConversationViewModel conversation = await OpenAsync(group.SessionId);
        return conversation.Group?.Away ?? throw new ArgumentException($"group '{group.Title}' has no away panel (not an agent group)");
    }

    internal static int IntOr(JsonElement args, string name, int fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;

    internal static bool BoolOr(JsonElement args, string name, bool fallback) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    internal static string? StringOr(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static List<string> Strings(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : [];
}

/// <summary>
/// 建一个智能体群并点开它。<c>args</c>：name、members（角色标识或名字，顺序即发言顺序）、models（与成员一一对应，可省）、
/// workspace、mode（serial / parallel）、stop（conservative / aggressive）、host（成员下标，-1 无）、permission（权限档序号）
/// </summary>
internal sealed class GroupCreateCommand : IAsyncDevCommand
{
    public string Name => "group.create";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        string name = DevCommandRegistry.RequireString(args, "name");
        List<CharacterData> members = GroupDevCommands.Strings(args, "members").Select(DevCommandRegistry.ResolveCharacter).ToList();
        List<string> models = GroupDevCommands.Strings(args, "models");
        string workspace = DevCommandRegistry.RequireString(args, "workspace");
        EGroupScheduleMode mode = GroupDevCommands.StringOr(args, "mode") == "parallel"
            ? EGroupScheduleMode.Parallel
            : EGroupScheduleMode.Serial;
        EGroupStopPolicy stop = GroupDevCommands.StringOr(args, "stop") == "aggressive"
            ? EGroupStopPolicy.Aggressive
            : EGroupStopPolicy.Conservative;

        ChatSession group = GroupChatSessions.Create(name, true, members, workspace,
            models.Count > 0 ? models.Cast<string?>().ToList() : null,
            new GroupSchedule(mode, stop, GroupDevCommands.IntOr(args, "host", -1)));

        Stopwatch watch = Stopwatch.StartNew();
        ConversationViewModel conversation = await GroupDevCommands.OpenAsync(group.SessionId);
        long openMs = watch.ElapsedMilliseconds;
        int permission = GroupDevCommands.IntOr(args, "permission", -1);
        if (permission >= 0) conversation.PermissionModeIndex = permission; //与右栏权限档选择器同一个属性

        return new { id = group.SessionId, members = members.Select(x => x.CharacterName), mode = mode.ToString(), openMs };
    }

}

/// <summary>往群里说一句：点开群、填输入框、点发送。不等这一波跑完（那是 group.wait 的事）</summary>
internal sealed class GroupPostCommand : IAsyncDevCommand
{
    public string Name => "group.post";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        ChatSession group = GroupDevCommands.RequireGroup(args);
        string text = DevCommandRegistry.RequireString(args, "text");
        ConversationViewModel conversation = await GroupDevCommands.OpenAsync(group.SessionId);
        conversation.InputText = text;
        _ = conversation.SendMessageCommand.ExecuteAsync(null);
        return new { group = group.Title, chars = text.Length };
    }
}

/// <summary>点「继续」：串行再说一圈，并行叫醒还有新话没听的人。同样不等跑完</summary>
internal sealed class GroupContinueCommand : IAsyncDevCommand
{
    public string Name => "group.continue";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        ChatSession group = GroupDevCommands.RequireGroup(args);
        ConversationViewModel conversation = await GroupDevCommands.OpenAsync(group.SessionId);
        _ = conversation.ContinueGroupRoundCommand.ExecuteAsync(null);
        return new { group = group.Title };
    }
}

/// <summary>
/// 等若干个群都跑完这一波。<c>args</c>：groups（群名数组）、timeoutMinutes、approvals（deny / once，默认 deny）。
/// 期间冒出来的审批按 approvals 在待审批条上点掉，并记进报告——被要了什么本身就是观测量
/// </summary>
internal sealed class GroupWaitCommand : IAsyncDevCommand
{
    private const int IdlePollsToSettle = 3; //连着几次看到闲着才算跑完：开波与收尾之间有空拍

    public string Name => "group.wait";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        List<ChatSession> groups = GroupDevCommands.Strings(args, "groups")
            .Select(GroupDevCommands.RequireGroup)
            .ToList();
        TimeSpan timeout = TimeSpan.FromMinutes(GroupDevCommands.IntOr(args, "timeoutMinutes", 30));
        string decision = GroupDevCommands.StringOr(args, "approvals") ?? "deny";

        List<object> approvals = [];
        Stopwatch watch = Stopwatch.StartNew();
        int idlePolls = 0;
        while (idlePolls < IdlePollsToSettle)
        {
            if (watch.Elapsed > timeout)
            {
                return new { timedOut = true, elapsedSeconds = (int)watch.Elapsed.TotalSeconds, approvals };
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
            foreach (ChatSession group in groups) approvals.AddRange(ResolveApprovals(group, decision));
            idlePolls = groups.Any(x => GroupChatCoordinator.Instance.IsRunning(x.SessionId)) ? 0 : idlePolls + 1;
        }

        return new { timedOut = false, elapsedSeconds = (int)watch.Elapsed.TotalSeconds, approvals };
    }

    // 与群视图输入框上方那条待审批条同一份视图数据，点的是同一个按钮
    private static List<object> ResolveApprovals(ChatSession group, string decision)
    {
        using GroupApprovalsViewData pending = new(group);
        List<object> handled = [];
        foreach (GroupApprovalViewData item in pending.Items.ToList())
        {
            handled.Add(new { group = group.Title, member = item.MemberName, tool = item.Card.ToolName, item.Summary, decision });
            item.Card.ResolveCommand.Execute(decision);
        }

        return handled;
    }
}

/// <summary>
/// 把一个群导成一份 markdown：流水（谁说了什么）、成员累计 token、本群产物。<c>args</c>：group、path
/// </summary>
internal sealed class GroupDumpCommand : IDevCommand
{
    public string Name => "group.dump";

    public object? Execute(JsonElement args)
    {
        ChatSession group = GroupDevCommands.RequireGroup(args);
        string path = DevCommandRegistry.RequireString(args, "path");
        List<(string Name, ChatSession? Session)> members = GroupRoster.Of(group).Present
            .Select(member => (member.Name, SessionManager.Instance.Load(member.SessionId)))
            .ToList();
        if (GroupAvatar.MetaOf(group.SessionId) is { } avatar) members.Add(("化身", SessionManager.Instance.Load(avatar.SessionId)));

        StringBuilder text = new();
        text.AppendLine($"# {group.Title}").AppendLine();
        text.AppendLine("## 流水").AppendLine();
        foreach (ChatMessage post in group.History)
        {
            string speaker = ChatMessageAnnotations.GroupAwayReceiptOf(post) != null ? "离席回执"
                : ChatMessageAnnotations.GroupAvatarPostOf(post) != null ? "用户（化身）"
                : post.Role == ChatRole.User ? "用户" : post.AuthorName ?? "?";
            text.AppendLine($"### {speaker}").AppendLine().AppendLine(post.Text.Trim()).AppendLine();
        }

        text.AppendLine("## 成员用量").AppendLine();
        foreach ((string name, ChatSession? session) in members)
        {
            int toolCalls = session?.History.SelectMany(x => x.Contents).OfType<FunctionCallContent>().Count() ?? 0;
            text.AppendLine($"- {name}：输入 {session?.TotalInputTokens ?? 0} · 输出 {session?.TotalOutputTokens ?? 0} · 工具调用 {toolCalls} 次");
        }

        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.CollectFor(group);
        text.AppendLine().AppendLine("## 本群产物").AppendLine();
        foreach (GroupArtifact artifact in artifacts)
        {
            text.AppendLine($"- [{artifact.Source}] {artifact.DisplayPath}（{string.Join("、", artifact.Authors)}）");
        }

        File.WriteAllText(path, text.ToString());
        return new { group = group.Title, posts = group.History.Count, artifacts = artifacts.Count, path };
    }
}

/// <summary>
/// 开始离席：点开群，在右栏离席块里填目标、授权范围、选化身模型、点「开始离席」。<c>args</c>：group、goal、mandate（可省）、model（模型名，省略跟随全局）
/// </summary>
internal sealed class GroupAwayStartCommand : IAsyncDevCommand
{
    public string Name => "group.away.start";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        ChatSession group = GroupDevCommands.RequireGroup(args);
        GroupAwayViewData away = await GroupDevCommands.AwayOf(group);
        away.Goal = DevCommandRegistry.RequireString(args, "goal");
        away.Mandate = GroupDevCommands.StringOr(args, "mandate") ?? string.Empty;
        if (GroupDevCommands.StringOr(args, "model") is { } model)
        {
            away.SelectedModel = away.ModelOptions.FirstOrDefault(x => x.ModelName == model)
                                 ?? throw new ArgumentException($"model '{model}' not found");
        }

        away.StartCommand.Execute(null);
        return new { group = group.Title, away = GroupAwayController.Instance.IsAway(group.SessionId), model = away.SelectedModel.DisplayName };
    }
}

/// <summary>点离席块上的「结束离席」</summary>
internal sealed class GroupAwayEndCommand : IAsyncDevCommand
{
    public string Name => "group.away.end";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        ChatSession group = GroupDevCommands.RequireGroup(args);
        (await GroupDevCommands.AwayOf(group)).EndCommand.Execute(null);
        return new { group = group.Title };
    }
}

/// <summary>
/// 等离席结束。<c>args</c>：group、timeoutMinutes（到点就点「结束离席」收场）、skipDelays（true 时一出倒计时就点「立即唤醒」，冒烟不必真等退避）。
/// 报告带化身出手次数与回执原文
/// </summary>
internal sealed class GroupAwayWaitCommand : IAsyncDevCommand
{
    public string Name => "group.away.wait";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        ChatSession group = GroupDevCommands.RequireGroup(args);
        GroupAwayViewData away = await GroupDevCommands.AwayOf(group);
        TimeSpan timeout = TimeSpan.FromMinutes(GroupDevCommands.IntOr(args, "timeoutMinutes", 60));
        bool skipDelays = GroupDevCommands.BoolOr(args, "skipDelays", false);

        Stopwatch watch = Stopwatch.StartNew();
        int wakes = 0;
        bool timedOut = false;
        while (GroupAwayController.Instance.IsAway(group.SessionId))
        {
            if (watch.Elapsed > timeout)
            {
                timedOut = true;
                away.EndCommand.Execute(null);
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
            if (!skipDelays || !away.HasCountdown) continue;
            wakes++;
            away.WakeNowCommand.Execute(null);
        }

        GroupAwayReceipt? receipt = group.History.Select(GroupAwayReceipt.Of).LastOrDefault(x => x != null);
        return new
        {
            group = group.Title, timedOut, elapsedSeconds = (int)watch.Elapsed.TotalSeconds, skippedDelays = wakes,
            receipt = receipt == null ? null : GroupAwayReceiptText.Format(receipt),
        };
    }
}
