using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 单聊冒烟用的几步：新建、说话、等这一轮跑完、导出流水。与 <see cref="GroupDevCommands"/> 同一口径——
/// 只走用户那条路：新建是点「新建」再选角色，说话是填输入框再点发送，审批是点审批卡上的按钮。
/// 不带 id 时作用于 session.new 开出的那个会话；带 id（session.post 的返回值）时作用于那个会话，
/// 于是可以开几个单聊并行、分别等
/// </summary>
internal static class SessionDevCommands
{
    /// <summary>
    /// session.new 拿到的那份视图模型；之后的 post / wait / dump 都盯它，不看「当前显示的是哪个」——
    /// 实测跑着跑着显示的会话会换掉，读当前的会把别人的会话当成这一轮
    /// </summary>
    private static ConversationViewModel? _active;

    /// <summary>造出全部单聊步骤</summary>
    /// <returns>步骤集合</returns>
    public static IReadOnlyList<IDevCommand> CreateAll() =>
    [
        new SessionNewCommand(),
        new SessionPostCommand(),
        new SessionWaitCommand(),
        new SessionDumpCommand(),
        new SessionInjectCommand(),
    ];

    internal static ConversationViewModel Active => _active ?? DevCommandRegistry.RequireConversationPage().Conversation;

    internal static void Track(ConversationViewModel conversation) => _active = conversation;

    /// <summary>args 里的 id；没给为 null</summary>
    internal static string? IdOf(JsonElement args) => GroupDevCommands.StringOr(args, "id");

    /// <summary>
    /// 带 id 时找承载那个会话的实例：还在缓存里就直接用，已回收就像在左栏点一下那样打开它（主窗口跟着换）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>装载完的实例</returns>
    internal static async Task<ConversationViewModel> OpenAsync(string sessionId)
    {
        ConversationPageDataBase page = DevCommandRegistry.RequireConversationPage();
        if (page.FindLiveConversation(sessionId) is { } live) return live;

        page.SessionList.SelectSession(sessionId);
        if (page.SessionList.Find(sessionId) is null)
            throw new ArgumentException($"session '{sessionId}' is not in this page's list");
        Stopwatch watch = Stopwatch.StartNew();
        while (page.Conversation.CurrentMeta?.SessionId != sessionId || page.Conversation.IsSessionLoading)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new InvalidOperationException($"session '{sessionId}' did not open");
            await Task.Delay(100);
        }

        return page.Conversation;
    }
}

/// <summary>
/// 进空态并选好角色，与点「新建」再在选择器里挑角色同一条路；会话首轮发送时才建（懒建）。
/// <c>args</c>：character（角色标识或名字）、workspace（智能体侧才用）、permission（权限档序号）、
/// model（会话模型名，与在会话模型下拉里选同一条路；自动挑选不会挑视觉模型，测看图得点名）
/// </summary>
internal sealed class SessionNewCommand : IAsyncDevCommand
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1.5); //页面这么久没换会话、没在装载才算稳

    public string Name => "session.new";

    public string Usage => "在会话页新建单聊并盯住它（首轮发送时才真正建），之后不带 id 的 session.post / wait / dump / inject 都作用于它，再 new 就改盯新的；session.post 返回会话 id，并行开几个单聊时凭 id 分别等。单人干活开单聊，要多人讨论才建群。character、workspace、permission、model";

    public async Task<JsonNode?> ExecuteAsync(JsonElement args)
    {
        if (DevCommandRegistry.RequireConversationPage() is not ConversationPageData page)
            throw new InvalidOperationException("session.new needs the conversation page");

        // 实测：刚启动时页面还在后台恢复上次打开的会话，这时点「新建」会被恢复盖回去，话就说进了旧会话
        await WaitSettledAsync(page);
        page.NewSessionCommand.Execute(null);
        ConversationViewModel conversation = page.Conversation;
        conversation.ChangeCharacter(DevCommandRegistry.ResolveCharacter(DevCommandRegistry.RequireString(args, "character")));
        if (GroupDevCommands.StringOr(args, "workspace") is { } workspace) conversation.Workspace.Path = workspace;
        int permission = GroupDevCommands.IntOr(args, "permission", -1);
        if (permission >= 0) conversation.PermissionModeIndex = permission;
        if (GroupDevCommands.StringOr(args, "model") is { } model)
        {
            conversation.SessionModel.SelectedOption =
                conversation.SessionModel.Options.FirstOrDefault(o => o.ModelName == model)
                ?? throw new InvalidOperationException($"no model named '{model}'");
        }

        SessionDevCommands.Track(conversation);
        return new JsonObject { ["character"] = conversation.ActiveCharacterName, ["agentForm"] = conversation.IsAgentSession };
    }

    private static async Task WaitSettledAsync(ConversationPageDataBase page)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        Stopwatch stable = Stopwatch.StartNew();
        ConversationViewModel shown = page.Conversation;
        while (stable.Elapsed < SettleTime)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("conversation page never settled");
            await Task.Delay(100);
            if (ReferenceEquals(shown, page.Conversation) && !shown.IsSessionLoading) continue;
            shown = page.Conversation;
            stable.Restart();
        }
    }
}

/// <summary>
/// 往会话说一句：填输入框、点发送。不等跑完（那是 session.wait 的事）。
/// 返回会话 id（懒建的会话在首轮发送时才有 id，这里等它出现），之后 wait / dump / post 可凭它点名
/// </summary>
internal sealed class SessionPostCommand : IAsyncDevCommand
{
    public string Name => "session.post";

    public string Usage => "往会话说一句，不等跑完，返回会话 id。text、id（省略为 session.new 盯住的那个；给了就发到那个会话，已回收的会先打开）";

    public async Task<JsonNode?> ExecuteAsync(JsonElement args)
    {
        string text = DevCommandRegistry.RequireString(args, "text");
        ConversationViewModel conversation = SessionDevCommands.IdOf(args) is { } id
            ? await SessionDevCommands.OpenAsync(id)
            : SessionDevCommands.Active;
        conversation.InputText = text;
        _ = conversation.SendMessageCommand.ExecuteAsync(null);

        Stopwatch watch = Stopwatch.StartNew();
        while (conversation.CurrentMeta?.SessionId == null && watch.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(100);
        return new JsonObject { ["chars"] = text.Length, ["id"] = conversation.CurrentMeta?.SessionId };
    }
}

/// <summary>
/// 等当前会话这一轮（含后台子代理、轮末的交接文档）跑完。<c>args</c>：timeoutMinutes、approvals（deny / once / session，默认 deny）、
/// foreground（true 时只等主会话自己这一轮，不等后台子代理——要趁对方还在干活时插话就靠它）。
/// 期间冒出来的审批卡按 approvals 点掉并记进报告——被要了什么本身就是观测量
/// </summary>
internal sealed class SessionWaitCommand : IAsyncDevCommand
{
    private const int IdlePollsToSettle = 3; //连着几次看到闲着才算跑完：发送到开跑之间有空拍

    public string Name => "session.wait";

    public string Usage => "等会话这一轮跑完（默认连后台子代理一起等）。id（省略为 session.new 盯住的那个）、timeoutMinutes、approvals（deny / once / session；扩展卡上 session 即本会话允许该扩展，浏览器接管卡上 once 即「完成了，继续」、deny 即取消）、foreground（true 只等主会话这一轮）";

    public async Task<JsonNode?> ExecuteAsync(JsonElement args)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(GroupDevCommands.IntOr(args, "timeoutMinutes", 10));
        string decision = GroupDevCommands.StringOr(args, "approvals") ?? "deny";
        bool foregroundOnly = GroupDevCommands.BoolOr(args, "foreground", false);

        ConversationPageDataBase page = DevCommandRegistry.RequireConversationPage();
        string? targetId = SessionDevCommands.IdOf(args);
        ConversationViewModel? tracked = targetId == null ? SessionDevCommands.Active : null;
        JsonArray approvals = [];
        JsonArray shownChanges = []; //这一轮跑着时界面换成了别的会话：用户那头就是「看着看着跳走了」
        string? shownId = page.Conversation.CurrentMeta?.SessionId;
        Stopwatch watch = Stopwatch.StartNew();
        int idlePolls = 0;
        while (idlePolls < IdlePollsToSettle)
        {
            if (watch.Elapsed > timeout)
                return new JsonObject
                {
                    ["timedOut"] = true, ["elapsedSeconds"] = (int)watch.Elapsed.TotalSeconds, ["approvals"] = approvals,
                    ["shownChanges"] = shownChanges,
                };

            await Task.Delay(TimeSpan.FromSeconds(1));
            if (page.Conversation.CurrentMeta?.SessionId is var nowShown && nowShown != shownId)
            {
                shownChanges.Add((JsonNode)new JsonObject
                {
                    ["atSeconds"] = (int)watch.Elapsed.TotalSeconds, ["from"] = shownId, ["to"] = nowShown,
                    ["running"] = targetId ?? tracked?.CurrentMeta?.SessionId,
                });
                shownId = nowShown;
            }

            // 按 id 等时实例可能已被回收（跑完了），每拍现找；回收了就只看运行表
            ConversationViewModel? conversation = targetId == null ? tracked : page.FindLiveConversation(targetId);
            foreach (ApprovalRequestItem card in conversation?.Items.OfType<ApprovalRequestItem>().Where(x => !x.IsResolved).ToList() ?? [])
            {
                approvals.Add((JsonNode)new JsonObject { ["tool"] = card.ToolName, ["summary"] = card.ArgumentSummary, ["decision"] = decision });
                card.ResolveCommand.Execute(decision);
            }

            // 交接文档在轮末写，那时这一轮已不算在跑：不等它的话，紧跟着的 quit 会把它掐掉。
            // 唤醒轮一开始就占住会话，界面的 IsGenerating 要等装配完才亮，中间约两秒：
            // 不问占用的话，子代理回信刚落进历史就被当成跑完，下一步的 post 撞上唤醒轮
            string? sessionId = targetId ?? conversation?.CurrentMeta?.SessionId;
            bool busy = (conversation != null && (foregroundOnly ? conversation.IsGenerating : conversation.HasPendingWork))
                        || (sessionId != null && SessionManager.Instance.Running.IsBusy(sessionId))
                        || TurnDriver.IsCompacting(sessionId);
            idlePolls = busy ? 0 : idlePolls + 1;
        }

        return new JsonObject
        {
            ["timedOut"] = false, ["elapsedSeconds"] = (int)watch.Elapsed.TotalSeconds, ["approvals"] = approvals,
            ["shownChanges"] = shownChanges,
        };
    }
}

/// <summary>
/// 把当前会话导成一份 markdown：逐条消息，工具调用带<b>原样参数</b>（参数类型传错这类问题只在这里看得见）。
/// <c>args</c>：path
/// </summary>
internal sealed class SessionDumpCommand : IDevCommand
{
    private const int MaxResultChars = 600; //工具结果只留开头：看的是调用本身，不是读回来的文件

    public string Name => "session.dump";

    public string Usage => "把会话导成 markdown（工具调用带原样参数）。path、id（省略为 session.new 盯住的那个）";

    public JsonNode? Execute(JsonElement args)
    {
        string path = DevCommandRegistry.RequireString(args, "path");
        string id = SessionDevCommands.IdOf(args)
                    ?? SessionDevCommands.Active.CurrentMeta?.SessionId
                    ?? throw new InvalidOperationException("no session yet; post first");
        ChatSession session = SessionManager.Instance.Load(id) ?? throw new InvalidOperationException($"session '{id}' failed to load");
        string? character = DevCommandRegistry.RequireConversationPage().FindLiveConversation(id)?.ActiveCharacterName;

        StringBuilder text = new();
        text.AppendLine(character == null ? $"# {session.Title}" : $"# {session.Title}（{character}）").AppendLine();
        foreach (ChatMessage message in session.History)
        {
            text.AppendLine($"### {message.Role}").AppendLine();
            foreach (AIContent content in message.Contents) AppendContent(text, content);
        }

        text.AppendLine($"输入 {session.TotalInputTokens} · 输出 {session.TotalOutputTokens}");
        File.WriteAllText(path, text.ToString());
        return new JsonObject { ["messages"] = session.History.Count, ["path"] = path };
    }

    private static void AppendContent(StringBuilder text, AIContent content)
    {
        switch (content)
        {
            case TextReasoningContent reasoning when !string.IsNullOrWhiteSpace(reasoning.Text):
                text.AppendLine($"> 思考：{reasoning.Text.Trim().Replace("\n", " ")}").AppendLine();
                break;
            case TextContent plain when !string.IsNullOrWhiteSpace(plain.Text):
                text.AppendLine(plain.Text.Trim()).AppendLine();
                break;
            case FunctionCallContent call:
                text.AppendLine($"- 调用 `{call.Name}`：`{ToJson(call.Arguments)}`");
                break;
            case FunctionResultContent result:
                string body = result.Result?.ToString() ?? "";
                if (body.Length > MaxResultChars) body = body[..MaxResultChars] + "…";
                text.AppendLine($"- 结果：{body.Replace("\n", " ")}").AppendLine();
                break;
        }
    }

    private static string ToJson(IDictionary<string, object?>? arguments) =>
        arguments is not { Count: > 0 } ? "" : JsonSerializer.Serialize(arguments, ToolJson.Arguments);
}
