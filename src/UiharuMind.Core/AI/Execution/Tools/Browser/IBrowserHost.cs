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

    /// <summary>
    /// 读会话标签页的正文（Markdown），不编号：读文章、文档、搜索结果用，比快照短得多
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>正文</returns>
    Task<BrowserTextView> ReadTextAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// 截会话标签页看得见的那一屏
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>截图</returns>
    Task<BrowserScreenshot> ScreenshotAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>有没有窗口给用户看；无头运行时没有，接管无从谈起</summary>
    bool HasWindow { get; }

    /// <summary>
    /// 把会话的浏览器窗口切到最前，交给用户接手：此后闲置回收跳过它、页面弹窗留给用户，直到智能体再动这个标签页
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <returns>切成为 true；没有窗口（无头运行）为 false</returns>
    Task<bool> ShowAsync(string sessionId);

    /// <summary>
    /// 按会话最近一份快照把目标翻回元素，<b>不碰页面</b>：审批卡据此说清这一步动的是什么
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="target">编号或看得见的文字</param>
    /// <returns>元素；会话没开过页面、或翻不出为 null</returns>
    BrowserElement? PeekElement(string sessionId, string target);

    /// <summary>
    /// 指定会话的下载落到哪：页面上点出来的下载不进 Chrome 默认的下载目录，下完挪到这里并把路径告诉模型
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <param name="directory">目录（不存在会在第一次下载时创建）</param>
    void UseDownloadDirectory(string sessionId, string directory);

    /// <summary>
    /// 会话标签页此刻的地址，不碰页面
    /// </summary>
    /// <param name="sessionId">会话 id</param>
    /// <returns>地址；没开过页面为 null</returns>
    string? PeekUrl(string sessionId);
}

/// <summary>
/// 页面上的一个元素，给人看的那几样
/// </summary>
/// <param name="Role">角色（button、link、textbox…）</param>
/// <param name="Name">可访问名称</param>
/// <param name="Secret">是否密码框</param>
public sealed record BrowserElement(string Role, string Name, bool Secret);

/// <summary>
/// 页面的正文
/// </summary>
/// <param name="Url">地址</param>
/// <param name="Title">标题</param>
/// <param name="Text">正文（Markdown）；页面没有可读正文时为空串</param>
/// <param name="Notes">顺带发生的事（弹窗被关掉了）</param>
public sealed record BrowserTextView(string Url, string Title, string Text, IReadOnlyList<string> Notes);

/// <summary>
/// 页面的截图
/// </summary>
/// <param name="Url">地址</param>
/// <param name="Title">标题</param>
/// <param name="Jpeg">JPEG 数据</param>
/// <param name="Notes">顺带发生的事</param>
public sealed record BrowserScreenshot(string Url, string Title, byte[] Jpeg, IReadOnlyList<string> Notes);

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
