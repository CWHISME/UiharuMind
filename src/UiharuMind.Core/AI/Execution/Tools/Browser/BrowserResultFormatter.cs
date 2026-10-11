/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 把页面写成交给模型的工具结果，并记住上一份（一个会话一个）：比对、落盘与精简交给 <see cref="AxResultFormatter"/>，
/// 这里只添网页独有的两样——正文与截图。
/// </summary>
/// <param name="spillDirectory">超长快照落盘的目录</param>
internal sealed class BrowserResultFormatter(string spillDirectory)
{
    private readonly AxResultFormatter _snapshots = new(spillDirectory, new AxResultWording("browser", "BrowserSnapshot",
        "BrowserFind", "(the page has no readable content)", (fullBytes, cut, saved) =>
            "Long page: navigation, sidebars and footers are collapsed" +
            (cut ? " and the rest of the page is cut" : string.Empty) +
            $". The full snapshot ({fullBytes} bytes, same refs) is {saved}; search it with Grep or Read, " +
            $"or call {BrowserTools.SnapshotMention} with find."));

    /// <summary>
    /// 动作之后：只有几行原地变了就只给这几行，否则整页
    /// </summary>
    /// <param name="view">动作后的页面</param>
    /// <returns>工具结果</returns>
    public string AfterAction(BrowserPageView view) => _snapshots.AfterAction(ToView(view));

    /// <summary>
    /// 打开新地址之后：整页
    /// </summary>
    /// <param name="view">打开后的页面</param>
    /// <returns>工具结果</returns>
    public string AfterNavigation(BrowserPageView view) => _snapshots.Full(ToView(view));

    /// <summary>
    /// 用户接管完交回之后：整页（他做了什么只有页面知道）
    /// </summary>
    /// <param name="view">交回时的页面</param>
    /// <returns>工具结果</returns>
    public string AfterHandoff(BrowserPageView view) => _snapshots.Full(ToView(view));

    /// <summary>
    /// 主动重拍：带 find 只给命中的行；紧跟着上一份且一模一样就只说没变，否则整页
    /// </summary>
    /// <param name="view">当前页面</param>
    /// <param name="find">关键字，可空</param>
    /// <returns>工具结果</returns>
    public string Observe(BrowserPageView view, string? find) => _snapshots.Observe(ToView(view), find);

    /// <summary>
    /// 正文：带 find 只给命中的段落；超出预算时全文落盘、内联开头，并先指 find——
    /// 通用的「用 Read 接着读」会把模型引去整份读进来（实测一次多花六万 token）
    /// </summary>
    /// <param name="view">正文</param>
    /// <param name="find">关键字，可空</param>
    /// <returns>工具结果</returns>
    public string Text(BrowserTextView view, string? find)
    {
        // 正文一行就是一整段，本身就是上下文，不再捎带前后段
        string text = ReadableText.Tidy(view.Text);
        bool finding = find is { Length: > 0 } && text.Length > 0;
        string body = text.Length == 0
            ? "(no main text found on this page; use the outline view)"
            : finding ? AxSnapshotSearch.FindLines(text, find!, withContext: false) : text;
        string full = Compose(view.Notes, view.Title, view.Url, body);
        int fullBytes = Encoding.UTF8.GetByteCount(full);
        ToolOutputBudget budget = ToolOutputBudget.Snapshot;
        if (fullBytes <= budget.MaxBytes) return full;

        // 不给尾巴：正文的尾巴多是参考文献、分类、页脚
        string shown = AxResultFormatter.HeadLines(full, budget.HeadBytes);
        string saved = _snapshots.SaveOrSay(full, "BrowserText");
        int nextLine = shown.Split('\n').Length + 1;
        return finding
            ? $"{shown}\n\n---\n*[Many paragraphs mention \"{find}\": showing the first " +
              $"{Encoding.UTF8.GetByteCount(shown)} of {fullBytes} bytes. Try a more specific word; all matches are " +
              $"{saved} (Read from line {nextLine} to see the rest).]*"
            : $"{shown}\n\n---\n*[Long page: showing the first {Encoding.UTF8.GetByteCount(shown)} of {fullBytes} bytes. " +
              $"To find something, call {BrowserTools.SnapshotMention} with view \"text\" and find — it returns just the " +
              $"paragraphs that mention it. The full text is {saved} (Read from line {nextLine} to keep reading).]*";
    }

    /// <summary>
    /// 截图：存进草稿目录，结果只给路径与怎么看——图本身由 Read 的投影带上（ADR 0073），不进这条结果
    /// </summary>
    /// <param name="shot">截图</param>
    /// <param name="lookHint">怎么看（Read 它 / 交给 AnalyzeImage）</param>
    /// <returns>工具结果</returns>
    public string Screenshot(BrowserScreenshot shot, string lookHint)
    {
        Directory.CreateDirectory(spillDirectory);
        string path = Path.Combine(spillDirectory, ToolResultSpill.FileNameFor("BrowserScreenshot", shot.Jpeg, ".jpg"));
        File.WriteAllBytes(path, shot.Jpeg);
        return Compose(shot.Notes, shot.Title, shot.Url,
            $"Screenshot of the visible part of the page saved to {path}. {lookHint}");
    }

    private static AxView ToView(BrowserPageView view) =>
        new(view.Url, Header(view.Title, view.Url), view.Snapshot, view.CompactSnapshot, view.Notes);

    private static string Compose(IReadOnlyList<string> notes, string title, string url, string body) =>
        AxResultFormatter.Compose(notes, Header(title, url), body, "(the page has no readable content)");

    private static string Header(string title, string url) =>
        $"Page: {(title.Length > 0 ? title : "(untitled)")}{Environment.NewLine}URL: {url}";
}
