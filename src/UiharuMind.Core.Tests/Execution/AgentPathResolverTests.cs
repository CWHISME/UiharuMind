using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死工具路径的解析口径。文件工具、搜索、审批、群产物区、审批预演都经它，
/// 这里漂一格，就是「执行落在 A、审批判的是 B」。
/// </summary>
public class AgentPathResolverTests
{
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "uiharu-paths-ws");
    private static readonly string Draft = Path.Combine(Path.GetTempPath(), "uiharu-paths-data", "ws_1234", "abcd1234");

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

    /// <summary>四种 shell 写法都认：模型照着哪种 shell 学的就会写哪种</summary>
    [Theory]
    [InlineData("$DRAFT/probe.py")]
    [InlineData("${DRAFT}/probe.py")]
    [InlineData("$env:DRAFT/probe.py")]
    [InlineData("%DRAFT%/probe.py")]
    public void Resolve_DraftShorthand_ExpandsToTheDraftRoot(string path)
    {
        Assert.Equal(Path.Combine(Draft, "probe.py"), WithDraft().Resolve(path));
    }

    [Fact]
    public void Resolve_BareShorthand_IsTheDraftRootItself()
    {
        Assert.Equal(Draft, WithDraft().Resolve("$DRAFT"));
    }

    /// <summary>只是前缀撞上的不是简写，照旧按相对路径拼工作区</summary>
    [Fact]
    public void Resolve_NameThatMerelyStartsLikeTheShorthand_StaysRelative()
    {
        Assert.Equal(Path.Combine(Workspace, "$DRAFTS", "a.py"), WithDraft().Resolve("$DRAFTS/a.py"));
    }

    /// <summary>简写之后照常规范化：<c>..</c> 能走出草稿目录，审批据此判界外</summary>
    [Fact]
    public void Resolve_ShorthandWithParentSegments_LeavesTheDraftRoot()
    {
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Draft)!, "other.py"), WithDraft().Resolve("$DRAFT/../other.py"));
    }

    /// <summary>没有草稿目录时简写报错，而不是在工作区里建出一个叫 $DRAFT 的文件夹</summary>
    [Fact]
    public void Resolve_ShorthandWithoutDraftRoot_Fails()
    {
        AgentPathResolver paths = new(Workspace);

        Assert.Throws<ArgumentException>(() => paths.Resolve("$DRAFT/probe.py"));
        Assert.False(paths.TryResolve("$DRAFT/probe.py", out _));
    }

    /// <summary>写回用会话 shell 的写法：PowerShell 里 $DRAFT 会静默展开成空串</summary>
    [Theory]
    [InlineData("/bin/zsh", "$DRAFT/sub/a.png")]
    [InlineData("/bin/bash", "$DRAFT/sub/a.png")]
    [InlineData("pwsh", "$env:DRAFT/sub/a.png")]
    [InlineData("/usr/local/bin/powershell", "$env:DRAFT/sub/a.png")]
    [InlineData("cmd.exe", "%DRAFT%/sub/a.png")]
    [InlineData(null, "$DRAFT/sub/a.png")]
    public void ToPortable_FilesInTheDraftRoot_UseTheShellsShorthand(string? shellBinary, string expected)
    {
        AgentPathResolver paths = new(Workspace, Draft, shellBinary);

        Assert.Equal(expected, paths.ToPortable(Path.Combine(Draft, "sub", "a.png")));
        Assert.Equal(paths.DraftToken, paths.ToPortable(Draft));
    }

    /// <summary>写回的简写能原样喂回去</summary>
    [Fact]
    public void ToPortable_RoundTripsThroughResolve()
    {
        AgentPathResolver paths = WithDraft();
        string file = Path.Combine(Draft, "sub", "a.png");

        Assert.Equal(file, paths.Resolve(paths.ToPortable(file)));
    }

    private static AgentPathResolver WithDraft() => new(Workspace, Draft);
}
