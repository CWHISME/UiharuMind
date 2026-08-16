/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// todo 快照。框架的 TodoItem 属 Microsoft.Agents.AI，不外流到 UI 层。
/// </summary>
/// <param name="Title">内容描述</param>
/// <param name="IsComplete">是否已完成</param>
public readonly record struct TodoSnapshot(string Title, bool IsComplete);

/// <summary>
/// 一次对话的执行者：持有底层 agent 与其会话，对外只暴露稳定类型
/// (<see cref="ChatMessage"/> / <see cref="AIContent"/> 来自 Microsoft.Extensions.AI)。
///
/// 一个会话只有一个执行者，由会话本体持有（<see cref="ChatSession.Runner"/>），
/// 所有入口（页面、快捷技能、调度）都必须经它执行；实现内部串行——
/// 同一会话的并发请求排队而非交错。会话卸载/删除时由
/// <see cref="ChatSession.DisposeRunnerAsync"/> 释放。
///
/// 角色扮演与 agent 共用这一个执行者，差异由角色的 <see cref="Character.ECharacterKind"/> 决定。
/// 存在的意义同时也是划定编译期边界——Agent Framework 的 preview/alpha 面被 PrivateAssets
/// 挡在 Core 内，UI 层无法直接引用；框架若发生破坏性变更，需要重写的只有本接口的实现。
/// </summary>
public interface ICharacterRunner : IAsyncDisposable
{
    /// <summary>是否已装载会话</summary>
    bool HasSession { get; }

    /// <summary>
    /// 本会话装配好的对话选项（系统提示词、工具集与采样参数）；未挂接时为 null。
    ///
    /// 旁路请求（写交接文档那一发）必须带上同一份。两个理由：模型不该在一个它没见过的身份下
    /// 写这份文档；而请求体的前缀是「system + 工具定义 + 消息」，少任何一段前缀就从那里岔开，
    /// 服务端的前缀缓存整个作废——旁路那一发恰好是占用最高时发出的、最大的一次请求。
    /// </summary>
    ChatOptions? ChatOptions { get; }

    /// <summary>
    /// 绑定到指定会话：按会话的角色、工作目录与权限档装配 agent（变化时重建），
    /// 并恢复框架附加状态。历史不在附加状态里——它的权威来源是会话本体，
    /// 因此附加状态缺失只会丢 todos/mode，不会丢对话。
    /// </summary>
    /// <param name="session">目标会话</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task AttachAsync(ChatSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// 持久化当前会话的框架附加状态
    /// </summary>
    Task SaveStateAsync();

    /// <summary>
    /// 运行一轮，产出内容流。审批往返由调用方驱动：
    /// 流中出现 <see cref="ToolApprovalRequestContent"/> 时，把用户的回应作为下一轮消息再次调用。
    /// 本轮的输入与输出消息由历史提供器自动写入会话，调用方不要重复追加。
    /// </summary>
    /// <param name="messages">本轮输入消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>内容增量流</returns>
    /// <exception cref="InvalidOperationException">未先调用 <see cref="AttachAsync"/> 即运行</exception>
    IAsyncEnumerable<AIContent> RunAsync(IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前会话的历史消息（界面回放用）
    /// </summary>
    /// <returns>历史消息；无会话时为空</returns>
    IReadOnlyList<ChatMessage> GetHistory();

    /// <summary>
    /// 读取当前会话的 plan/execute 模式
    /// </summary>
    /// <returns>模式；无会话时返回 <see cref="EAgentMode.Execute"/></returns>
    Task<EAgentMode> GetModeAsync();

    /// <summary>
    /// 设置当前会话的 plan/execute 模式；无会话时不做任何事
    /// </summary>
    /// <param name="mode">目标模式</param>
    Task SetModeAsync(EAgentMode mode);

    /// <summary>
    /// 获取当前会话的 todo 快照
    /// </summary>
    /// <returns>todo 列表；无会话时为空</returns>
    Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync();

    /// <summary>
    /// 获取本会话实际挂上的能力（工具与 MCP 分组）。默认实现为空快照——
    /// 不走 harness 的形态本就不装配工具，没什么可报的
    /// </summary>
    /// <returns>能力快照</returns>
    AgentCapabilitySnapshot GetCapabilities() => AgentCapabilitySnapshot.Empty;

    /// <summary>
    /// 我们自己估的本轮输入（固定开销 + 历史），与服务端报的取大即<b>有效占用</b>。
    /// 默认 null——不走 harness 的形态没有装配产物，取大时等于只信服务端那个数，
    /// 即本条引入之前的行为。见 ADR 0009
    /// </summary>
    TurnInputEstimate? InputEstimate => null;

    /// <summary>
    /// 运行中插话：把消息投入注入队列，agent 下一次机会消费
    /// </summary>
    /// <param name="messages">插入的消息</param>
    /// <returns>成功入队返回 true；当前不支持插话返回 false</returns>
    Task<bool> TryInjectAsync(IEnumerable<ChatMessage> messages);
}

/// <summary>
/// 从内容流中筛出正文文本。
/// </summary>
public static class CharacterRunnerExtensions
{
    /// <summary>
    /// 运行一轮，只产出正文文本的<b>增量</b>（思考内容与工具调用不计入）。
    /// 增量是原语，累积是消费方的事：快捷工具的各个窗口是把收到的字符串
    /// <b>追加</b>到显示控件上的（AppendContent），若在此处折叠成累积全文，
    /// 窗口会把每次的全文再追加一遍，内容重复且随长度二次膨胀。
    /// </summary>
    /// <param name="runner">执行者</param>
    /// <param name="messages">本轮输入</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文本增量流</returns>
    public static async IAsyncEnumerable<string> RunTextAsync(this ICharacterRunner runner,
        IEnumerable<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        await foreach (AIContent content in runner.RunAsync(messages, cancellationToken).ConfigureAwait(false))
        {
            if (content is TextContent { Text.Length: > 0 } text) yield return text.Text;
        }
    }
}
