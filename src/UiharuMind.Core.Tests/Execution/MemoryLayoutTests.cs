using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 工作区记忆（ADR 0028）的目录布局。
/// </summary>
public class MemoryLayoutTests
{
    /// <summary>绑了工作区：记忆落在工作区家目录的 Memory/ 子目录，与各会话产出房间平级、跨会话共享</summary>
    [Fact]
    public void GetMemoryDirectory_ForBoundWorkspace_LandsInHomeRoot()
    {
        const string workspace = "/Users/me/projects/client";

        string dir = MemoryLayout.GetMemoryDirectory(workspace, "client_x/12345678");

        Assert.Equal(
            Path.Combine(MemoryLayout.RootPath, WorkspaceSegment.From(workspace), MemoryLayout.FolderName),
            dir);
        // 记忆不进会话房间：房间的清理随会话走，记忆是跨会话的
        Assert.DoesNotContain("12345678", dir);
    }

    /// <summary>没绑工作区：家目录就是会话房间，记忆随会话生灭</summary>
    [Fact]
    public void GetMemoryDirectory_WithoutWorkspace_FollowsTheSessionRoom()
    {
        string dir = MemoryLayout.GetMemoryDirectory(null, "NoWorkspace/12345678");

        Assert.Equal(
            Path.Combine(MemoryLayout.RootPath, "NoWorkspace/12345678", MemoryLayout.FolderName), dir);
    }

    /// <summary>无会话（能力预览）时没有记忆目录——空串即"没有记忆"</summary>
    [Fact]
    public void GetMemoryDirectory_IsEmpty_WithoutSession()
    {
        Assert.Equal(string.Empty, MemoryLayout.GetMemoryDirectory(null, string.Empty));
    }
}