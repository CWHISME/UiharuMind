using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 让一个成员在他自己的会话上跑一轮、或往他正在跑的那一轮里插一句。
/// 抽成接口是为了 <see cref="GroupChatCoordinator"/> 能不起模型地测：调度与投递的对错跟模型无关。
/// </summary>
public interface IGroupMemberTurnRunner
{
    /// <summary>
    /// 跑一轮。输入与输出都落在成员自己的会话里
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="input">这一轮的输入（投递正文）</param>
    /// <param name="cancellationToken">用户停止群聊时取消</param>
    /// <returns>正常跑完为 true；失败或被取消为 false——那时的半截输出不算群发言</returns>
    Task<bool> RunAsync(ChatSession member, ChatMessage input, CancellationToken cancellationToken);

    /// <summary>
    /// 往成员正在跑的那一轮里插一句，在安全点（下一次模型调用前）被消费
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="message">插进去的消息</param>
    /// <returns>排进去了为 true</returns>
    Task<bool> TryInjectAsync(ChatSession member, ChatMessage message);
}
