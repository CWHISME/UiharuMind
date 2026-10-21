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
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 群的发言工具（SendMessage）。两种形态各一把描述，同一把工具名：
/// <list type="bullet">
/// <item><b>成员</b>：一轮<b>中途</b>说话（先说接哪一块、做完一段报结果）。一轮说完的回复正文照样进群（ADR 0060 修订）</item>
/// <item><b>化身</b>（只经 SendMessage）：化身<b>唯一</b>的发群通道——它的回复正文不再自动进群，要说话就调本工具，以用户名义发出</item>
/// </list>
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
    /// <param name="sessionId">发言人的会话标识（成员或化身）</param>
    /// <returns>工具实例</returns>
    public static AITool Create(string sessionId)
    {
        bool isAvatar = SessionManager.Instance.GetMeta(sessionId)?.IsGroupAvatar == true;
        string description = isAvatar
            ? "把要说的话以用户的名义发到群里。你的回复正文不会自动发到群里——" +
              "要说话就调用本工具（告别话也用它，说完再结束离席）；不想说就不调，直接结束本轮。"
            : "在轮次中途对群说点什么（比如开工前说你要接哪一块）。你的最终回复同样会发到群里，所以不要在最终回复里重复已经用本工具说过的话。";
        return ToolJson.CreateFunction(
            ([Description("你要在群里对大家说的话。")]
                string content) => Post(sessionId, content),
            ToolName,
            description);
    }

    private static string Post(string sessionId, string content)
    {
        bool isAvatar = SessionManager.Instance.GetMeta(sessionId)?.IsGroupAvatar == true;
        if (string.IsNullOrWhiteSpace(content)) return "Error: 内容不能为空。";
        if (GroupTranscript.IsPass(content))
            return isAvatar
                ? "Error: 未发送。你没有「不接话」：想不说话就别调用本工具，直接结束本轮。"
                : $"Error: 未发送。想保持沉默就别调用本工具；结束本轮，只回复 {GroupTranscript.PassReply}。";

        if (!isAvatar)
        {
            return GroupChatCoordinator.Instance.TryPostFromMember(sessionId, content)
                ? "已发到群里。"
                : "Error: 未发送。你此刻不在群发言中。";
        }

        return GroupChatCoordinator.Instance.TryPostFromAvatar(sessionId, content) switch
        {
            EAvatarPostResult.Posted => "已发到群里。",
            EAvatarPostResult.AwayEnded => "Error: 未发送。本轮已调 EndAway——结束之后的话不再进群；告别话应在结束前用本工具说完。",
            EAvatarPostResult.NotInTurn or EAvatarPostResult.TurnStopped =>
                "Error: 未发送。此刻没有在跑的化身轮，发不了。",
            _ => "Error: 未发送。此刻不在能发群的轮里（想结束离席，先用本工具把告别话说完，再调 EndAway）。",
        };
    }
}
