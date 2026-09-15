using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死 agent 产物的目录布局。承重之处是<b>不同会话不许共用一个房间</b>：
/// 产物以绝对路径写进对话历史，共用时两个会话各画一张同名图，后者会静默盖掉前者，
/// 而旧对话回头看一切正常、只是图是错的——这类缺陷在实机上几乎不可能被发现。
/// 布局本身见 ADR 0026：一个工作区一个家，家里按会话分房间。
/// </summary>
public class AgentOutputLayoutTests
{
    private const string SessionA = "3f2a1b0c9d8e7f6a5b4c3d2e1f009988";
    private const string SessionB = "aabbccdd11223344556677889900aabb";

    private const string Workspace1 = "/Users/me/projects/client";
    private const string Workspace2 = "/Users/me/projects/server";

    [Fact]
    public void GetFolderName_SeparatesSessionsIntoRoomsUnderSameWorkspace()
    {
        string a = AgentOutputLayout.GetFolderName(Workspace1, SessionA);
        string b = AgentOutputLayout.GetFolderName(Workspace1, SessionB);

        // 同一个工作区共用一个家,房间按会话分——跨会话同名图不互盖的根基
        Assert.Equal(Path.GetDirectoryName(a), Path.GetDirectoryName(b));
        Assert.NotEqual(a, b);
        Assert.Contains("client_", a); //家目录名要带目录名,用户得能认出来
        // 实现里是以字面 '/' 拼接(与 FileMemoryLayout 同惯例),这里断言也写字面 '/',
        // 使得 Windows 上 dotnet test 也能过(Path.DirectorySeparatorChar 在那边是 '\\')
        Assert.EndsWith("/3f2a1b0c", a);
    }

    [Fact]
    public void GetFolderName_DifferentWorkspacesGetDifferentHomes()
    {
        string a = AgentOutputLayout.GetFolderName(Workspace1, SessionA);
        string c = AgentOutputLayout.GetFolderName(Workspace2, SessionA);

        Assert.NotEqual(Path.GetDirectoryName(a), Path.GetDirectoryName(c));
    }

    [Fact]
    public void GetFolderName_NoWorkspace_GoesToNoWorkspaceBucket()
    {
        // 同 GetFolderName_SeparatesSessionsIntoRoomsUnderSameWorkspace:字面 '/' 是实现的拼接字符
        string expected = $"{AgentOutputLayout.NoWorkspaceFolder}/3f2a1b0c";
        Assert.Equal(expected, AgentOutputLayout.GetFolderName(null, SessionA));
        Assert.Equal(expected, AgentOutputLayout.GetFolderName("", SessionA));
        Assert.Equal(expected, AgentOutputLayout.GetFolderName("   ", SessionA));
    }

    [Fact]
    public void GetFolderName_IsEmptyWithoutSession()
    {
        // 能力预览走的是 FromDraft,此时没有会话。空串让装配退回根而不是造怪目录
        Assert.Equal(string.Empty, AgentOutputLayout.GetFolderName(Workspace1, string.Empty));
    }

    [Fact]
    public void DeleteAll_RemovesRoomAndPrunesEmptyHome_KeepsOtherSessions()
    {
        using TempRoot tmp = new();
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, "homeA", "3f2a1b0c"));
        File.WriteAllText(Path.Combine(tmp.Workspaces, "homeA", "3f2a1b0c", "chart.png"), "x");
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, "homeB", "aabbccdd"));
        File.WriteAllText(Path.Combine(tmp.Workspaces, "homeB", "aabbccdd", "data.csv"), "x");

        AgentOutputLayout.DeleteAll(tmp.Workspaces, tmp.Legacy, SessionA);

        // SessionA 的房间没了,且它独占的家整个被收掉
        Assert.False(Directory.Exists(Path.Combine(tmp.Workspaces, "homeA")));
        // SessionB 的房间与家都不被牵连
        Assert.True(Directory.Exists(Path.Combine(tmp.Workspaces, "homeB", "aabbccdd")));
    }

    [Fact]
    public void DeleteAll_KeepsHomeWhenItStillHostsOtherSessions()
    {
        using TempRoot tmp = new();
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, "home", "3f2a1b0c"));
        File.WriteAllText(Path.Combine(tmp.Workspaces, "home", "3f2a1b0c", "chart.png"), "x");
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, "home", "aabbccdd"));
        File.WriteAllText(Path.Combine(tmp.Workspaces, "home", "aabbccdd", "data.csv"), "x");

        AgentOutputLayout.DeleteAll(tmp.Workspaces, tmp.Legacy, SessionA);

        Assert.False(Directory.Exists(Path.Combine(tmp.Workspaces, "home", "3f2a1b0c")));
        // 同一家的别的会话房间还在,家也不能被删
        Assert.True(Directory.Exists(Path.Combine(tmp.Workspaces, "home", "aabbccdd")));
    }

    [Fact]
    public void DeleteAll_AlsoCleansLegacyLayoutRemnants()
    {
        using TempRoot tmp = new();
        // 旧布局的残留:改过名留下的多个目录,按 id 后缀通配清掉
        Directory.CreateDirectory(Path.Combine(tmp.Legacy, "旧标题_3f2a1b0c"));
        File.WriteAllText(Path.Combine(tmp.Legacy, "旧标题_3f2a1b0c", "chart.png"), "x");
        Directory.CreateDirectory(Path.Combine(tmp.Legacy, "别人的会话_aabbccdd"));
        File.WriteAllText(Path.Combine(tmp.Legacy, "别人的会话_aabbccdd", "chart.png"), "x");

        AgentOutputLayout.DeleteAll(tmp.Workspaces, tmp.Legacy, SessionA);

        Assert.False(Directory.Exists(Path.Combine(tmp.Legacy, "旧标题_3f2a1b0c")));
        Assert.True(Directory.Exists(Path.Combine(tmp.Legacy, "别人的会话_aabbccdd"))); //别的会话不被牵连
    }

    [Fact]
    public void SweepEmptyDirectories_RemovesOnlyEmptyRoomsAndHomes()
    {
        using TempRoot tmp = new();
        // 空房间、有内容的房间、空的家、空的 NoWorkspace 桶、空的旧布局目录、有内容的旧布局目录
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, "home", "3f2a1b0c"));
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, "home", "aabbccdd"));
        File.WriteAllText(Path.Combine(tmp.Workspaces, "home", "aabbccdd", "chart.png"), "x");
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, "emptyHome"));
        Directory.CreateDirectory(Path.Combine(tmp.Workspaces, AgentOutputLayout.NoWorkspaceFolder));
        Directory.CreateDirectory(Path.Combine(tmp.Legacy, "空的旧目录"));
        Directory.CreateDirectory(Path.Combine(tmp.Legacy, "有内容的旧目录"));
        File.WriteAllText(Path.Combine(tmp.Legacy, "有内容的旧目录", "chart.png"), "x");

        AgentOutputLayout.SweepEmptyDirectories(tmp.Workspaces, tmp.Legacy);

        // 空房间被删,有内容的房间留着;家因空房间被删而变空的也被收掉
        Assert.False(Directory.Exists(Path.Combine(tmp.Workspaces, "home", "3f2a1b0c")));
        Assert.True(Directory.Exists(Path.Combine(tmp.Workspaces, "home", "aabbccdd")));
        Assert.True(Directory.Exists(Path.Combine(tmp.Workspaces, "home")));
        Assert.False(Directory.Exists(Path.Combine(tmp.Workspaces, "emptyHome")));
        Assert.False(Directory.Exists(Path.Combine(tmp.Workspaces, AgentOutputLayout.NoWorkspaceFolder)));
        Assert.False(Directory.Exists(Path.Combine(tmp.Legacy, "空的旧目录")));
        Assert.True(Directory.Exists(Path.Combine(tmp.Legacy, "有内容的旧目录")));
    }

    /// <summary>临时根：新布局与旧布局两个根都装进同一个临时目录，用后即焚</summary>
    private sealed class TempRoot : IDisposable
    {
        public string Workspaces { get; }
        public string Legacy { get; }

        private readonly string _root;

        public TempRoot()
        {
            _root = Path.Combine(Path.GetTempPath(), "uiharu-outputs-test-" + Guid.NewGuid().ToString("N"));
            Workspaces = Path.Combine(_root, "Workspaces");
            Legacy = Path.Combine(_root, "Legacy");
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
