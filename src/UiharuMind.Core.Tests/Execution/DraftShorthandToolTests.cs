using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 草稿目录简写走通整条路：shell 导出同名变量，文件工具认它、搜索结果也按它写回。
/// 写回那一半不能省——模型照抄工具结果，回显长路径它下一次就又去抄长路径。
/// </summary>
public class DraftShorthandToolTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("uiharu-draft-").FullName;
    private readonly string _workspace;
    private readonly string _draft;
    private readonly string _memory;
    private readonly AgentPathResolver _paths;

    public DraftShorthandToolTests()
    {
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "ws")).FullName;
        _draft = Directory.CreateDirectory(Path.Combine(_root, "Workspaces", "ws_1234", "abcd1234")).FullName;
        _memory = Path.Combine(_root, "Workspaces", "ws_1234", "Memory");
        _paths = new AgentPathResolver(_workspace, _draft, _memory, "/bin/zsh");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    [Fact]
    public async Task WriteAndRead_ThroughTheShorthand_LandInTheDraftRoot()
    {
        PermissiveFileAccessTools tools = new(_paths, new FileBackupStore(Path.Combine(_root, "backups")));
        CancellationToken ct = TestContext.Current.CancellationToken;

        await tools.Write("$DRAFT/probe.py", "print('hello')", ct);

        Assert.True(File.Exists(Path.Combine(_draft, "probe.py")));
        Assert.False(Directory.Exists(Path.Combine(_workspace, "$DRAFT"))); //没当成相对路径散进工作区
        Assert.Equal("print('hello')", await tools.Read("$DRAFT/probe.py", cancellationToken: ct));
    }

    /// <summary>记忆目录同理：Write 按需建出目录，Glob 回来的也是简写</summary>
    [Fact]
    public async Task MemoryShorthand_WritesIntoTheMemoryRoot()
    {
        PermissiveFileAccessTools tools = new(_paths, new FileBackupStore(Path.Combine(_root, "backups")));
        CancellationToken ct = TestContext.Current.CancellationToken;

        await tools.Write("$MEMORY/decisions.md", "# 决定", ct);
        GlobOutcome glob = await new SimpleGlobber(_paths).SearchAsync("*.md", "$MEMORY", ct: ct);

        Assert.True(File.Exists(Path.Combine(_memory, "decisions.md")));
        Assert.Equal(["$MEMORY/decisions.md"], glob.Entries.Select(x => x.Path));
    }

    [Fact]
    public async Task SearchResults_InTheDraftRoot_ComeBackAsShorthand()
    {
        await File.WriteAllTextAsync(Path.Combine(_draft, "probe.py"), "hello", TestContext.Current.CancellationToken);

        GlobOutcome glob = await new SimpleGlobber(_paths)
            .SearchAsync("*.py", "$DRAFT", ct: TestContext.Current.CancellationToken);
        GrepOutcome grep = await new SimpleGrepper(_paths)
            .SearchAsync("hello", path: "$DRAFT", ct: TestContext.Current.CancellationToken);

        Assert.Equal(["$DRAFT/probe.py"], glob.Entries.Select(x => x.Path));
        Assert.Equal(["$DRAFT/probe.py"], grep.Matches.Select(x => x.FileName));
    }

    /// <summary>单文件范围的 Glob 按写回的路径做精确过滤：写回成简写之后这一步也得认得它</summary>
    [Fact]
    public async Task GlobScopedToOneDraftFile_StillFindsIt()
    {
        await File.WriteAllTextAsync(Path.Combine(_draft, "probe.py"), "x", TestContext.Current.CancellationToken);

        GlobOutcome glob = await new SimpleGlobber(_paths)
            .SearchAsync("*.py", "$DRAFT/probe.py", ct: TestContext.Current.CancellationToken);

        Assert.Equal(["$DRAFT/probe.py"], glob.Entries.Select(x => x.Path));
    }

    /// <summary>解析不了的路径回一句能照着改的话：文件工具不抛给框架，搜索回结构化失败</summary>
    [Fact]
    public async Task UnknownVariable_IsReportedAsText_AndNothingLandsInTheWorkspace()
    {
        PermissiveFileAccessTools tools = new(_paths, new FileBackupStore(Path.Combine(_root, "backups")));
        CancellationToken ct = TestContext.Current.CancellationToken;

        string write = await tools.Write("$HOME/notes.md", "x", ct);
        string read = await tools.Read("$HOME/notes.md", cancellationToken: ct);
        GlobOutcome glob = await new SimpleGlobber(_paths).SearchAsync("*.md", "$HOME", ct: ct);
        GrepOutcome grep = await new SimpleGrepper(_paths).SearchAsync("x", path: "$HOME", ct: ct);

        Assert.StartsWith("[Write failed]", write);
        Assert.Contains("$DRAFT", read);
        Assert.Equal(ESearchFailureKind.InvalidPath, glob.Failure?.Kind);
        Assert.Equal(ESearchFailureKind.InvalidPath, grep.Failure?.Kind);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
    }

    [Fact]
    public void ShellEnvironment_ExportsBothRoots_AlongsideVenvActivation()
    {
        Dictionary<string, string?> activation = new() { ["VIRTUAL_ENV"] = "/venv" };

        IReadOnlyDictionary<string, string?>? merged = AgentAssemblyPlan.BuildShellEnvironment(activation, _draft, _memory);

        Assert.NotNull(merged);
        Assert.Equal(_draft, merged![AgentPathResolver.DraftVariable]);
        Assert.Equal(_memory, merged[AgentPathResolver.MemoryVariable]);
        Assert.Equal("/venv", merged["VIRTUAL_ENV"]);
        Assert.Null(AgentAssemblyPlan.BuildShellEnvironment(null, string.Empty, string.Empty)); //什么都不加就不改环境
    }
}
