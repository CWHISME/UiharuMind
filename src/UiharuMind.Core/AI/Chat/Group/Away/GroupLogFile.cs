using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>
/// 一次追加进群流水文件的段：靠它给化身「新增发言在哪几行」的精确锚点
/// </summary>
/// <param name="FirstIndex">段内第一条发言的群历史下标（0-based，人读时 +1）</param>
/// <param name="LastIndex">段内最后一条发言的群历史下标</param>
/// <param name="StartLine">段头所在行（1-based，段头先写在这一行）</param>
/// <param name="EndLine">段尾（含）所在行</param>
public sealed record GroupLogAppend(int FirstIndex, int LastIndex, int StartLine, int EndLine);

/// <summary>
/// 离席期间给化身读的群流水 markdown 文件（第三方视角）：全群共用的草稿目录里一份，
/// 每波末增量追加。化身不再靠投递段拿到群发言正文，而是按锚点 <c>Read</c> 这一段——
/// 流水里「用户（化身）」与「用户」的标注是硬隔离，化身不会再把自己的回声当成用户真身的话。
///
/// <b>不读 <see cref="ChatSession.History"/> 本体</b>：群历史由 <see cref="GroupChatCoordinator"/>
/// 在 <c>_locker</c> 里追加，这里从调用方收快照（<c>HistorySnapshot</c>）遍历，避免与写入并发
/// 触发 List 版本检查或读到半截。文件自身用进程内 <see cref="Sync"/> 互斥追加。
/// </summary>
public static class GroupLogFile
{
    /// <summary>流水文件路径：与产物区同一间草稿目录（群壳那一间，成员与化身的 $DRAFT 都指它）</summary>
    /// <param name="group">群壳会话</param>
    /// <returns>绝对路径</returns>
    public static string PathOf(ChatSession group) =>
        Path.Combine(GroupArtifacts.DraftRoomOf(group), GroupLogText.FileName);

    /// <summary>
    /// 初始化流水文件：写下「标题 + 当前全量历史」。每次离席开始都重写——\n\n
    /// 不以「文件已存在就保留」为前提：跨离席时旧文件追到哪、与群历史差哪段都不好查，\n
    /// 重写一份连开头到开离席这一刻的全量快照永远正确，化身拿到的就是干净基线，之后的发言都是追加。\n\n
    /// 内容来自快照而非 <c>group.History</c> 本体（见类注释）。
    /// </summary>
    /// <param name="group">群壳会话（取标题与草稿目录）</param>
    /// <param name="history">当前群历史快照</param>
    /// <returns>文件当前行数（1-based 下一行起点）；写失败返回 0</returns>
    public static int Initialize(ChatSession group, IReadOnlyList<ChatMessage> history)
    {
        string path = PathOf(group);
        try
        {
            StringBuilder text = new();
            text.Append(GroupLogText.BuildHeading(group.Title));
            foreach (ChatMessage post in history) text.Append(GroupLogText.FormatPost(post));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text.ToString());
            return CountLines(text.ToString()); //内存数行数，不重读文件
        }
        catch (Exception e)
        {
            Log.Warning($"Group log file initialize '{path}' failed: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// 追加 <c>[fromIndex, history.Count)</c> 的新发言到流水文件，段头用「## 流水 @ #K-#M」。
    /// 进程内互斥，同一时间只许一个追加（波末由离席统一刷，化身跑着时群里新进的话下一波才见）。
    /// </summary>
    /// <param name="group">群壳会话（取草稿目录）</param>
    /// <param name="history">当前群历史快照；追加到其末尾</param>
    /// <param name="fromIndex">上次追到的群历史下标（含）</param>
    /// <param name="startLine">段头要落到的行号（1-based），由调用方维护</param>
    /// <returns>追加结果；没有新发言为 null（写失败也返回 null，日志有告警）</returns>
    public static GroupLogAppend? AppendNew(ChatSession group, IReadOnlyList<ChatMessage> history, int fromIndex,
        int startLine)
    {
        if (fromIndex >= history.Count) return null;
        string path = PathOf(group);

        StringBuilder text = new();
        int first = fromIndex;
        int last = history.Count - 1;
        text.Append(GroupLogText.BuildSegmentHeader(first, last));
        for (int i = fromIndex; i < history.Count; i++) text.Append(GroupLogText.FormatPost(history[i]));
        string segment = text.ToString();
        int lines = CountLines(segment);

        try
        {
            lock (Sync)
            {
                // 行号由调用方维护（离席会话里）。兜底也放锁内，别在锁外数行号跟追加竞争
                if (startLine < 1) startLine = LineCount(path) + 1;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, segment);
                return new GroupLogAppend(first, last, startLine, startLine + lines - 1);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Group log file append '{path}' failed: {e.Message}");
            return null;
        }
    }

    private static int LineCount(string path) => File.Exists(path) ? File.ReadLines(path).Count() : 0;

    private static int CountLines(string text) => text.Count(c => c == '\n');

    private static readonly object Sync = new();
}