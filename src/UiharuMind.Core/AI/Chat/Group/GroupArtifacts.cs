using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>产物落在哪</summary>
public enum EGroupArtifactSource
{
    /// <summary>全群共用的草稿目录</summary>
    DraftRoom,

    /// <summary>工作区（成员用 Write / Edit 改的）</summary>
    Workspace,
}

/// <summary>
/// 本群的一件产物
/// </summary>
/// <param name="FullPath">绝对路径</param>
/// <param name="DisplayPath">显示用的路径：草稿目录里的相对草稿目录，工作区里的相对工作区，界外的给绝对路径</param>
/// <param name="Source">落在哪</param>
/// <param name="Authors">写过它的成员，按第一次写的先后；草稿目录里别处来的文件（用户放的、命令行生成的）为空</param>
/// <param name="LastWrite">盘上的最后修改时间</param>
public sealed record GroupArtifact(string FullPath, string DisplayPath, EGroupArtifactSource Source,
    IReadOnlyList<string> Authors, DateTimeOffset LastWrite);

/// <summary>
/// 本群产物区（方案 v6 §1.4「产出锚点」）：群到底落了哪些盘。两个来源——
/// <list type="bullet">
/// <item>草稿目录：全群共用那一间，直接列盘，谁放的都算</item>
/// <item>工作区：成员会话里成功的 Write / Edit。只认工具结果里「真写进去了」的那几次（<see cref="FileToolNames.IsSuccessfulWrite"/>），
/// 不看 git——那会把用户自己的改动也算进来；命令行改的文件认不出来，是这条来源的已知盲区</item>
/// </list>
/// 纯函数：读盘与读历史都在这里，谁来刷新、什么时候刷新是界面的事
/// </summary>
public static class GroupArtifacts
{
    private const string FilePathArgument = "filePath"; //Write / Edit 的路径参数名

    /// <summary>
    /// 群的草稿目录（成员共用群壳那一间，见 <c>AgentBuildProfile.OutputFolderName</c>）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>绝对路径</returns>
    public static string DraftRoomOf(ChatSession group) =>
        AgentOutputLayout.GetRoomAbsolutePath(AgentOutputLayout.GetFolderName(group.WorkspacePath, group.SessionId));

    /// <summary>
    /// 成员工具调用里的路径按什么口径解析（认成员写过哪些文件用）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>路径解析口径</returns>
    public static AgentPathResolver MemberPathsOf(ChatSession group) =>
        new(group.WorkspacePath, DraftRoomOf(group),
            MemoryLayout.GetMemoryDirectory(group.WorkspacePath, AgentOutputLayout.GetFolderName(group.WorkspacePath, group.SessionId)));

    /// <summary>
    /// 收集本群产物（读会话管理器）：在群里待过的成员、加上化身写过的都算，退群的人写过的照样列
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>产物，最近改过的在前</returns>
    public static IReadOnlyList<GroupArtifact> CollectFor(ChatSession group)
    {
        List<(string, IReadOnlyList<string>)> writers = [];
        AgentPathResolver paths = MemberPathsOf(group);
        IEnumerable<string> sessionIds = GroupRoster.Of(group).Everyone.Select(x => x.SessionId);
        if (GroupAvatar.MetaOf(group.SessionId) is { } avatar) sessionIds = sessionIds.Append(avatar.SessionId);
        foreach (string id in sessionIds)
        {
            if (SessionManager.Instance.Load(id) is not { } writer) continue;
            // 历史已卸掉的用缓存，不为这一张清单把整份历史读回来
            writers.Add((GroupSceneSource.SpeakerNameOf(writer), GroupWrittenPaths.Of(writer, paths)));
        }

        return Collect(DraftRoomOf(group), group.WorkspacePath, writers);
    }

    /// <summary>
    /// 收集本群产物，最近改过的在前
    /// </summary>
    /// <param name="draftRoom">草稿目录绝对路径</param>
    /// <param name="workspace">群的工作区；没绑为 null</param>
    /// <param name="members">各成员的名字与写过的文件（<see cref="WrittenPaths"/>，界面经 <see cref="GroupWrittenPaths"/> 取），按成员顺序</param>
    /// <returns>产物；盘上已不存在的不列</returns>
    public static IReadOnlyList<GroupArtifact> Collect(string draftRoom, string? workspace,
        IEnumerable<(string Name, IReadOnlyList<string> Written)> members)
    {
        Dictionary<string, List<string>> authors = new(PathComparer);
        foreach ((string name, IReadOnlyList<string> written) in members)
        {
            foreach (string path in written)
            {
                if (!authors.TryGetValue(path, out List<string>? list)) authors[path] = list = [];
                if (!list.Contains(name)) list.Add(name);
            }
        }

        List<GroupArtifact> artifacts = [];
        HashSet<string> seen = new(PathComparer);
        foreach (string file in ListDraftRoom(draftRoom))
        {
            seen.Add(file);
            artifacts.Add(new GroupArtifact(file, Path.GetRelativePath(draftRoom, file), EGroupArtifactSource.DraftRoom,
                authors.GetValueOrDefault(file) ?? [], File.GetLastWriteTime(file)));
        }

        foreach ((string file, List<string> names) in authors)
        {
            if (seen.Contains(file) || !File.Exists(file)) continue;
            artifacts.Add(new GroupArtifact(file, DisplayPathOf(file, workspace), EGroupArtifactSource.Workspace,
                names, File.GetLastWriteTime(file)));
        }

        return artifacts.OrderByDescending(x => x.LastWrite).ToList();
    }

    /// <summary>
    /// 一段会话历史里成功写过的文件（绝对路径，按第一次写的先后，不重复）
    /// </summary>
    /// <param name="history">会话历史</param>
    /// <param name="paths">成员工具的路径口径；没绑工作区时相对路径认不出落在哪，跳过</param>
    /// <returns>绝对路径</returns>
    public static IReadOnlyList<string> WrittenPaths(IReadOnlyList<ChatMessage> history, AgentPathResolver paths)
    {
        Dictionary<string, string> pending = new(); //调用编号 → 目标路径，等它的结果来确认
        List<string> written = [];
        foreach (AIContent content in history.SelectMany(x => x.Contents))
        {
            if (content is FunctionCallContent call && FileToolNames.Mutating.Contains(call.Name)
                && ResolveTarget(call, paths) is { } target)
            {
                pending[call.CallId] = target;
            }
            else if (content is FunctionResultContent { CallId: { } callId } result
                     && pending.Remove(callId, out string? path)
                     && FileToolNames.IsSuccessfulWrite(result.Result?.ToString())
                     && !written.Contains(path, PathComparer))
            {
                written.Add(path);
            }
        }

        return written;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string? ResolveTarget(FunctionCallContent call, AgentPathResolver paths)
    {
        object? argument = null;
        if (call.Arguments?.TryGetValue(FilePathArgument, out argument) != true) return null;

        //不成形的路径：那次写入本来也落不了盘
        return paths.TryResolve(argument?.ToString(), out string full) ? full : null;
    }

    private static IEnumerable<string> ListDraftRoom(string draftRoom)
    {
        if (string.IsNullOrWhiteSpace(draftRoom) || !Directory.Exists(draftRoom)) return [];

        try
        {
            // 点开头的（.DS_Store、编辑器的临时文件）不是谁的产物
            return Directory.EnumerateFiles(draftRoom, "*", SearchOption.AllDirectories)
                .Where(x => !Path.GetFileName(x).StartsWith('.'))
                .ToList();
        }
        catch (Exception e)
        {
            Log.Warning($"Group artifacts: list draft room '{draftRoom}' failed: {e.Message}");
            return [];
        }
    }

    private static string DisplayPathOf(string file, string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace)) return file;

        string relative = Path.GetRelativePath(workspace, file);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? file : relative;
    }
}
