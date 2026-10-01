using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 单聊冒烟用的几步：新建、说话、等这一轮跑完、导出流水。与 <see cref="GroupDevCommands"/> 同一口径——
/// 只走用户那条路：新建是点「新建」再选角色，说话是填输入框再点发送，审批是点审批卡上的按钮。
/// 都作用于 session.new 开出的那个会话，所以先 <c>page.jump</c>
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
    ];

    internal static ConversationViewModel Active => _active ?? DevCommandRegistry.RequireConversationPage().Conversation;

    internal static void Track(ConversationViewModel conversation) => _active = conversation;
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

    public async Task<object?> ExecuteAsync(JsonElement args)
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
        return new { character = conversation.ActiveCharacterName, agentForm = conversation.IsAgentSession };
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

/// <summary>往当前会话说一句：填输入框、点发送。不等跑完（那是 session.wait 的事）</summary>
internal sealed class SessionPostCommand : IDevCommand
{
    public string Name => "session.post";

    public object? Execute(JsonElement args)
    {
        string text = DevCommandRegistry.RequireString(args, "text");
        ConversationViewModel conversation = SessionDevCommands.Active;
        conversation.InputText = text;
        _ = conversation.SendMessageCommand.ExecuteAsync(null);
        return new { chars = text.Length };
    }
}

/// <summary>
/// 等当前会话这一轮（含后台子代理、轮末的交接文档）跑完。<c>args</c>：timeoutMinutes、approvals（deny / once，默认 deny）。
/// 期间冒出来的审批卡按 approvals 点掉并记进报告——被要了什么本身就是观测量
/// </summary>
internal sealed class SessionWaitCommand : IAsyncDevCommand
{
    private const int IdlePollsToSettle = 3; //连着几次看到闲着才算跑完：发送到开跑之间有空拍

    public string Name => "session.wait";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(GroupDevCommands.IntOr(args, "timeoutMinutes", 10));
        string decision = GroupDevCommands.StringOr(args, "approvals") ?? "deny";

        ConversationViewModel conversation = SessionDevCommands.Active;
        ConversationPageDataBase page = DevCommandRegistry.RequireConversationPage();
        List<object> approvals = [];
        List<object> shownChanges = []; //这一轮跑着时界面换成了别的会话：用户那头就是「看着看着跳走了」
        string? shownId = page.Conversation.CurrentMeta?.SessionId;
        Stopwatch watch = Stopwatch.StartNew();
        int idlePolls = 0;
        while (idlePolls < IdlePollsToSettle)
        {
            if (watch.Elapsed > timeout)
                return new { timedOut = true, elapsedSeconds = (int)watch.Elapsed.TotalSeconds, approvals, shownChanges };

            await Task.Delay(TimeSpan.FromSeconds(1));
            if (page.Conversation.CurrentMeta?.SessionId is var nowShown && nowShown != shownId)
            {
                shownChanges.Add(new { atSeconds = (int)watch.Elapsed.TotalSeconds, from = shownId, to = nowShown, running = conversation.CurrentMeta?.SessionId });
                shownId = nowShown;
            }

            foreach (ApprovalRequestItem card in conversation.Items.OfType<ApprovalRequestItem>().Where(x => !x.IsResolved).ToList())
            {
                approvals.Add(new { tool = card.ToolName, summary = card.ArgumentSummary, decision });
                card.ResolveCommand.Execute(decision);
            }

            // 交接文档在轮末写，那时这一轮已不算在跑：不等它的话，紧跟着的 quit 会把它掐掉
            bool busy = conversation.HasPendingWork || TurnDriver.IsCompacting(conversation.CurrentMeta?.SessionId);
            idlePolls = busy ? 0 : idlePolls + 1;
        }

        return new { timedOut = false, elapsedSeconds = (int)watch.Elapsed.TotalSeconds, approvals, shownChanges };
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

    public object? Execute(JsonElement args)
    {
        string path = DevCommandRegistry.RequireString(args, "path");
        ConversationViewModel conversation = SessionDevCommands.Active;
        string id = conversation.CurrentMeta?.SessionId ?? throw new InvalidOperationException("no session yet; post first");
        ChatSession session = SessionManager.Instance.Load(id) ?? throw new InvalidOperationException($"session '{id}' failed to load");

        StringBuilder text = new();
        text.AppendLine($"# {session.Title}（{conversation.ActiveCharacterName}）").AppendLine();
        foreach (ChatMessage message in session.History)
        {
            text.AppendLine($"### {message.Role}").AppendLine();
            foreach (AIContent content in message.Contents) AppendContent(text, content);
        }

        text.AppendLine($"输入 {session.TotalInputTokens} · 输出 {session.TotalOutputTokens}");
        File.WriteAllText(path, text.ToString());
        return new { messages = session.History.Count, path };
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
                text.AppendLine($"- 调用 `{call.Name}`：`{JsonSerializer.Serialize(call.Arguments)}`");
                break;
            case FunctionResultContent result:
                string body = result.Result?.ToString() ?? "";
                if (body.Length > MaxResultChars) body = body[..MaxResultChars] + "…";
                text.AppendLine($"- 结果：{body.Replace("\n", " ")}").AppendLine();
                break;
        }
    }
}
