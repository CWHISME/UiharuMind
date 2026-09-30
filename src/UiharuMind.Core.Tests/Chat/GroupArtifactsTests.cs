using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 本群产物区的收集：草稿目录直接列盘，工作区只认成员成功的 Write / Edit。
/// 盘是真的（临时目录），历史是手搭的
/// </summary>
public sealed class GroupArtifactsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-artifacts-" + Guid.NewGuid().ToString("N"));
    private readonly string _room;
    private readonly string _workspace;

    public GroupArtifactsTests()
    {
        _room = Directory.CreateDirectory(Path.Combine(_root, "room")).FullName;
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "ws")).FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void DraftRoom_ListsEverything_AndNamesWhoWroteIt()
    {
        string note = Touch(_room, "171-挂点核对.md");
        Touch(_room, "chart.png"); //命令行画的图：列出来，但认不出是谁
        Touch(_room, ".DS_Store");
        List<ChatMessage> uiharu = [];
        AddWrite(uiharu, "a", FileToolNames.Write, note, "Saved '171-挂点核对.md' (12 lines).");

        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.Collect(_room, _workspace, [("初春饰利", Written(uiharu))]);

        Assert.Equal(["171-挂点核对.md", "chart.png"], artifacts.Select(x => x.DisplayPath).Order());
        Assert.All(artifacts, x => Assert.Equal(EGroupArtifactSource.DraftRoom, x.Source));
        Assert.Equal(["初春饰利"], artifacts.Single(x => x.FullPath == note).Authors);
        Assert.Empty(artifacts.Single(x => x.DisplayPath == "chart.png").Authors);
    }

    /// <summary>工作区里只收真写进去的：编辑失败、被拒的不算；相对路径按工作区解析；已经删掉的不列</summary>
    [Fact]
    public void Workspace_OnlyCountsWritesThatLanded()
    {
        string spec = Touch(_workspace, "Design/spec.md");
        string code = Touch(_workspace, "Code/A.cpp");
        Touch(_workspace, "Code/B.cpp");
        List<ChatMessage> mitsuko = [];
        AddWrite(mitsuko, "1", FileToolNames.Edit, "Design/spec.md", "Applied 2 edit(s) to 'Design/spec.md'.\n-old\n+new");
        AddWrite(mitsuko, "2", FileToolNames.Edit, "Code/B.cpp", "[Edit failed] oldString not found");
        AddWrite(mitsuko, "3", FileToolNames.Write, "Code/C.cpp", "Tool call invocation rejected. denied");
        AddWrite(mitsuko, "4", FileToolNames.Write, "Code/Gone.cpp", "Saved 'Code/Gone.cpp' (3 lines).");
        List<ChatMessage> accelerator = [];
        AddWrite(accelerator, "5", FileToolNames.Edit, code, "Applied 1 edit(s) to 'A.cpp'.");
        AddWrite(mitsuko, "6", FileToolNames.Edit, code, "Applied 1 edit(s) to 'A.cpp'.");

        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.Collect(_room, _workspace,
            [("婚后光子", Written(mitsuko)), ("一方通行", Written(accelerator))]);

        Assert.Equal([Path.Combine("Code", "A.cpp"), Path.Combine("Design", "spec.md")],
            artifacts.Select(x => x.DisplayPath).Order());
        Assert.All(artifacts, x => Assert.Equal(EGroupArtifactSource.Workspace, x.Source));
        Assert.Equal(["婚后光子", "一方通行"], artifacts.Single(x => x.FullPath == code).Authors);
        Assert.Equal(["婚后光子"], artifacts.Single(x => x.FullPath == spec).Authors);
    }

    [Fact]
    public void RecentlyChanged_ComeFirst()
    {
        string older = Touch(_room, "old.md");
        string newer = Touch(_room, "new.md");
        File.SetLastWriteTime(older, DateTime.Now.AddHours(-1));

        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.Collect(_room, null, []);

        Assert.Equal([newer, older], artifacts.Select(x => x.FullPath));
    }

    [Fact]
    public void MissingDraftRoom_IsJustEmpty()
    {
        Assert.Empty(GroupArtifacts.Collect(Path.Combine(_root, "nope"), null, []));
    }

    private IReadOnlyList<string> Written(List<ChatMessage> history) => GroupArtifacts.WrittenPaths(history, new AgentPathResolver(_workspace));

    private static string Touch(string root, string relative)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private static void AddWrite(List<ChatMessage> history, string callId, string tool, string path, string result)
    {
        history.Add(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, tool, new Dictionary<string, object?> { ["filePath"] = path })]));
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, result)]));
    }
}
