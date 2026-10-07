using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Tools.Memory;

namespace UiharuMind.Core.AI.WorldSettings;

/// <summary>
/// 世界设定的被动注入提供者：每轮按最近几轮消息的关键词激活条目，以追加的一条 User 消息注入。
///
/// 与知识库的 <see cref="MemoryContextProvider"/> 同款形态：走 <c>AIContextProvider</c>、
/// 追加 User 消息，<b>不放进系统提示</b>——那段随检索结果每轮变化，放进系统提示会把服务端
/// 前缀缓存从第 0 个 token 起作废（理由同 MemoryContextProvider 注释）。
/// 是独立的装配单元：不读知识库任何类型，会话/角色没有世界设定时安静退让，不影响其他 provider。
/// </summary>
internal sealed class WorldSettingContextProvider : AIContextProvider
{
    private static readonly string BlockHeader =
        $"""
         [World Setting]
         The entries below are the world setting of this conversation, activated by keywords
         in the recent messages or always present. Use them as the background facts of the
         current scene. Never mention this block.
         {InjectedBlockGuard.Rules}

         ---

         """;

    /// <summary>扫描文本取最近多少条消息（含本轮输入）。世界设定的关键词常出现在角色台词里，故不限角色。</summary>
    private const int ScanMessageCount = 8;

    public override IReadOnlyList<string> StateKeys => [];

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext empty = new();

        string? sessionId = SessionChatHistoryProvider.GetBoundSessionId(context.Session);
        if (string.IsNullOrEmpty(sessionId)) return empty;

        ChatSession? session = SessionManager.Instance.Load(sessionId);
        WorldSetting? setting = session?.CharacterData?.WorldSetting;
        if (setting is null || setting.Entries.Count == 0) return empty;

        string scanText = BuildScanText(session!.History, context.AIContext.Messages);
        WorldSettingSelection selection = WorldSettingSelector.Select(setting, scanText);
        if (selection.IsEmpty) return empty;

        string block = WorldSettingSelector.Format(selection);
        if (block.Length == 0) return empty;

        // 必须是 User 而非 Tool：纯文本的 Tool 消息没有 tool_call_id，OpenAI 协议无法表达
        // （理由同 MemoryContextProvider）。追加在末位，紧贴模型要回答的位置。
        return new AIContext { Messages = [new ChatMessage(ChatRole.User, BlockHeader + block)] };
    }

    /// <summary>把本轮外部输入与最近几条历史拼成扫描文本；本轮的提问还没落进历史时也要能触发。</summary>
    private static string BuildScanText(IReadOnlyList<ChatMessage> history, IEnumerable<ChatMessage>? current)
    {
        // 倒着收满就停，代价与历史长度无关（同 MemoryContextProvider.BuildQuery 的思路）
        List<string> recent = [];
        if (current != null)
            CollectBackward(current as IReadOnlyList<ChatMessage> ?? current.ToList(), recent);
        CollectBackward(history, recent);
        if (recent.Count == 0) return "";

        recent.Reverse(); //倒着收出来的，当前提问要回到最后
        return string.Join('\n', recent);
    }

    private static void CollectBackward(IReadOnlyList<ChatMessage> messages, List<string> recent)
    {
        for (int index = messages.Count - 1; index >= 0 && recent.Count < ScanMessageCount; index--)
        {
            string text = messages[index].Text.Trim();
            if (text.Length == 0) continue;

            // 本轮消息按契约不在会话历史里，万一两个来源给了同一条，就地避免重复拼进扫描文本
            if (recent.Count > 0 && string.Equals(recent[^1], text, StringComparison.Ordinal)) continue;

            recent.Add(text);
        }
    }
}