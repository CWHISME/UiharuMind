/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools.Accessibility;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 桌面的平台层（ADR 0084）：列窗口、读无障碍树、动手。只管「能不能、怎么做」，
/// 会话、编号、审批、拒绝名单都在 <see cref="DesktopHost"/>，各平台一份实现（macOS 的 AX、Windows 的 UI Automation）。
/// 调用可能阻塞（跨进程问另一个应用），宿主在线程池上调
/// </summary>
public interface IDesktopPlatform
{
    /// <summary>此刻用不了的原因（没授权无障碍）；能用为 null</summary>
    string? Unavailable { get; }

    /// <summary>
    /// 请系统授权（macOS 弹出无障碍授权框）；不需要授权的平台什么也不做
    /// </summary>
    void RequestAccess();

    /// <summary>
    /// 列出用户能看到的应用窗口（含最小化的），前台的在前
    /// </summary>
    /// <returns>窗口</returns>
    IReadOnlyList<DesktopWindowInfo> ListWindows();

    /// <summary>
    /// 两次列出的是不是同一个窗口（编号据此跨次复用）
    /// </summary>
    /// <param name="a">一个</param>
    /// <param name="b">另一个</param>
    /// <returns>是为 true</returns>
    bool SameWindow(DesktopWindowInfo a, DesktopWindowInfo b);

    /// <summary>
    /// 读一个窗口的无障碍树。根是窗口本身，节点的 <see cref="AxNode.Source"/> 留着找回真实元素的门路，
    /// 角色映射成与网页同一套名字（button、textbox、checkbox…）
    /// </summary>
    /// <param name="window">窗口</param>
    /// <param name="maxNodes">最多读多少个节点，超出的列成一行「还有几项」</param>
    /// <returns>树</returns>
    /// <exception cref="DesktopWindowGoneException">窗口已经关了</exception>
    AxNode ReadWindow(DesktopWindowInfo window, int maxNodes);

    /// <summary>
    /// 第 1 级：用无障碍接口自带的动作做，不动光标、不抢前台
    /// </summary>
    /// <param name="node">目标（出自 <see cref="ReadWindow"/>）</param>
    /// <param name="action">动作：只会是点击、填入、打字、选择、聚焦</param>
    /// <param name="value">填入或打的字、要选的那一项</param>
    /// <returns>做成为 true；这个控件不支持（交给第 2 级）为 false</returns>
    bool TryPerform(AxNode node, EDesktopAction action, string value);

    /// <summary>
    /// 第 2、3 级：发键鼠事件。<paramref name="real"/> 为 false 时只投递给窗口所属进程，不动光标；
    /// 为 true 时是真实键鼠（宿主已把窗口提到前台、问过用户）
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="input">事件</param>
    /// <param name="real">是否真实键鼠</param>
    void Input(DesktopWindowInfo window, DesktopInput input, bool real);

    /// <summary>
    /// 把窗口提到最前（并激活它的应用）
    /// </summary>
    /// <param name="window">窗口</param>
    /// <returns>做成为 true</returns>
    bool Raise(DesktopWindowInfo window);

    /// <summary>用户最近一次动键盘鼠标（硬件输入，不含这里发的事件）距今多少秒</summary>
    double SecondsSinceUserInput { get; }

    /// <summary>
    /// 启动应用，或用默认应用打开文件
    /// </summary>
    /// <param name="target">应用名或文件的绝对路径</param>
    /// <param name="isFile">是不是文件</param>
    /// <exception cref="AxActionException">打不开（没有这个应用）</exception>
    void Open(string target, bool isFile);
}

/// <summary>
/// 窗口已经关了（读它时发现）：动作把窗口关掉是正常结局，宿主据此如实说，而不是报错
/// </summary>
/// <param name="window">窗口</param>
public sealed class DesktopWindowGoneException(DesktopWindowInfo window)
    : AxActionException($"The window \"{window.Title}\" ({window.App}) is closed.");

/// <summary>
/// 平台列出的一个窗口
/// </summary>
/// <param name="Pid">所属进程</param>
/// <param name="App">应用名（给人看的）</param>
/// <param name="AppId">应用标识（macOS 的 bundle id、Windows 的可执行文件名），拒绝名单与审批按它认</param>
/// <param name="Title">标题</param>
/// <param name="Bounds">位置与大小（逻辑坐标）</param>
public sealed record DesktopWindowInfo(int Pid, string App, string AppId, string Title, AxRect Bounds)
{
    /// <summary>是否前台窗口</summary>
    public bool Focused { get; init; }

    /// <summary>是否最小化</summary>
    public bool Minimized { get; init; }

    /// <summary>平台侧的窗口句柄，平台据此找回窗口；宿主不碰</summary>
    public object? Handle { get; init; }
}

/// <summary>
/// 一次键鼠事件（第 2、3 级）
/// </summary>
/// <param name="Kind">种类</param>
public sealed record DesktopInput(EDesktopInputKind Kind)
{
    /// <summary>点击、滚动的位置（屏幕逻辑坐标）</summary>
    public double X { get; init; }

    /// <summary>点击、滚动的位置（屏幕逻辑坐标）</summary>
    public double Y { get; init; }

    /// <summary>点几下</summary>
    public int Clicks { get; init; } = 1;

    /// <summary>是否右键</summary>
    public bool RightButton { get; init; }

    /// <summary>按键：键名（与浏览器同一套，见 BrowserKeys），修饰键在前</summary>
    public IReadOnlyList<string> Keys { get; init; } = [];

    /// <summary>要打的字</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>滚几行：正数向下</summary>
    public int ScrollLines { get; init; }
}

/// <summary>
/// 键鼠事件的种类
/// </summary>
public enum EDesktopInputKind
{
    /// <summary>点击</summary>
    Click,
    /// <summary>按键（组合键）</summary>
    Key,
    /// <summary>在焦点处打字</summary>
    Text,
    /// <summary>全选焦点里的内容（替换输入前用）</summary>
    SelectAll,
    /// <summary>滚动</summary>
    Scroll,
}

/// <summary>
/// 窗口里的动作
/// </summary>
public enum EDesktopAction
{
    /// <summary>点击</summary>
    Click,
    /// <summary>双击</summary>
    DoubleClick,
    /// <summary>右键</summary>
    RightClick,
    /// <summary>清空后填入</summary>
    Fill,
    /// <summary>在焦点处（或先聚焦目标）打字，不清空</summary>
    Type,
    /// <summary>下拉框、列表里选一项</summary>
    Select,
    /// <summary>勾上</summary>
    Check,
    /// <summary>取消勾选</summary>
    Uncheck,
    /// <summary>按键（有目标先聚焦）</summary>
    Press,
    /// <summary>聚焦</summary>
    Focus,
    /// <summary>滚动</summary>
    Scroll,
}
