using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 各档档案：只读边界（现在只剩脑）、各自的模型配置源、类型到档案的唯一映射。
///
/// Explorer 的枚举值必须仍能读回来（枚举按<b>数字</b>序列化，所以兼容靠的是「新值只追加在末尾」），
/// 否则导入老盘会炸索引加载——
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
    public void Brain_ResolveModelNames_ReadsBrainSetting()
    {
        AgentSettingConfig config = new() { BrainSubAgentModelNames = { "fast-1", "fast-2" } };

        Assert.Equal(new[] { "fast-1", "fast-2" }, SubAgentProfile.Brain.ResolveModelNames(config));
    }

    /// <summary>脑模型未配置 = 跟随主代理/全局（空列表）</summary>
    [Fact]
    public void Brain_ResolveModelNames_Empty_FallsBackToGlobal()
    {
        Assert.Empty(SubAgentProfile.Brain.ResolveModelNames(new AgentSettingConfig()));
    }

    /// <summary>General 不吃脑配置</summary>
    [Fact]
    public void General_ResolveModelNames_IgnoresBrainSetting()
    {
        AgentSettingConfig config = new()
        {
            BrainSubAgentModelNames = { "fast-1" },
            GeneralSubAgentModelNames = { "main-1" },
        };

        Assert.Equal(new[] { "main-1" }, SubAgentProfile.General.ResolveModelNames(config));
    }

    /// <summary>Explorer 字符串值仍可反序列化（老存档/导入兼容），且是活档语义</summary>
    [Fact]
    public void Explorer_EnumString_StillParses()
    {
        Assert.True(Enum.TryParse<ESubAgentType>("Explorer", out ESubAgentType parsed));
        Assert.Equal(ESubAgentType.Explorer, parsed);
    }

    //================= 探索档（ADR 0044 复活） =================

    /// <summary>
    /// 委派的三档都<b>不</b>强制只读（ADR 0112 修订）：工具集相同，能不能改由权限档回答。
    /// 从前探索档恒定只读，那条产品决定已取消。
    /// </summary>
    [Theory]
    [InlineData(ESubAgentType.General)]
    [InlineData(ESubAgentType.Explorer)]
    [InlineData(ESubAgentType.Reviewer)]
    public void DelegationTiers_AreNotForcedReadOnly(ESubAgentType type)
    {
        Assert.False(SubAgentProfile.Of(type).ForceReadOnly);
    }

    [Fact]
    public void Explorer_ResolveModelNames_ReadsExplorerSetting()
    {
        AgentSettingConfig config = new() { ExplorerSubAgentModelNames = { "survey-1" } };

        Assert.Equal(new[] { "survey-1" }, SubAgentProfile.Explorer.ResolveModelNames(config));
    }

    /// <summary>探索模型未配置 = 跟随主代理/全局（空列表）</summary>
    [Fact]
    public void Explorer_ResolveModelNames_Empty_FallsBackToGlobal()
    {
        Assert.Empty(SubAgentProfile.Explorer.ResolveModelNames(new AgentSettingConfig()));
    }

    /// <summary>探索档不算脑系：右栏照常列条目、不被脑追问认领</summary>
    [Fact]
    public void Explorer_IsNotBrain()
    {
        Assert.False(SubAgentProfile.Explorer.IsBrain);
        Assert.False(SubAgentProfile.IsBrainLineage(ESubAgentType.Explorer));
    }

    /// <summary>探索挂 Glob（与脑不同：脑是内容向不关心目录形状，探索要看目录形状）</summary>
    [Fact]
    public void Explorer_MountsGlob()
    {
        Assert.True(SubAgentProfile.Explorer.MountsGlob);
    }

    //================= 评审档（ADR 0112） =================

    /// <summary>评审档与其它委派档同边界：它不「顺手改」是分工，不是挂不上写工具（ADR 0112 修订）</summary>
    [Fact]
    public void Reviewer_HasNoReadOnlyBoundaryOfItsOwn()
    {
        Assert.False(SubAgentProfile.Reviewer.ForceReadOnly);
        Assert.True(SubAgentProfile.Brain.ForceReadOnly); // 只读边界现在只剩整理档
    }

    /// <summary>评审要看目录形状与形状之外的东西：与脑不同，它挂 Glob</summary>
    [Fact]
    public void Reviewer_MountsGlob()
    {
        Assert.True(SubAgentProfile.Reviewer.MountsGlob);
    }

    /// <summary>评审不算脑系：右栏照常列条目，也不该被脑追问认领</summary>
    [Fact]
    public void Reviewer_IsNotBrainLineage()
    {
        Assert.False(SubAgentProfile.IsBrainLineage(ESubAgentType.Reviewer));
    }

    /// <summary>读自己那一列模型，不吃通用/探索的配置</summary>
    [Fact]
    public void Reviewer_ResolveModelNames_ReadsItsOwnColumn()
    {
        AgentSettingConfig config = new()
        {
            GeneralSubAgentModelNames = { "g" },
            ExplorerSubAgentModelNames = { "e" },
            ReviewerSubAgentModelNames = { "r1", "r2" },
        };

        Assert.Equal(new[] { "r1", "r2" }, SubAgentProfile.Reviewer.ResolveModelNames(config));
    }

    /// <summary>类型 → 档案只有一处映射：每个枚举值都必须映射到自己那档，不能落进 default</summary>
    [Theory]
    [InlineData(ESubAgentType.General)]
    [InlineData(ESubAgentType.Brain)]
    [InlineData(ESubAgentType.Explorer)]
    [InlineData(ESubAgentType.Reviewer)]
    public void Of_MapsEveryTypeToItsOwnProfile(ESubAgentType type)
    {
        Assert.Equal(type, SubAgentProfile.Of(type).Type);
    }
}
