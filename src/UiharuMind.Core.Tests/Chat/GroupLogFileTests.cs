using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群流水文件（ADR 0055 的第三方视角）：初始化写全量、增量段头与行号、发言人标注区分。
/// 化身靠这份文件按锚点自读，不再把群发言正文灌进上下文
/// </summary>
public class GroupLogFileTests : IDisposable
{
    private readonly string _sessionId = "log" + Guid.NewGuid().ToString("N")[..8];
    private readonly ChatSession _group;

    public GroupLogFileTests()
    {
        _group = new ChatSession
        {
            Title = "测试群",
            SessionId = _sessionId,
            WorkspacePath = Path.Combine(Path.GetTempPath(), "uiharu-grouplog-ws-" + Guid.NewGuid().ToString("N")),
            IsTransient = true,
        };
    }

    public void Dispose()
    {
        try
        {
            string room = Path.GetDirectoryName(GroupLogFile.PathOf(_group))!;
            if (Directory.Exists(room)) Directory.Delete(room, recursive: true);
        }
        catch
        {
            // 清理失败只是留垃圾，不影响断言
        }
    }

    private void Post(string text, bool asUser = false, bool asAvatar = false, string? memberName = null)
    {
        ChatMessage post = new(asUser || asAvatar ? ChatRole.User : ChatRole.Assistant, text)
        {
            AuthorName = asUser ? "CWH" : asAvatar ? "CWH" : memberName ?? "成员",
        };
        if (asAvatar) ChatMessageAnnotations.MarkGroupAvatarPost(post, "avatar1");
        _group.History.Add(post);
    }

    private static string ReadLog(ChatSession group) => File.ReadAllText(GroupLogFile.PathOf(group));

    [Fact]
    public void Initialize_WritesAllHistory_WithSpeakerLabels()
    {
        Post("用户原话", asUser: true);
        Post("成员发言", memberName: "神田空太");
        Post("化身说的话", asAvatar: true);
        Post("成员二", memberName: "椎名真白");

        int endLine = GroupLogFile.Initialize(_group, _group.History);

        string log = ReadLog(_group);
        Assert.Contains("## 流水", log);
        Assert.Contains("### 用户", log);
        Assert.Contains("### 神田空太", log);
        Assert.Contains("### 用户（化身）", log);
        Assert.Contains("### 椎名真白", log);
        Assert.True(endLine > 10);
        // 化身的话不冒充用户真身：正文同在，标注不同
        Assert.Contains("用户原话", log);
        Assert.Contains("化身说的话", log);
    }

    [Fact]
    public void Initialize_ReWritesCurrentFullHistory_EveryTime()
    {
        Post("第一句", asUser: true);
        GroupLogFile.Initialize(_group, _group.History);
        string once = ReadLog(_group);

        Post("第二句", asUser: true);
        GroupLogFile.Initialize(_group, _group.History); //每次都重写：当前全量快照
        string twice = ReadLog(_group);

        Assert.NotEqual(once, twice);
        Assert.Contains("第一句", twice);
        Assert.Contains("第二句", twice); //新历史也被快照进来
        Assert.DoesNotContain("## 流水 @", twice); //重写是干净基线，没有 append 段头残留
    }

    [Fact]
    public void AppendNew_AddsSegmentHeaderWithLineRange()
    {
        Post("第一句", asUser: true);
        int initial = GroupLogFile.Initialize(_group, _group.History);

        Post("第二句", asUser: true);
        Post("第三句", asAvatar: true);
        GroupLogAppend append = GroupLogFile.AppendNew(_group, _group.History, 1, initial + 1)!;

        Assert.Equal(1, append.FirstIndex); //第二句
        Assert.Equal(2, append.LastIndex);  //第三句
        Assert.True(append.StartLine > initial);
        Assert.True(append.EndLine >= append.StartLine);

        string log = ReadLog(_group);
        Assert.Contains($"## 流水 @ #2-3", log);
        Assert.Contains("第二句", log);
        Assert.Contains("第三句", log);
    }

    [Fact]
    public void AppendNew_NoNewPosts_ReturnsNull()
    {
        Post("第一句", asUser: true);
        int initial = GroupLogFile.Initialize(_group, _group.History);

        GroupLogAppend? none = GroupLogFile.AppendNew(_group, _group.History, 1, initial + 1);
        Assert.Null(none);
    }

    [Fact]
    public void AppendNew_ConsecutiveSegments_LinesCarryOn()
    {
        Post("第一句", asUser: true);
        int end = GroupLogFile.Initialize(_group, _group.History);

        Post("第二句", memberName: "茅场晶彦");
        GroupLogAppend first = GroupLogFile.AppendNew(_group, _group.History, 1, end + 1)!;
        Assert.Equal(1, first.FirstIndex);

        Post("第三句", asUser: true);
        GroupLogAppend second = GroupLogFile.AppendNew(_group, _group.History, 2, first.EndLine + 1)!;
        Assert.Equal(2, second.FirstIndex);
        Assert.True(second.StartLine > first.EndLine); //第二段从第一段之后接起
        Assert.Equal(second.StartLine, first.EndLine + 1);

        string log = ReadLog(_group);
        Assert.Contains("## 流水 @ #2-2", log);
        Assert.Contains("## 流水 @ #3-3", log);
    }

    [Fact]
    public void SpeakerLabel_DistinguishesAvatarFromUser() =>
        Assert.Equal("用户（化身）", GroupLogText.SpeakerLabel(AvatarPost("x")));

    private static ChatMessage AvatarPost(string text)
    {
        ChatMessage post = new(ChatRole.User, text);
        ChatMessageAnnotations.MarkGroupAvatarPost(post, "avatar1");
        return post;
    }
}