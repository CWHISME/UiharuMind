using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死工作区说明注入的语义：AGENTS.md 优先于 CLAUDE.md、超长截断、
/// 文件内容变化经装配快照触发重建。
/// </summary>
public class WorkspaceInstructionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("uiharu-ws-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
    }

    [Fact]
    public void Load_PrefersAgentsMd_OverClaudeMd()
    {
        File.WriteAllText(Path.Combine(_dir, "AGENTS.md"), "agents rules");
        File.WriteAllText(Path.Combine(_dir, "CLAUDE.md"), "claude rules");

        Assert.Equal("agents rules", WorkspaceInstructionsLoader.Load(_dir));
    }

    [Fact]
    public void Load_FallsBackToClaudeMd()
    {
        File.WriteAllText(Path.Combine(_dir, "CLAUDE.md"), "claude rules");

        Assert.Equal("claude rules", WorkspaceInstructionsLoader.Load(_dir));
    }

    [Fact]
    public void Load_MissingFileOrWorkspace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, WorkspaceInstructionsLoader.Load(_dir));
        Assert.Equal(string.Empty, WorkspaceInstructionsLoader.Load(null));
    }

    [Fact]
    public void Load_OverlongContent_IsTruncated()
    {
        File.WriteAllText(Path.Combine(_dir, "AGENTS.md"), new string('x', 50_000));

        string loaded = WorkspaceInstructionsLoader.Load(_dir);

        Assert.Contains("[workspace instructions truncated]", loaded);
        Assert.True(loaded.Length < 17_000, $"截断后长度应受限,实际 {loaded.Length}");
    }

    [Fact]
    public void ChangedWorkspaceInstructions_ProduceDifferentSnapshot()
    {
        CharacterData character = new() { CharacterId = "agent", Kind = ECharacterKind.Agent };
        AgentSettingConfig config = new();

        AgentAssemblySnapshot before = AgentAssemblySnapshot.Capture(character, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, config, 1, workspaceInstructions: "v1");
        AgentAssemblySnapshot after = AgentAssemblySnapshot.Capture(character, "prompt", "/ws",
            EAgentPermissionMode.AutoEdit, null, config, 1, workspaceInstructions: "v2");

        Assert.NotEqual(before, after); //文件编辑 → 下一次挂接重建装配
    }
}
