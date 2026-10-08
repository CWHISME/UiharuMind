/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 浏览器宿主：一个会话一个标签页，对外只说「打开、看、动手」三件事。
/// 引擎（PuppeteerSharp、将来可能的 Playwright 或接管用户自己的浏览器）都在这层后面，
/// 换引擎不动工具与审批。
/// </summary>
public interface IBrowserHost
{
    /// <summary>
    /// 在会话的标签页里打开地址（没有标签页就开一个，浏览器没起就先起）
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="url">地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>打开后的页面</returns>
    Task<BrowserPageView> NavigateAsync(string sessionId, string url, CancellationToken cancellationToken);

    /// <summary>
    /// 重拍会话标签页的快照
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前页面</returns>
    Task<BrowserPageView> SnapshotAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// 在会话标签页里做一个动作，做完重拍快照
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="request">动作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动作之后的页面</returns>
    Task<BrowserPageView> ActAsync(string sessionId, BrowserActionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 页面在某一刻的样子
/// </summary>
/// <param name="Url">地址</param>
/// <param name="Title">标题</param>
/// <param name="Snapshot">完整快照（见 <see cref="AxSnapshotWriter"/>）</param>
/// <param name="CompactSnapshot">精简版：导航、侧栏、页脚收成一行，编号不变</param>
/// <param name="Notes">这一步顺带发生的事（做了什么、弹窗被关掉了）</param>
public sealed record BrowserPageView(
    string Url, string Title, string Snapshot, string CompactSnapshot, IReadOnlyList<string> Notes);

/// <summary>
/// 一个页面动作
/// </summary>
/// <param name="Action">做什么</param>
/// <param name="Target">对谁：快照里的编号（e5），或没有编号时看得见的文字；整页动作为空</param>
/// <param name="Value">填的字、选项的文字、按键名、滚动方向</param>
public sealed record BrowserActionRequest(EBrowserAction Action, string Target, string Value);

/// <summary>
/// 页面动作的种类
/// </summary>
public enum EBrowserAction
{
    /// <summary>点击</summary>
    Click,
    /// <summary>清空后填入</summary>
    Fill,
    /// <summary>下拉框选一项</summary>
    Select,
    /// <summary>勾上</summary>
    Check,
    /// <summary>取消勾选</summary>
    Uncheck,
    /// <summary>按键（有目标先聚焦）</summary>
    Press,
    /// <summary>悬停</summary>
    Hover,
    /// <summary>整页滚动</summary>
    Scroll,
    /// <summary>后退</summary>
    Back,
}

/// <summary>
/// 写给模型看的动作失败原因（编号过期、找不到元素、选项不存在），工具门面原样交回
/// </summary>
/// <param name="message">原因</param>
public sealed class BrowserActionException(string message) : Exception(message);
