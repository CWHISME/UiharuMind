using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 脑（<see cref="SubAgentProfile.Brain"/>）与探索档（<see cref="SubAgentProfile.Explorer"/>）的档案：
/// 只读、各自的模型配置源。
///
/// Explorer 字符串值必须仍能反序列化（枚举按字符串序列化），否则导入老盘会炸索引加载——
/// 复活为活档后这层兼容仍在（见 CONTEXT.md 的 Brain 词条）。
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

    /// <summary>Explorer 字符串值仍可反序列化（老存档/导入兼容），且是活档语义</summary>
    [Fact]
    public void Explorer_EnumString_StillParses()
    {
        Assert.True(Enum.TryParse<ESubAgentType>("Explorer", out ESubAgentType parsed));
        Assert.Equal(ESubAgentType.Explorer, parsed);
    }

    //================= 探索档（ADR 0044 复活） =================

    /// <summary>探索档恒定只读，与档位无关：一次「帮我看看」不该顺手改掉东西</summary>
    [Fact]
    public void Explorer_ForceReadOnly_IsTrue()
    {
        Assert.True(SubAgentProfile.Explorer.ForceReadOnly);
    }

    [Fact]
    public void Explorer_ResolveModelName_ReadsExplorerSetting()
    {
        AgentSettingConfig config = new() { ExplorerSubAgentModelName = "survey-1" };

        Assert.Equal("survey-1", SubAgentProfile.Explorer.ResolveModelName(config));
    }

    /// <summary>探索模型配置空串 = 跟随主代理/全局</summary>
    [Fact]
    public void Explorer_ResolveModelName_Empty_FallsBackToGlobal()
    {
        Assert.Equal(string.Empty, SubAgentProfile.Explorer.ResolveModelName(new AgentSettingConfig()));
    }

    /// <summary>探索档只读但不算脑系：右栏照常列条目、不被脑追问认领</summary>
    [Fact]
    public void Explorer_IsNotBrain_ButReadOnly()
    {
        Assert.False(SubAgentProfile.Explorer.IsBrain);
        Assert.False(SubAgentProfile.IsBrainLineage(ESubAgentType.Explorer));
        Assert.True(SubAgentProfile.Explorer.ForceReadOnly);
    }

    /// <summary>探索挂 Glob（与脑不同：脑是内容向不关心目录形状，探索要看目录形状）</summary>
    [Fact]
    public void Explorer_MountsGlob()
    {
        Assert.True(SubAgentProfile.Explorer.MountsGlob);
    }
}