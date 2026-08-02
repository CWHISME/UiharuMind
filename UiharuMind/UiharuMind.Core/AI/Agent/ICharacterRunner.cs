/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// todo 快照。框架的 TodoItem 属 Microsoft.Agents.AI，不外流到 UI 层。
/// </summary>
/// <param name="Title">内容描述</param>
/// <param name="IsComplete">是否已完成</param>
public readonly record struct TodoSnapshot(string Title, bool IsComplete);

/// <summary>
/// 一次对话的执行者：持有底层 agent 与其会话，对外只暴露稳定类型
/// (<see cref="ChatMessage"/> / <see cref="AIContent"/> 来自 Microsoft.Extensions.AI)。
/// 存在的意义是划定编译期边界——Agent Framework 的 preview/alpha 面被 PrivateAssets 挡在 Core 内，
/// UI 层无法直接引用；框架若发生破坏性变更，需要重写的只有本接口的实现。
/// </summary>
public interface ICharacterRunner : IAsyncDisposable
{
    /// <summary>是否已装载会话</summary>
    bool HasSession { get; }

    /// <summary>
    /// 确保底层 agent 与给定配置一致；workspace 或权限档变化时重建，
    /// 已有会话经序列化迁移到新实例（迁移失败则丢弃会话状态）。
    /// </summary>
    /// <param name="workspacePath">绑定的工作目录，null 表示通用助手模式</param>
    /// <param name="permissionMode">权限档</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ConfigureAsync(string? workspacePath, EAgentPermissionMode permissionMode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 绑定到指定会话：已有框架附加状态则恢复，否则新建。
    /// 历史不在框架状态里——它的权威来源是 <see cref="Core.Chat.ChatSession"/>，
    /// 因此附加状态缺失只会丢 todos/mode，不会丢对话。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task AttachSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 丢弃当前会话引用（切换会话前调用，不影响磁盘数据）
    /// </summary>
    void ClearSession();

    /// <summary>
    /// 持久化当前会话的框架附加状态
    /// </summary>
    Task SaveSessionAsync();

    /// <summary>
    /// 运行一轮，产出内容流。审批往返由调用方驱动：
    /// 流中出现 <see cref="ToolApprovalRequestContent"/> 时，把用户的回应作为下一轮消息再次调用。
    /// </summary>
    /// <param name="messages">本轮输入消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>内容增量流</returns>
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
    EAgentMode GetMode();

    /// <summary>
    /// 设置当前会话的 plan/execute 模式；无会话时不做任何事
    /// </summary>
    /// <param name="mode">目标模式</param>
    void SetMode(EAgentMode mode);

    /// <summary>
    /// 获取当前会话的 todo 快照
    /// </summary>
    /// <returns>todo 列表；无会话时为空</returns>
    Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync();

    /// <summary>
    /// 运行中插话：把消息投入注入队列，agent 下一次机会消费
    /// </summary>
    /// <param name="messages">插入的消息</param>
    /// <returns>成功入队返回 true；当前不支持插话返回 false</returns>
    bool TryInject(IEnumerable<ChatMessage> messages);
}
