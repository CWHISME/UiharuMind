/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 智能体形态的群成员一轮<b>中途</b>说话（先说接哪一块、做完一段报结果）。一轮说完的回复正文照样进群（ADR 0060 修订）。
///
/// 从 <see cref="SubAgentTool"/> 里拆出来单独成一把：群成员不委派，而共用 <c>SendMessage</c> 时
/// 收件人填错（写成用户名、留空）就会静默派出一个子代理——群里看不见、群视图停不了，
/// 它的报告回来还被当成用户的话。这把只收正文，没有收件人可填错。
/// </summary>
public static class GroupPostTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "SendMessage";

    /// <summary>
    /// 创建群发言工具
    /// </summary>
    /// <param name="memberSessionId">发言人的成员会话标识</param>
    /// <returns>工具实例</returns>
    public static AITool Create(string memberSessionId)
    {
        return AIFunctionFactory.Create(
            ([Description("What you say to everyone in the chat group.")]
                string content) => Post(memberSessionId, content),
            ToolName,
            "Say something to the group mid-turn (e.g. what you take on before working). Your final reply is posted to the group as well, so don't repeat there what you already said with this tool.");
    }

    private static string Post(string memberSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Error: content must not be empty.";
        if (GroupTranscript.IsPass(content))
            return $"Error: not posted. To stay silent, don't call this tool; end your turn replying only {GroupTranscript.PassReply}.";

        return GroupChatCoordinator.Instance.TryPostFromMember(memberSessionId, content)
            ? "Posted to the group."
            : "Error: not posted. You are not speaking in a group right now.";
    }
}
