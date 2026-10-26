/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 把后台任务的结局写成送回会话的那条消息
/// </summary>
public static class BackgroundTaskReport
{
    private const int TailLines = 60;
    private const int TailChars = 6000;

    /// <summary>
    /// 组装结果消息：user 角色、带 <see cref="ChatMessageAnnotations.BackgroundTaskReport"/> 标记
    /// </summary>
    /// <param name="outcome">结局</param>
    /// <returns>消息</returns>
    public static ChatMessage BuildMessage(BackgroundTaskOutcome outcome) =>
        new(ChatRole.User, BuildText(outcome))
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.BackgroundTaskReport] = outcome.Task.Id,
            },
        };

    /// <summary>
    /// 结果正文（含输出末尾）
    /// </summary>
    /// <param name="outcome">结局</param>
    /// <returns>正文</returns>
    public static string BuildText(BackgroundTaskOutcome outcome) => BuildText(outcome, ReadTail(outcome.Task.LogPath));

    // inline 任务没拼出正文时，按结束方式交代一句（正常完成不会走到这里）
    private static string HowInlineEnded(BackgroundTaskOutcome outcome) => outcome.End switch
    {
        EBackgroundTaskEnd.TimeLimit => "跑满时间上限被结束，还没等到",
        EBackgroundTaskEnd.Stopped => "被用户叫停，没有等到",
        EBackgroundTaskEnd.AppExit => "在应用退出时被中止",
        _ => "已结束",
    };

    /// <summary>
    /// 把一段<b>模型输出</b>包成能直接进报告正文的样子：过长截断，整段进围栏。
    ///
    /// 报告是 user 角色（ADR 0062），另一个会话或群成员说的话原样铺开，既会被当成用户自己说的，
    /// 也可能自带 ``` 把卡片的 markdown 提前截断——这两件事统一在这里做，任务体只管拼标题
    /// </summary>
    /// <param name="text">模型输出原文</param>
    /// <returns>可进报告正文的文本</returns>
    internal static string QuoteForReport(string text)
    {
        string body = text.Length > TailChars ? $"……（前文过长，已截断）\n{text[^TailChars..]}" : text;
        string fence = FenceFor(body);
        return $"{fence}\n{body}\n{fence}";
    }

    /// <summary>
    /// 结果正文。打头那句交代来源（哪个任务、怎么结束的），输出末尾整段在围栏里——
    /// 这条是 user 角色、输出里什么都可能有，靠的是来源说清楚，不另加「不是用户的话」（ADR 0062）
    /// </summary>
    internal static string BuildText(BackgroundTaskOutcome outcome, string tail)
    {
        if (outcome.Task is InlineBackgroundTask inline)
        {
            // inline 任务（SendMessage 的等待任务）：正文由任务体拼好；没拼出来时按结束方式兜底
            if (inline.ResultText is { Length: > 0 } result) return result;
            return $"等待任务（{inline.Description}）{HowInlineEnded(outcome)}，没有产出结果。";
        }

        // 认不出的实现（第三个形态）按最小面交代，别在这里抛 InvalidCastException
        if (outcome.Task is not BackgroundTask task)
        {
            return $"后台任务（{outcome.Task.Description}）已结束，没有可读的结果。";
        }

        string how = outcome.End switch
        {
            EBackgroundTaskEnd.Exited => $"已结束，退出码 {outcome.ExitCode?.ToString() ?? "未知"}",
            EBackgroundTaskEnd.TimeLimit => $"跑满时间上限 {FormatDuration(task.MaxRuntime)} 被结束（连同它起的进程）",
            EBackgroundTaskEnd.Stopped => "被用户叫停，没有跑完",
            _ => "在应用退出时被中止，没有跑完",
        };

        StringBuilder text = new();
        text.Append($"你先前启动的后台任务 `{task.Id}`（{task.Description}）{how}，用时 {FormatDuration(outcome.Duration)}。");
        text.Append($"完整输出在 `{task.LogPath}`。\n\n");
        if (tail.Length == 0)
        {
            text.Append("它没有任何输出。");
        }
        else
        {
            string fence = FenceFor(tail);
            text.Append($"输出末尾：\n{fence}\n{tail}\n{fence}");
        }

        return text.ToString();
    }

    // 围栏比输出里最长的一串反引号还长一位:输出自带 ``` 时不至于把卡片的 markdown 提前截断
    private static string FenceFor(string content)
    {
        int longest = 0;
        int run = 0;
        foreach (char c in content)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return new string('`', Math.Max(3, longest + 1));
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1) return $"{Math.Max(0, (int)duration.TotalSeconds)} 秒";
        if (duration.TotalHours < 1) return $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒";
        return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分";
    }

    // 只取末尾:长任务的日志动辄上万行,结论几乎总在最后
    private static string ReadTail(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty; //inline 任务没有日志文件
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(Math.Max(0, stream.Length - TailChars * 4), SeekOrigin.Begin);
            using StreamReader reader = new(stream, Encoding.UTF8);
            string[] lines = reader.ReadToEnd().TrimEnd().Split('\n');
            string tail = string.Join('\n', lines.TakeLast(TailLines));
            return tail.Length > TailChars ? tail[^TailChars..] : tail;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
