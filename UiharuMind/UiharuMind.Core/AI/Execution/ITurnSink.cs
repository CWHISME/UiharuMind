/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 一轮对话的渲染落点。<see cref="TurnDriver"/> 把内容流与几个收尾动作交给它，
/// 自己不认识任何界面类型。
///
/// 界面侧由 <c>ConversationTranscript</c> 实现（这五个成员它本来就有，逐字同名）；
/// 无头执行（定时任务）没有要渲染的东西，传 null 即可。
/// </summary>
public interface ITurnSink
{
    /// <summary>
    /// 装配一段内容
    /// </summary>
    /// <param name="content">来自执行者的一段内容</param>
    void Apply(AIContent content);

    /// <summary>
    /// 收尾当前流段
    /// </summary>
    void CloseSegment();

    /// <summary>
    /// 把仍挂着「运行中」的工具调用收掉——中途停止意味着那条结果永远不会来
    /// </summary>
    /// <param name="note">写进结果区的说明</param>
    void StopRunningToolCalls(string note);

    /// <summary>
    /// 一轮结束时收尾残留的嵌套过程（只可能来自被取消的委派调用）
    /// </summary>
    void CloseNestedActivity();

    /// <summary>
    /// 收尾并取走「正在流的那一段正文」。
    ///
    /// 取消时用它落库：本轮更早的那些段落已经由框架逐次服务调用各自落过盘了，
    /// 只有正在流的这一段随着失败一起丢掉。
    /// </summary>
    /// <returns>正在流的正文；当时没在流正文（比如卡在工具调用上）则为 null</returns>
    string? TakeStreamingText();
}

/// <summary>
/// 一轮对话过程中值得让界面知道的事
/// </summary>
public enum ETurnNotice
{
    /// <summary>本轮开始——此刻起本轮实际使用的模型可解析</summary>
    Started,

    /// <summary>一轮内容流结束——框架内务工具的产物此刻可读（如 todo）</summary>
    RoundCompleted,

    /// <summary>历史已落盘——界面条目此刻可与消息配对</summary>
    Persisted,

    /// <summary>本轮彻底结束（无论成败），界面据此清理待决审批与刷新会话列表</summary>
    Ended,

    /// <summary>本轮失败，<c>Payload</c> 为异常消息</summary>
    Failed,

    /// <summary>请求把视图滚到末尾</summary>
    ScrollToEnd,

    /// <summary>
    /// 知识库检索完成，<c>Payload</c> 为片段全文。界面据此插一张检索卡片，
    /// 让注入路径与 <c>knowledge_search</c> 工具路径观感一致
    /// </summary>
    KnowledgeRetrieved,

    /// <summary>记了一次用量（账本与会话累计已更新，界面据此刷新占用显示）</summary>
    UsageObserved,

    /// <summary>交接文档已写入，<c>Payload</c> 为文档正文</summary>
    HandoffWritten,

    /// <summary>交接文档写失败</summary>
    HandoffFailed,

    /// <summary>上一份交接之后攒下的消息太少，没什么可压（仅手动触发时报）</summary>
    HandoffNothingToCompact,
}

/// <summary>
/// 一条通知。
///
/// 刻意<b>不带文案</b>——本地化属界面层，Core 只说发生了什么。
/// </summary>
/// <param name="Kind">事件种类</param>
/// <param name="Payload">附带正文，只有部分种类有</param>
public readonly record struct TurnNotice(ETurnNotice Kind, string? Payload = null);

/// <summary>
/// 审批回应的取得方式。一轮内容流结束时若冒出过审批请求，
/// <see cref="TurnDriver"/> 调用它取回应，回应即下一轮的输入。
///
/// 交互式实现等用户点选（并可在用户点停止时把待决的一律按拒绝完成）；
/// 无头执行一律拒绝。传 null 表示不进入审批轮次，本轮就此结束。
/// </summary>
/// <param name="requests">本轮新增的审批请求</param>
/// <returns>回应消息</returns>
public delegate Task<IReadOnlyList<ChatMessage>> ApprovalResolver(
    IReadOnlyList<ToolApprovalRequestContent> requests);
