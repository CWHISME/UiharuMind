using Microsoft.Agents.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.Memory;
using UiharuMind.Core.AI.WorldSettings;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉住装配 provider 链：世界设定在知识库之前（知识库贴回答位）、每次装配新建一份列表。
/// </summary>
public class AgentAssemblyProvidersTests
{
    private static AgentAssemblyPlan PlanWith(CharacterData character) => new()
    {
        Profile = new AgentBuildProfile { Character = character },
    };

    [Fact]
    public void BuildContextProviders_WorldSettingBeforeKnowledge_AndFreshPerCall()
    {
        AgentAssemblyPlan plan = PlanWith(new CharacterData { CharacterId = "c" });

        List<AIContextProvider> first = AgentAssembler.BuildContextProviders(plan);
        List<AIContextProvider> second = AgentAssembler.BuildContextProviders(plan);

        Assert.Equal(2, first.Count);
        Assert.IsType<WorldSettingContextProvider>(first[0]);
        Assert.IsType<MemoryContextProvider>(first[1]);
        Assert.NotSame(first, second);
        Assert.NotSame(first[0], second[0]);
    }
}