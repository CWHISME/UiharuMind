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
/// 群成员在一轮中途先对全群说一句（ADR 0046 决策 4 的修订）。
///
/// 从 <see cref="SubAgentTool"/> 里拆出来单独成一把：群成员不委派，而共用 <c>SendMessage</c> 时
/// 收件人填错（写成用户名、留空）就会静默派出一个子代理——群里看不见、群视图停不了，
/// 它的报告回来还被当成用户的话。这把只收正文，没有收件人可填错。
/// </summary>
public static class GroupPostTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "PostToGroup";

    /// <summary>
    /// 创建群发言工具
    /// </summary>
    /// <param name="memberSessionId">发言人的成员会话标识</param>
    /// <returns>工具实例</returns>
    public static AITool Create(string memberSessionId)
    {
        return AIFunctionFactory.Create(
            ([Description("What you say to everyone in the group, as you would say it in the chat.")]
                string content) => Post(memberSessionId, content),
            ToolName,
            "Say something to the whole group right now, in the middle of your turn. " +
            "Only for words the group should see before you finish; your finished reply is posted anyway.");
    }

    private static string Post(string memberSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Error: content must not be empty.";

        return GroupChatCoordinator.Instance.TryPostFromMember(memberSessionId, content)
            ? "Posted to the group. The rest of this message is not posted again; " +
              "your next finished reply will be posted as usual."
            : "Error: not posted. You are not speaking in a group right now.";
    }
}
