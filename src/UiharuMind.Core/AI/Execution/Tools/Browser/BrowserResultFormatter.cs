/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 把页面写成交给模型的工具结果，并记住上一份（一个会话一个）：
/// 动作之后只有几行原地变了就只给这几行；紧跟着的重拍页面没变就只说没变；
/// 页面超出预算时完整版落盘、内联精简版。快照每一步都回、都进历史，这里省下的每一份都要重发很多轮。
/// </summary>
/// <param name="spillDirectory">超长快照落盘的目录</param>
internal sealed class BrowserResultFormatter(string spillDirectory)
{
    private static readonly TimeSpan ChangedLinesWindow = TimeSpan.FromMinutes(2); //隔久了上一份可能已被压缩折叠，整页重给
    private static readonly TimeSpan UnchangedWindow = TimeSpan.FromSeconds(10); //同一批并行调用里的重拍

    private readonly Lock _gate = new();
    private string _lastUrl = string.Empty;
    private string _lastSnapshot = string.Empty;
    private DateTime _lastAt = DateTime.MinValue;

    /// <summary>
    /// 动作之后：只有几行原地变了就只给这几行，否则整页
    /// </summary>
    /// <param name="view">动作后的页面</param>
    /// <returns>工具结果</returns>
    public string AfterAction(BrowserPageView view) => Changes(view, ChangedLinesWindow, identicalOnly: false) ?? Full(view);

    /// <summary>
    /// 打开新地址之后：整页
    /// </summary>
    /// <param name="view">打开后的页面</param>
    /// <returns>工具结果</returns>
    public string AfterNavigation(BrowserPageView view) => Full(view);

    /// <summary>
    /// 用户接管完交回之后：整页（他做了什么只有页面知道）
    /// </summary>
    /// <param name="view">交回时的页面</param>
    /// <returns>工具结果</returns>
    public string AfterHandoff(BrowserPageView view) => Full(view);

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
        string shown = HeadLines(full, budget.HeadBytes);
        string saved = SaveOrSay(full, "BrowserText");
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

    /// <summary>
    /// 主动重拍：带 find 只给命中的行；紧跟着上一份且一模一样就只说没变，否则整页
    /// </summary>
    /// <param name="view">当前页面</param>
    /// <param name="find">关键字，可空</param>
    /// <returns>工具结果</returns>
    public string Observe(BrowserPageView view, string? find)
    {
        if (find is not { Length: > 0 }) return Changes(view, UnchangedWindow, identicalOnly: true) ?? Full(view);

        string found = Compose(view, AxSnapshotSearch.FindLines(view.Snapshot, find));
        return ToolResultSpill.Limit(found, spillDirectory, "BrowserFind", ToolOutputBudget.Snapshot);
    }

    private string? Changes(BrowserPageView view, TimeSpan window, bool identicalOnly)
    {
        lock (_gate)
        {
            bool recent = view.Url == _lastUrl && DateTime.UtcNow - _lastAt <= window;
            string? changed = recent ? AxSnapshotDiff.ChangedLines(_lastSnapshot, view.Snapshot) : null;
            if (changed == null || (identicalOnly && changed.Length > 0)) return null;

            Remember(view);
            return changed.Length == 0
                ? Compose(view, "(unchanged since the previous browser result; its refs still apply)")
                : Compose(view, "Changed lines (the rest is as in the previous browser result, refs unchanged):\n" + changed);
        }
    }

    private string Full(BrowserPageView view)
    {
        lock (_gate)
        {
            Remember(view);
        }

        ToolOutputBudget budget = ToolOutputBudget.Snapshot;
        string full = Compose(view, view.Snapshot);
        int fullBytes = Encoding.UTF8.GetByteCount(full);
        if (fullBytes <= budget.MaxBytes) return full;

        // 页面超出预算：完整版落盘；内联给精简版（导航、侧栏、页脚收成一行，编号不变），还超就只留开头。
        // 不给头尾骨架：长页面的开头本是导航，正文才是要看的
        string compact = Compose(view, view.CompactSnapshot);
        bool cut = Encoding.UTF8.GetByteCount(compact) > budget.HeadBytes;
        string shown = cut ? HeadLines(compact, budget.HeadBytes) : compact;
        string saved = SaveOrSay(full, "BrowserSnapshot");

        return $"{shown}\n\n---\n*[Long page: navigation, sidebars and footers are collapsed" +
               (cut ? " and the rest of the page is cut" : string.Empty) +
               $". The full snapshot ({fullBytes} bytes, same refs) is {saved}; search it with Grep or Read, " +
               $"or call {BrowserTools.SnapshotMention} with find.]*";
    }

    // 落盘失败也不丢这次结果，只是说存不下
    private string SaveOrSay(string text, string fileStem)
    {
        try
        {
            return "saved to " + ToolResultSpill.Save(text, spillDirectory, fileStem);
        }
        catch (Exception e)
        {
            Log.Warning($"[Browser] spill failed ({fileStem}): {e.Message}");
            return "could not be saved";
        }
    }

    private void Remember(BrowserPageView view)
    {
        _lastUrl = view.Url;
        _lastSnapshot = view.Snapshot;
        _lastAt = DateTime.UtcNow;
    }

    private static string Compose(BrowserPageView view, string body) => Compose(view.Notes, view.Title, view.Url, body);

    private static string Compose(IReadOnlyList<string> notes, string title, string url, string body)
    {
        StringBuilder text = new();
        foreach (string note in notes) text.AppendLine(note);
        text.Append("Page: ").AppendLine(title.Length > 0 ? title : "(untitled)");
        text.Append("URL: ").AppendLine(url);
        text.AppendLine();
        text.Append(body.Length > 0 ? body : "(the page has no readable content)");
        return text.ToString();
    }

    // 按整行取开头，不劈开一行；第一行就装不下（一整段超长正文）时退化为按字节截
    private static string HeadLines(string text, int budgetBytes)
    {
        StringBuilder head = new();
        int bytes = 0;
        foreach (string line in text.Split('\n'))
        {
            int size = Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes + size > budgetBytes) break;
            head.Append(line).Append('\n');
            bytes += size;
        }

        return head.Length > 0 ? head.ToString().TrimEnd() : ToolOutputTruncation.TakeHeadBytes(text, budgetBytes);
    }
}
