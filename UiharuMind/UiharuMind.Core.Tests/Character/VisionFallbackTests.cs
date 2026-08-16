using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 「发图有没有退路」的判据。这条判据同时被装配侧（挂不挂 ask_vision）与界面侧
/// （附件盘要不要出警示）消费，一旦两边岔开，症状就是气泡里显示着图片、
/// 模型其实只收到一行路径文本——界面在撒谎，而且不报任何错。
/// </summary>
public class VisionFallbackTests
{
    [Theory]
    [InlineData(ECharacterKind.Roleplay)]
    [InlineData(ECharacterKind.Tool)]
    [InlineData(ECharacterKind.UserCard)]
    public void NonAgentKinds_HaveNoFallback_EvenWithVisionToolEnabled(ECharacterKind kind)
    {
        // 非 agent 档一律不装工具(ADR 0003):开关开着也没用,它根本走不到装配那一步
        AgentToolConfig tools = new() { EnableVisionTool = true };

        Assert.False(VisionFallback.HasFallback(kind, tools));
    }

    [Fact]
    public void AgentKind_HasFallbackOnlyWhenVisionToolEnabled()
    {
        Assert.True(VisionFallback.HasFallback(ECharacterKind.Agent, new AgentToolConfig { EnableVisionTool = true }));
        Assert.False(VisionFallback.HasFallback(ECharacterKind.Agent, new AgentToolConfig { EnableVisionTool = false }));
    }

    [Fact]
    public void NullCharacter_IsTreatedAsHavingFallback()
    {
        // 拿不准就别警示:空态下角色还没定,警示宁可不出
        Assert.True(VisionFallback.HasFallback(null));
    }

    [Fact]
    public void CharacterOverload_FollowsKindAndTools()
    {
        CharacterData roleplay = new() { Kind = ECharacterKind.Roleplay, Tools = new AgentToolConfig() };
        CharacterData agent = new() { Kind = ECharacterKind.Agent, Tools = new AgentToolConfig { EnableVisionTool = true } };

        Assert.False(VisionFallback.HasFallback(roleplay));
        Assert.True(VisionFallback.HasFallback(agent));
    }

    [Theory]
    [InlineData(false, false, false, false)] //没图,谈不上白发
    [InlineData(false, false, true, false)]
    [InlineData(true, true, false, false)] //模型自己看得了图
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, false)] //看不了但有 ask_vision 兜着
    [InlineData(true, false, false, true)] //有图 + 看不了 + 没退路 = 白发
    public void WillDropImages_OnlyWhenAllThreeHold(bool hasImage, bool modelSupportsVision, bool hasFallback,
        bool expected)
    {
        Assert.Equal(expected, VisionFallback.WillDropImages(hasImage, modelSupportsVision, hasFallback));
    }

    [Fact]
    public void RoleplayWithNonVisionModelAndImage_IsTheWarningCase()
    {
        // 这就是 S12-c 要警示的那一格:扮演档 + 非视觉模型 + 粘了图
        CharacterData roleplay = new() { Kind = ECharacterKind.Roleplay, Tools = new AgentToolConfig() };

        Assert.True(VisionFallback.WillDropImages(true, false, VisionFallback.HasFallback(roleplay)));
    }
}
