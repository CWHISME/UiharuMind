using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

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
        AddWrite(uiharu, "a", FileToolNames.Write, note, "已保存 '171-挂点核对.md'（12 行）。");

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
        // Touch 的相对路径用 Path.Combine 拼:直接写 "Code/A.cpp" 在 Windows 上
        // Path.Combine 会保留混合分隔符,而 Resolve 的 GetFullPath 会收成 "\",两个绝对路径串就比不上了
        string spec = Touch(_workspace, Path.Combine("Design", "spec.md"));
        string code = Touch(_workspace, Path.Combine("Code", "A.cpp"));
        Touch(_workspace, "Code/B.cpp");
        List<ChatMessage> mitsuko = [];
        AddWrite(mitsuko, "1", FileToolNames.Edit, "Design/spec.md", "已修改 'Design/spec.md'（2 处编辑）。\n-old\n+new");
        AddWrite(mitsuko, "2", FileToolNames.Edit, "Code/B.cpp", "[编辑失败] oldString 没有找到");
        AddWrite(mitsuko, "3", FileToolNames.Write, "Code/C.cpp", "Tool call invocation rejected. denied");
        AddWrite(mitsuko, "4", FileToolNames.Write, "Code/Gone.cpp", "已保存 'Code/Gone.cpp'（3 行）。");
        List<ChatMessage> accelerator = [];
        AddWrite(accelerator, "5", FileToolNames.Edit, code, "已修改 'A.cpp'（1 处编辑）。");
        AddWrite(mitsuko, "6", FileToolNames.Edit, code, "已修改 'A.cpp'（1 处编辑）。");

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

    /// <summary>离席簿记（群流水与心跳日志）不进产物清单：哪怕化身真用 Write 写过心跳.md，也不列、不署名——
    /// 否则「本群产物」指纹被污染，保险丝与退避永不触发</summary>
    [Fact]
    public void BookkeepingFiles_AreExcluded_EvenWhenWrittenViaTools()
    {
        Touch(_room, GroupLogText.FileName);
        Touch(_room, GroupHeartbeatLog.FileName);
        string note = Touch(_room, "正常产物.md");
        List<ChatMessage> avatar = [];
        AddWrite(avatar, "1", FileToolNames.Write, Path.Combine(_room, GroupLogText.FileName), "已保存 '群流水.md'。");
        AddWrite(avatar, "2", FileToolNames.Write, Path.Combine(_room, GroupHeartbeatLog.FileName), "已保存 '心跳.md'。");
        AddWrite(avatar, "3", FileToolNames.Write, note, "已保存 '正常产物.md'。");

        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.Collect(_room, _workspace, [("化身", Written(avatar))]);

        Assert.Equal(["正常产物.md"], artifacts.Select(x => x.DisplayPath));
    }

    private IReadOnlyList<string> Written(List<ChatMessage> history) => GroupArtifacts.WrittenPaths(history, new AgentPathResolver(_workspace));

    /// <summary>运行时文件不进产物：后台任务日志目录（tasks/）、未绑工作区时落在草稿目录里的记忆目录（Memory/）、
    /// 任何位置的 .log——它们自动生成、高频变动，列进来只是噪音；干活落的真产物与子目录里的产物照常列</summary>
    [Fact]
    public void RuntimeLogs_AreExcluded_FromDraftRoom()
    {
        string note = Touch(_room, "正常产物.md");
        Touch(_room, Path.Combine(BackgroundTask.LogDirectoryName, "a1.log"));
        Touch(_room, Path.Combine("logs", "output.log"));
        Touch(_room, Path.Combine(MemoryLayout.FolderName, "偏好.md"));
        Touch(_room, Path.Combine("sub", "report.md"));

        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.Collect(_room, null, []);

        Assert.Equal(new[] { Path.Combine("sub", "report.md"), "正常产物.md" }.Order(),
            artifacts.Select(x => x.DisplayPath).Order());
        Assert.Contains(artifacts, x => x.FullPath == note);
    }

    /// <summary>工作区里模型有意写下的 .log 也不算产物：同一条判据两处共用（见 <see cref="GroupArtifacts"/> 的 IsRuntimeFile）</summary>
    [Fact]
    public void RuntimeLogs_AreExcluded_FromWorkspaceWrites()
    {
        Touch(_workspace, "run.log");
        Touch(_workspace, Path.Combine("Code", "A.cpp"));
        List<ChatMessage> history = [];
        AddWrite(history, "1", FileToolNames.Write, "run.log", "已保存 'run.log'。");
        AddWrite(history, "2", FileToolNames.Write, Path.Combine("Code", "A.cpp"), "已保存 'Code/A.cpp'。");

        IReadOnlyList<GroupArtifact> artifacts = GroupArtifacts.Collect(_room, _workspace, [(string.Empty, Written(history))]);

        Assert.Equal([Path.Combine("Code", "A.cpp")], artifacts.Select(x => x.DisplayPath));
    }

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
