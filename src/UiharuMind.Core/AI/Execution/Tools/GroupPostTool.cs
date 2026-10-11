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
/// 从 <see cref="SubAgentTool"/> 里拆出来单独成一把：群成员不委派，而从前委派与群发言共用一把时
/// 收件人填错（写成用户名、留空）就会静默派出一个子代理——群里看不见、群视图停不了，
/// 它的报告回来还被当成用户的话。这把只收正文，没有收件人可填错。
/// 与委派的续聊工具（<see cref="SubAgentTool.MessageToolName"/>）同名：两者从不同场，由不变量测试钉住。
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
            ([Description("你要在群里对大家说的话。")]
                string content) => Post(memberSessionId, content),
            ToolName,
            "在轮次中途对群说点什么（比如开工前说你要接哪一块）。你的最终回复同样会发到群里，所以不要在最终回复里重复已经用本工具说过的话。");
    }

    private static string Post(string memberSessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Error: 内容不能为空。";
        if (GroupTranscript.IsPass(content))
            return $"Error: 未发送。想保持沉默就别调用本工具；结束本轮，只回复 {GroupTranscript.PassReply}。";

        return GroupChatCoordinator.Instance.TryPostFromMember(memberSessionId, content)
            ? "已发到群里。"
            : "Error: 未发送。你此刻不在群发言中。";
    }
}
