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
        string saved;
        try
        {
            saved = "saved to " + ToolResultSpill.Save(full, spillDirectory, "BrowserSnapshot");
        }
        catch (Exception e)
        {
            Log.Warning($"[Browser] snapshot spill failed: {e.Message}");
            saved = "could not be saved";
        }

        return $"{shown}\n\n---\n*[Long page: navigation, sidebars and footers are collapsed" +
               (cut ? " and the rest of the page is cut" : string.Empty) +
               $". The full snapshot ({fullBytes} bytes, same refs) is {saved}; search it with Grep or Read, " +
               $"or call {BrowserTools.SnapshotToolName} with find.]*";
    }

    private void Remember(BrowserPageView view)
    {
        _lastUrl = view.Url;
        _lastSnapshot = view.Snapshot;
        _lastAt = DateTime.UtcNow;
    }

    private static string Compose(BrowserPageView view, string body)
    {
        StringBuilder text = new();
        foreach (string note in view.Notes) text.AppendLine(note);
        text.Append("Page: ").AppendLine(view.Title.Length > 0 ? view.Title : "(untitled)");
        text.Append("URL: ").AppendLine(view.Url);
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
