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
    public const string NothingNew = "（群里这一阵没有新发言。）";

    /// <summary>离席回执在群流水里的正文（化身没留交代时）；界面另按回执画卡</summary>
    public const string AwayEndedText = "（离席结束）";

    /// <summary>化身上一句开的那一波没人接话（空推）之后，下一次投递末尾的提示</summary>
    public const string EmptyPushNote =
        $"（你上一句没人接。换个更具体的推法：点名到人、说清要他做什么；目标已经达成、或者该本人定的，就调用 {EndAwayTool.ToolName}。）";

    /// <summary>化身上一轮没给出下文之后的提示</summary>
    public const string SilentNote =
        $"（你上次没给出下一步。离席中只有两种结局：说一句能推进的话，或者调用 {EndAwayTool.ToolName}。）";

    /// <summary>化身上一轮出错之后的提示</summary>
    public const string FailedNote = "（你上一轮出错了，没说完。看看现在的情况，接着判断怎么推。）";

    /// <summary>
    /// 用户在离席中按了停止、过一阵再唤醒化身时的提示
    /// </summary>
    /// <param name="userName">用户的名字</param>
    /// <returns>提示</returns>
    public static string StoppedNote(string userName) => $"（{userName}刚才按了停止。看看现在的情况，再决定怎么推。）";

    /// <summary>
    /// 授权范围：用户开离席时只交代给化身的话，每一轮附在投递末尾（在没进展的提示之前），⛔ 不进群
    /// </summary>
    /// <param name="userName">用户的名字</param>
    /// <param name="mandate">用户写的授权范围</param>
    /// <returns>提示</returns>
    public static string MandateNote(string userName, string mandate) =>
        $"（{userName}离席前只交代给你的授权范围，成员看不到，别原样转述：{mandate.Trim()}）";

    /// <summary>
    /// 开离席时以用户名义发进群的那句：目标与备注原样带上标签
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
                    "直接写正文，不加前缀。想请某位成员接话，写 @名字。");
        text.Append($"\n- 尺度：一次两三句，像{scene.UserName}平时在群里说话那样；不写成报告。");
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
