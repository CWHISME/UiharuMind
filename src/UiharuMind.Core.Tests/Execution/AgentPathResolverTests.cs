using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死工具路径的解析口径。文件工具、搜索、审批、群产物区、审批预演都经它，
/// 这里漂一格，就是「执行落在 A、审批判的是 B」。
/// </summary>
public class AgentPathResolverTests
{
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "uiharu-paths-ws");

    [Fact]
    public void Resolve_RelativeGoesUnderWorkspace_AbsoluteStaysPut()
    {
        AgentPathResolver paths = new(Workspace);
        string outside = Path.Combine(Path.GetTempPath(), "elsewhere", "a.txt");

        Assert.Equal(Path.Combine(Workspace, "src", "A.cs"), paths.Resolve("src/A.cs"));
        Assert.Equal(outside, paths.Resolve(outside));
    }

    [Fact]
    public void Resolve_RelativeToOverridesTheBase()
    {
        AgentPathResolver paths = new(Workspace);
        string searchRoot = Path.Combine(Workspace, "src");

        Assert.Equal(Path.Combine(searchRoot, "A.cs"), paths.Resolve("A.cs", searchRoot));
    }

    [Fact]
    public void TryResolve_FailsWithoutWorkspaceForRelative_ButAbsoluteStillWorks()
    {
        AgentPathResolver paths = new(null);
        string absolute = Path.Combine(Workspace, "a.txt");

        Assert.False(paths.TryResolve("a.txt", out _));
        Assert.False(paths.TryResolve("  ", out _));
        Assert.True(paths.TryResolve(absolute, out string full));
        Assert.Equal(absolute, full);
    }

    [Fact]
    public void ToPortable_RelativeInsideWorkspace_AbsoluteOutside()
    {
        AgentPathResolver paths = new(Workspace);
        string outside = Path.Combine(Path.GetTempPath(), "elsewhere", "a.txt");

        Assert.Equal("src/A.cs", paths.ToPortable(Path.Combine(Workspace, "src", "A.cs")));
        Assert.Equal(outside, paths.ToPortable(outside));
    }

    [Fact]
    public void SameRoots_AreEqual()
    {
        Assert.Equal(new AgentPathResolver(Workspace), new AgentPathResolver(Workspace + Path.DirectorySeparatorChar + "."));
    }
}
