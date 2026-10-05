using System.Text;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 化身那一侧的文本（ADR 0055）：场景段、离席目标的标签。纯函数；化身的规矩本身在它的卡上（<c>GroupAvatarAgent.md</c>），
/// 这里只放随群变的东西
/// </summary>
public static class GroupAvatarTranscript
{
    /// <summary>离席目标那句群发言的开头：成员与化身都据此认出它</summary>
    public const string AwayGoalTag = "[离席目标]";

    /// <summary>化身这一轮没有新话可接时交给它的一句（被停过、或这一波谁也没说话）</summary>
    public const string NothingNew = "【群里这一阵没有新发言。】";

    /// <summary>离席回执在群流水里的正文（化身没留交代时）；界面另按回执画卡</summary>
    public const string AwayEndedText = "（离席结束）";

    /// <summary>化身上一句开的那一波没人接话（空推）之后，下一次投递末尾的提示</summary>
    public const string EmptyPushNote =
        $"【你上一句没人接。换个更具体的推法：点名到人、说清要他做什么；目标已经达成、或者该本人定的，就调用 {EndAwayTool.ToolName}。】";

    /// <summary>空推之后、无限模式的提示：目标达成也别调结束，直接找新方向</summary>
    public const string EmptyPushNoteInfinite =
        $"【你上一句没人接。换个更具体的推法：点名到人、说清要他做什么；目标已经达成就直接找个新方向接着推。】";

    /// <summary>化身上一轮没给出下文之后的提示</summary>
    public const string SilentNote =
        $"【你上次没给出下一步。离席中只有两种结局：说一句能推进的话，或者调用 {EndAwayTool.ToolName}。】";

    /// <summary>化身上一轮没给出下文之后、无限模式的提示：调结束只会报错，直接指无限模式一节</summary>
    public const string SilentNoteInfinite =
        $"【你上次没给出下一步。无限模式下没有“结束离席”这个选项：" +
        "说一句能推进的话；实在没可推的，按系统提示里的无限模式一节找个新方向。】";

    /// <summary>化身上一轮出错之后的提示</summary>
    public const string FailedNote = "【你上一轮出错了，没说完。看看现在的情况，接着判断怎么推。】";

    /// <summary>停止提示的内层固定串（用户名在它前面）：模板与剥离识别共用这一份，改文案只改这里</summary>
    internal const string StoppedMarker = "刚才按了停止。看看现在的情况，再决定怎么推。";

    /// <summary>
    /// 用户在离席中按了停止、过一阵再唤醒化身时的提示
    /// </summary>
    /// <param name="userName">用户的名字</param>
    /// <returns>提示</returns>
    public static string StoppedNote(string userName) => $"【{userName}{StoppedMarker}】";

    /// <summary>捎话的内层固定串：目标是用户原话、可能换行，剥离识别只凭它认；模板与识别共用这一份</summary>
    internal const string KickoffMarker = "开启本次离席时捎给你的话，成员看不到，别原样转述：";

    /// <summary>
    /// 开离席时捎给化身的第一句话，只随化身第一轮的投递交代（之后靠压缩的回查段留着），⛔ 不进群
    /// </summary>
    /// <param name="userName">用户的名字</param>
    /// <param name="goal">用户写的捎话</param>
    /// <returns>提示</returns>
    public static string KickoffNote(string userName, string goal) =>
        $"【{userName}{KickoffMarker}{goal.Trim()}】";

    /// <summary>重要提醒的内层固定串：模板与剥离识别共用这一份，改文案只改这里</summary>
    internal const string ReminderMarker = "离席前留的重要提醒，成员看不到，别原样转述：";

    /// <summary>
    /// 开离席时只交代给化身的重要提醒，每一轮附在投递末尾（在没进展的提示之前），⛔ 不进群
    /// </summary>
    /// <param name="userName">用户的名字</param>
    /// <param name="reminder">用户写的重要提醒</param>
    /// <returns>提示</returns>
    public static string ReminderNote(string userName, string reminder) =>
        $"【{userName}{ReminderMarker}{reminder.Trim()}】";

    /// <summary>
    /// 无限模式的标记：具体规矩在化身卡的无限模式一节（系统提示头部、跨轮稳定），
    /// 这里只指明本轮是无限模式——卡片本身不知道当前离席是不是无限模式
    /// </summary>
    public const string InfiniteNote = "【本次是无限模式：按系统提示里的无限模式一节办。】";

    /// <summary>
    /// 一次离席私下交代给化身的话：捎话只在首轮带，提醒与无限模式每一轮都带
    /// （化身历史跨离席保留，提醒只靠第一轮那一次会分不清哪份作数；捎话是用户原话，压缩的回查段会原样留着）
    /// </summary>
    /// <param name="userName">用户的名字</param>
    /// <param name="goal">捎话；首轮才带，没填则没有这份</param>
    /// <param name="reminder">重要提醒；没有为 null</param>
    /// <param name="infinite">是不是无限模式</param>
    /// <param name="kickoff">这一轮带不带捎话</param>
    /// <returns>提示；三样都没有为 null</returns>
    public static string? BriefingNote(string userName, string goal, string? reminder, bool infinite,
        bool kickoff = true)
    {
        List<string> parts = [];
        if (kickoff && !string.IsNullOrWhiteSpace(goal)) parts.Add(KickoffNote(userName, goal));
        if (!string.IsNullOrWhiteSpace(reminder)) parts.Add(ReminderNote(userName, reminder));
        if (infinite) parts.Add(InfiniteNote);
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    /// <summary>
    /// 从一份投递输入的末尾剥私下交代：化身那份输入 = 群发言 + 捎话/提醒/无限模式 + 当轮提示，
    /// 末尾这几段是说给化身的，界面另画成旁白，不能并进最后一位发言人的气泡。
    /// 从末尾往前逐段认、遇到第一段群发言即停；用户原话（捎话目标、提醒）里纵有换行也只影响它自己那一段。
    /// </summary>
    /// <param name="text">投递输入全文</param>
    /// <returns>群发言部分与剥出来的交代（原顺序）；没有为后者为空</returns>
    public static (string Speech, IReadOnlyList<string> Notes) SplitSystemNotes(string text)
    {
        List<string> notes = [];
        string rest = text;
        while (TryStripOneTrailingNote(ref rest, out string? note)) notes.Insert(0, note);
        return (rest, notes);
    }

    private static bool TryStripOneTrailingNote(ref string rest, out string? note)
    {
        // 定文的几段：全等或 "\n\n" 接在末尾
        foreach (string fixedNote in FixedNotes())
        {
            if (rest.Equals(fixedNote, StringComparison.Ordinal))
            {
                note = fixedNote;
                rest = string.Empty;
                return true;
            }

            string withSeparator = "\n\n" + fixedNote;
            if (rest.EndsWith(withSeparator, StringComparison.Ordinal))
            {
                note = fixedNote;
                rest = rest[..^withSeparator.Length];
                return true;
            }
        }

        // 带用户原话的三段：用户名与目标/提醒可变，只凭内层固定串认（用户原话里有换行也不怕，它是末尾那一段）
        if (TryStripVariableNote(ref rest, StoppedMarker, tailFixed: true, out note) ||
            TryStripVariableNote(ref rest, ReminderMarker, tailFixed: false, out note) ||
            TryStripVariableNote(ref rest, KickoffMarker, tailFixed: false, out note)) return true;

        note = null;
        return false;
    }

    private static IEnumerable<string> FixedNotes()
    {
        // 已落盘的旧化身历史里还是（）写法：一并认，老消息重开也剥得掉
        foreach (string note in new[]
                 {
                     EmptyPushNote, EmptyPushNoteInfinite, SilentNote, SilentNoteInfinite, FailedNote, InfiniteNote,
                     GroupTranscript.PrivateResumeNote,
                 })
        {
            yield return note;
            yield return note.Replace('【', '（').Replace('】', '）');
        }
    }

    private static bool TryStripVariableNote(ref string rest, string marker, bool tailFixed, out string? note)
    {
        note = null;
        if (!rest.EndsWith("】", StringComparison.Ordinal) && !rest.EndsWith("）", StringComparison.Ordinal))
            return false;

        // 候选开头从后往前试：用户原话（目标、提醒）里也可能有换行与括号，含标记的那个才算；旧数据是（）写法
        List<int> starts = [];
        CollectStarts(rest, "\n\n【", starts);
        CollectStarts(rest, "\n\n（", starts);
        starts.Sort();

        for (int i = starts.Count - 1; i >= 0; i--)
        {
            string candidate = rest[(starts[i] + 2)..];
            if (IsVariableCandidate(candidate, marker, tailFixed))
            {
                note = candidate;
                rest = rest[..starts[i]];
                return true;
            }
        }

        // 全文就是一段交代
        if ((rest.StartsWith("【", StringComparison.Ordinal) || rest.StartsWith("（", StringComparison.Ordinal)) &&
            IsVariableCandidate(rest, marker, tailFixed))
        {
            note = rest;
            rest = string.Empty;
            return true;
        }

        return false;
    }

    private static void CollectStarts(string text, string separator, List<int> starts)
    {
        int at = 0;
        while (true)
        {
            int found = text.IndexOf(separator, at, StringComparison.Ordinal);
            if (found < 0) return;
            starts.Add(found);
            at = found + 1;
        }
    }

    private static bool IsVariableCandidate(string candidate, string marker, bool tailFixed)
    {
        // 开合必须配对：【】或（）
        char closer = candidate[0] switch
        {
            '【' => '】',
            '（' => '）',
            _ => '\0',
        };
        if (closer == '\0' || candidate[^1] != closer ||
            !candidate.Contains(marker, StringComparison.Ordinal)) return false;

        // 停止提示没有可变尾巴：结尾必须是 标记 + 配对的关括号，否则只是群发言里偶然提到这句话
        return !tailFixed || candidate.EndsWith(marker + closer, StringComparison.Ordinal);
    }

    /// <summary>
    /// 开离席时以用户名义发进群的那句：目标与备注原样带上标签。
    /// 目标私聊化之后不再使用（保留供老群流水里的标签识别）
    /// </summary>
    /// <param name="goal">用户写的目标（可含备注）</param>
    /// <returns>群发言正文</returns>
    public static string GoalPost(string goal) => $"{AwayGoalTag} {goal.Trim()}";

    /// <summary>
    /// 化身的群场景段：群名、在场成员、主持人与发言格式。成员那份的「只说新的」「拍板归用户」不给它——
    /// 它就是替用户拍板的那一位
    /// </summary>
    /// <param name="scene">场景要素</param>
    /// <returns>场景段正文</returns>
    public static string BuildScene(GroupAvatarScene scene)
    {
        StringBuilder text = new();
        string members = GroupTranscript.JoinMembers(scene.Members);
        text.Append($"你在群聊「{scene.GroupName}」里，替{scene.UserName}坐着。");
        if (members.Length > 0) text.Append($"在场的成员有：{members}。");
        if (scene.HostName != null) text.Append($"本群主持人是{scene.HostName}。");

        text.Append($"\n\n- 格式：群里的发言按「[名字]: 内容」交给你；你说完的正文会以{scene.UserName}的名义发到群里，" +
                    "直接写正文，不加前缀。想请某位成员接话，写 @名字，只写名字、不带作品名。");
        text.Append($"\n- 尺度：像平时在群里说话那样；不写成报告。");
        text.Append("\n- 一轮怎么算：调用工具时顺手写的话只留在你这里，群里看不到；要对大家说的，等工具用完再说。");
        if (scene.SharesDraftRoom)
            text.Append("\n- 草稿目录是全群共用的：文件名起得具体些，新建前先看有没有同名的，别盖掉别人的。");

        return text.ToString();
    }
}

/// <summary>化身场景段的要素</summary>
/// <param name="GroupName">群名</param>
/// <param name="UserName">用户的名字（化身替的是他）</param>
/// <param name="Members">在场成员（名字 + 作品）</param>
/// <param name="HostName">主持人的名字；没有为 null</param>
/// <param name="SharesDraftRoom">化身有没有草稿目录（开着文件或命令行）</param>
public sealed record GroupAvatarScene(string GroupName, string UserName, IReadOnlyList<GroupMemberPresence> Members,
    string? HostName, bool SharesDraftRoom);
