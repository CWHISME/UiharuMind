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
/// 智能体形态的群成员在群里说话的唯一通道（ADR 0060）：回复正文留在他自己那里，不调用就是不接话。
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
            "You may call it several times in a turn (e.g. say what you take on, then report the result). ");
    }

    private static string Post(string memberSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Error: content must not be empty.";
        if (GroupTranscript.IsPass(content))
            return "Error: not posted. To stay silent, don't call this tool; just end your turn.";

        return GroupChatCoordinator.Instance.TryPostFromMember(memberSessionId, content)
            ? "Posted to the group."
            : "Error: not posted. You are not speaking in a group right now.";
    }
}
