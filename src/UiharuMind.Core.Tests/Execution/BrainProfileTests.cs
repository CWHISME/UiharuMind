using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 脑（<see cref="SubAgentProfile.Brain"/>）的档案：只读、模型源=脑配置。
///
/// 顺带钉住 Explorer 墓碑：它是反序列化存量的墓碑，字符串值 <c>"Explorer"</c> 必须仍能解析，
/// 否则盘上老会话的索引加载会抛 JsonException（见 ADR 0082 与 CONTEXT.md 的 Brain 词条）。
/// </summary>
public class BrainProfileTests
{
    [Fact]
    public void Brain_ForceReadOnly_IsTrue()
    {
        Assert.True(SubAgentProfile.Brain.ForceReadOnly);
    }

    [Fact]
    public void Brain_ResolveModelName_ReadsBrainSetting()
    {
        AgentSettingConfig config = new() { BrainSubAgentModelName = "fast-1" };

        Assert.Equal("fast-1", SubAgentProfile.Brain.ResolveModelName(config));
    }

    /// <summary>脑模型配置空串 = 跟随主代理/全局</summary>
    [Fact]
    public void Brain_ResolveModelName_Empty_FallsBackToGlobal()
    {
        Assert.Equal(string.Empty, SubAgentProfile.Brain.ResolveModelName(new AgentSettingConfig()));
    }

    /// <summary>General 不吃脑配置</summary>
    [Fact]
    public void General_ResolveModelName_IgnoresBrainSetting()
    {
        AgentSettingConfig config = new() { BrainSubAgentModelName = "fast-1", GeneralSubAgentModelName = "main-1" };

        Assert.Equal("main-1", SubAgentProfile.General.ResolveModelName(config));
    }

    /// <summary>Explorer 墓碑：老会话字符串值仍可反序列化，不炸索引加载</summary>
    [Fact]
    public void Explorer_Tombstone_StillParses()
    {
        Assert.True(Enum.TryParse<ESubAgentType>("Explorer", out ESubAgentType parsed));
        Assert.Equal(ESubAgentType.Explorer, parsed);
    }
}
