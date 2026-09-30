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
    private readonly AgentPathResolver _paths;

    public DraftShorthandToolTests()
    {
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "ws")).FullName;
        _draft = Directory.CreateDirectory(Path.Combine(_root, "Workspaces", "ws_1234", "abcd1234")).FullName;
        _paths = new AgentPathResolver(_workspace, _draft, "/bin/zsh");
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

    [Fact]
    public void ShellEnvironment_ExportsTheDraftRoot_AlongsideVenvActivation()
    {
        Dictionary<string, string?> activation = new() { ["VIRTUAL_ENV"] = "/venv" };

        IReadOnlyDictionary<string, string?>? merged = AgentAssemblyPlan.BuildShellEnvironment(activation, _draft);

        Assert.NotNull(merged);
        Assert.Equal(_draft, merged![AgentPathResolver.DraftVariable]);
        Assert.Equal("/venv", merged["VIRTUAL_ENV"]);
        Assert.Null(AgentAssemblyPlan.BuildShellEnvironment(null, string.Empty)); //什么都不加就不改环境
    }
}
