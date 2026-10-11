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

namespace UiharuMind.Core.AI.Execution.Tools.Accessibility;

/// <summary>
/// 一份快照在某一刻的样子（网页或桌面窗口），交给 <see cref="AxResultFormatter"/> 写成工具结果
/// </summary>
/// <param name="Key">是不是「同一个东西」：网页按地址、窗口按窗口编号；变了就不比对，整份重给</param>
/// <param name="Header">抬头（网页的标题与地址、窗口的应用与标题）</param>
/// <param name="Snapshot">完整快照</param>
/// <param name="CompactSnapshot">精简版，编号与完整版一致</param>
/// <param name="Notes">这一步顺带发生的事</param>
public sealed record AxView(string Key, string Header, string Snapshot, string CompactSnapshot, IReadOnlyList<string> Notes);

/// <summary>
/// 结果里随来源变的那几句
/// </summary>
/// <param name="Source">来源的叫法（browser、desktop），写进「与上一份相比」的说明</param>
/// <param name="SpillStem">超长快照落盘的文件名前缀</param>
/// <param name="FindStem">按关键字挑出的行超长时落盘的文件名前缀</param>
/// <param name="Empty">快照为空时的说法</param>
/// <param name="LongNote">超长时的说明：(完整字节数, 是否还截了尾, 存到了哪) → 说明</param>
internal sealed record AxResultWording(string Source, string SpillStem, string FindStem, string Empty,
    Func<int, bool, string, string> LongNote);

/// <summary>
/// 把快照写成交给模型的工具结果，并记住上一份（一个会话一个）：
/// 动作之后只有几行原地变了就只给这几行；紧跟着的重拍没变就只说没变；
/// 超出预算时完整版落盘、内联精简版。快照每一步都回、都进历史，这里省下的每一份都要重发很多轮。
/// </summary>
/// <param name="spillDirectory">超长快照落盘的目录</param>
/// <param name="wording">随来源变的说法</param>
internal sealed class AxResultFormatter(string spillDirectory, AxResultWording wording)
{
    private static readonly TimeSpan ChangedLinesWindow = TimeSpan.FromMinutes(2); //隔久了上一份可能已被压缩折叠，整份重给
    private static readonly TimeSpan UnchangedWindow = TimeSpan.FromSeconds(10); //同一批并行调用里的重拍

    private readonly Lock _gate = new();
    private string _lastKey = string.Empty;
    private string _lastSnapshot = string.Empty;
    private DateTime _lastAt = DateTime.MinValue;

    /// <summary>落盘目录</summary>
    public string SpillDirectory => spillDirectory;

    /// <summary>
    /// 动作之后：只有几行原地变了就只给这几行，否则整份
    /// </summary>
    /// <param name="view">动作后的样子</param>
    /// <returns>工具结果</returns>
    public string AfterAction(AxView view) => Changes(view, ChangedLinesWindow, identicalOnly: false) ?? Full(view);

    /// <summary>
    /// 主动重拍：带 find 只给命中的行；紧跟着上一份且一模一样就只说没变，否则整份
    /// </summary>
    /// <param name="view">当前的样子</param>
    /// <param name="find">关键字，可空</param>
    /// <returns>工具结果</returns>
    public string Observe(AxView view, string? find)
    {
        if (find is not { Length: > 0 }) return Changes(view, UnchangedWindow, identicalOnly: true) ?? Full(view);

        string found = Compose(view, AxSnapshotSearch.FindLines(view.Snapshot, find));
        return ToolResultSpill.Limit(found, spillDirectory, wording.FindStem, ToolOutputBudget.Snapshot);
    }

    /// <summary>
    /// 整份：超出预算时完整版落盘、内联精简版，还超就只留开头
    /// </summary>
    /// <param name="view">当前的样子</param>
    /// <returns>工具结果</returns>
    public string Full(AxView view)
    {
        lock (_gate)
        {
            Remember(view);
        }

        ToolOutputBudget budget = ToolOutputBudget.Snapshot;
        string full = Compose(view, view.Snapshot);
        int fullBytes = Encoding.UTF8.GetByteCount(full);
        if (fullBytes <= budget.MaxBytes) return full;

        // 不给头尾骨架：长页面的开头本是导航，正文才是要看的
        string compact = Compose(view, view.CompactSnapshot);
        bool cut = Encoding.UTF8.GetByteCount(compact) > budget.HeadBytes;
        string shown = cut ? HeadLines(compact, budget.HeadBytes) : compact;
        string saved = SaveOrSay(full, wording.SpillStem);
        return $"{shown}\n\n---\n*[{wording.LongNote(fullBytes, cut, saved)}]*";
    }

    /// <summary>
    /// 落盘一段长文字；失败也不丢这次结果，只是说存不下
    /// </summary>
    /// <param name="text">文字</param>
    /// <param name="fileStem">文件名前缀</param>
    /// <returns>「saved to 路径」或「could not be saved」</returns>
    public string SaveOrSay(string text, string fileStem)
    {
        try
        {
            return "saved to " + ToolResultSpill.Save(text, spillDirectory, fileStem);
        }
        catch (Exception e)
        {
            Log.Warning($"[{wording.Source}] spill failed ({fileStem}): {e.Message}");
            return "could not be saved";
        }
    }

    /// <summary>
    /// 旁注、抬头、正文拼成一条结果
    /// </summary>
    /// <param name="notes">旁注</param>
    /// <param name="header">抬头</param>
    /// <param name="body">正文</param>
    /// <param name="empty">正文为空时的说法</param>
    /// <returns>结果</returns>
    public static string Compose(IReadOnlyList<string> notes, string header, string body, string empty)
    {
        StringBuilder text = new();
        foreach (string note in notes) text.AppendLine(note);
        text.AppendLine(header);
        text.AppendLine();
        text.Append(body.Length > 0 ? body : empty);
        return text.ToString();
    }

    /// <summary>
    /// 按整行取开头，不劈开一行；第一行就装不下（一整段超长正文）时退化为按字节截
    /// </summary>
    /// <param name="text">文字</param>
    /// <param name="budgetBytes">字节预算</param>
    /// <returns>开头</returns>
    public static string HeadLines(string text, int budgetBytes)
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

    private string? Changes(AxView view, TimeSpan window, bool identicalOnly)
    {
        lock (_gate)
        {
            bool recent = view.Key == _lastKey && DateTime.UtcNow - _lastAt <= window;
            string? changed = recent ? AxSnapshotDiff.ChangedLines(_lastSnapshot, view.Snapshot) : null;
            if (changed == null || (identicalOnly && changed.Length > 0)) return null;

            Remember(view);
            return changed.Length == 0
                ? Compose(view, $"(unchanged since the previous {wording.Source} result; its refs still apply)")
                : Compose(view, $"Changed lines (the rest is as in the previous {wording.Source} result, refs unchanged):\n" +
                                changed);
        }
    }

    private void Remember(AxView view)
    {
        _lastKey = view.Key;
        _lastSnapshot = view.Snapshot;
        _lastAt = DateTime.UtcNow;
    }

    private string Compose(AxView view, string body) =>
        Compose(view.Notes, view.Header, body, wording.Empty);
}
