using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 装配侧对会话形态（ADR 0050）的分叉：agent 卡开成普通对话形态必须走 prompt-only 管线——
/// 不解析工作区、不装配工具、不挂 MCP/技能，能力快照也只报角色提示词段。
/// </summary>
public class AgentFormAssemblyTests
{
    private static CharacterData AgentCard() => new()
    {
        CharacterId = "form-agent",
        IsAgent = true,
        Tools = new AgentToolConfig
        {
            EnableFileAccess = true,
            EnableShellExecution = true,
            EnableKnowledgeSearchTool = true,
        },
    };

    [Fact]
    public void Profile_EffectiveForm_FallsBackToIdentity()
    {
        CharacterData agent = AgentCard();

        Assert.False(AgentBuildProfile.FromDraft(agent, "/ws", 0, isAgentForm: false).EffectiveIsAgentForm);
        Assert.True(AgentBuildProfile.FromDraft(agent, "/ws", 0).EffectiveIsAgentForm); //null = 跟身份
        Assert.False(AgentBuildProfile.FromSession(new ChatSession { IsAgentForm = false }).EffectiveIsAgentForm);
    }

    [Fact]
    public void Resolve_ChatFormAgentCard_SkipsToolResolution()
    {
        AgentAssemblyPlan plan = AgentAssemblyPlan.Resolve(
            AgentBuildProfile.FromDraft(AgentCard(), "/ws", 0, isAgentForm: false));

        Assert.False(plan.IsAgentForm);
        Assert.Equal(string.Empty, plan.WorkingDirectory); //没有工作区解析
        Assert.False(plan.MountVisionTool);
    }

    [Fact]
    public void Facts_ChatFormAgentCard_ZerosAllToolInputs()
    {
        AgentAssemblyFacts facts = AgentAssemblyFacts.Capture(AgentCard(),
            new AgentAssemblyInputs
            {
                Instructions = "instr", WorkspacePath = "/ws", Permission = EAgentPermissionMode.AutoEdit,
                WorkspaceInstructions = "ws-instr", OutputFolderName = "room", IsAgentForm = false,
            });

        Assert.False(facts.IsAgent);
        Assert.Null(facts.WorkspacePath);
        Assert.False(facts.FileAccess);
        Assert.False(facts.Shell);
        Assert.False(facts.KnowledgeSearchTool);
        Assert.False(facts.TodoList);
        Assert.False(facts.AgentMode);
    }

    [Fact]
    public void Facts_AgentForm_SameCard_KeepsTools()
    {
        AgentAssemblyFacts facts = AgentAssemblyFacts.Capture(AgentCard(),
            new AgentAssemblyInputs
            {
                Instructions = "instr", WorkspacePath = "/ws", Permission = EAgentPermissionMode.AutoEdit,
                WorkspaceInstructions = "ws-instr", OutputFolderName = "room", IsAgentForm = true,
            });

        Assert.True(facts.IsAgent);
        Assert.Equal("/ws", facts.WorkspacePath);
        Assert.True(facts.FileAccess);
        Assert.True(facts.Shell);
        Assert.True(facts.KnowledgeSearchTool);
    }

    [Fact]
    public void Facts_SameCard_DifferentForm_DoNotMatch()
    {
        // 句柄跨会话复用只比快照：同一张卡、不同形态的两个会话绝不能捡到同一个 agent
        AgentAssemblyFacts agentForm = AgentAssemblyFacts.Capture(AgentCard(),
            new AgentAssemblyInputs
            {
                Instructions = "instr", WorkspacePath = "/ws", Permission = EAgentPermissionMode.AutoEdit,
                IsAgentForm = true,
            });
        AgentAssemblyFacts chatForm = AgentAssemblyFacts.Capture(AgentCard(),
            new AgentAssemblyInputs
            {
                Instructions = "instr", WorkspacePath = "/ws", Permission = EAgentPermissionMode.AutoEdit,
                IsAgentForm = false,
            });

        Assert.NotEqual(agentForm, chatForm);
    }
}
