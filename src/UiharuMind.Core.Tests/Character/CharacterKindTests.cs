using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 身份轴的序列化往返与内置角色的定位。这是最容易静默失效的一点：
/// IsAgent 若读不出来会退化为 false，而普通角色是零工具的，
/// 于是智能体会变成一个连文件都读不了的普通聊天角色，且不报任何错。
/// </summary>
public class CharacterKindTests
{
    public CharacterKindTests()
    {
        DefaultCharacterManager.Instance.OnInitialize();
    }

    [Fact]
    public void WorkspaceAgent_IsAgentKind()
    {
        CharacterData agent = DefaultCharacterManager.Instance
            .GetCharacterData(DefaultCharacter.WorkspaceAgent);

        Assert.True(agent.IsAgent);
        Assert.Equal(nameof(DefaultCharacter.WorkspaceAgent), agent.CharacterId);
        Assert.False(string.IsNullOrWhiteSpace(agent.Template));
    }

    [Fact]
    public void EveryBuiltInCharacter_DeclaresItsKindExplicitly()
    {
        foreach (DefaultCharacter value in Enum.GetValues<DefaultCharacter>())
        {
            if (value is DefaultCharacter.Max) continue;

            // 缺字段会静默落到 IsAgent=false:智能体会变成读不了文件的聊天角色。
            // 所以每张内置卡都必须自己写明身份轴(ADR 0043 之前这个字段叫 "Kind")
            string json = EmbeddedResourcesUtils.Read(value + ".json");
            Assert.Contains("\"IsAgent\"", json);
        }
    }

    [Theory]
    [InlineData(DefaultCharacter.UiharuKazari, false, false)]
    [InlineData(DefaultCharacter.WorkspaceAgent, true, false)]
    [InlineData(DefaultCharacter.UserCard, false, true)]
    // ADR 0043 合并之后存量的工具人卡一律是普通角色
    [InlineData(DefaultCharacter.Translator, false, false)]
    [InlineData(DefaultCharacter.Assistant, false, false)]
    public void BuiltInCharacters_LandOnTheirIntendedAxis(DefaultCharacter character, bool isAgent, bool isUserCard)
    {
        CharacterData data = DefaultCharacterManager.Instance.GetCharacterData(character);

        Assert.Equal(isAgent, data.IsAgent);
        Assert.Equal(isUserCard, data.IsUserCard);
    }

    [Fact]
    public void SkillCharacters_AreInternal()
    {
        // 程序点名取用的技能角色不该出现在角色库默认视图与任何选择器候选里
        Assert.True(DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Vision).IsInternal);
        Assert.True(DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Translator).IsInternal);
        Assert.False(DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.UiharuKazari).IsInternal);
        Assert.False(DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.WorkspaceAgent).IsInternal);
    }

    /// <summary>
    /// 每个能开会话的角色都必须<b>恰好</b>落进一边：普通对话或智能体。
    ///
    /// 这条是实机踩出来的：装配分支曾写作 <c>== Roleplay</c>、聊天页会话列表曾写作
    /// <c>GetSessions(Roleplay)</c>——工具人两处都漏，会话在两边都不显示。
    /// 现在身份由两个标记组合，四种组合全部过一遍，将来加第三个标记时这条会立刻炸。
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EveryCharacter_LandsOnExactlyOneSurface(bool isAgent, bool isUserCard)
    {
        CharacterData character = new() { IsAgent = isAgent, IsUserCard = isUserCard };
        if (!character.CanStartSession()) return; //用户卡不开会话

        Assert.True(character.IsChat() ^ character.IsAgent, $"IsAgent={isAgent} 没有归类或同时归了两类");
    }

    /// <summary>
    /// 身份轴的往返，以及<b>老存档的迁移</b>。
    ///
    /// 后半段是承重的：升级前的角色卡身上是 <c>"Kind": "Agent"</c>，没有
    /// <c>CharacterData.LegacyKind</c> 那个只写不读的垫片，它们会全部落到
    /// <c>IsAgent=false</c> —— <b>现存的智能体静默降级成普通角色</b>。
    /// </summary>
    [Fact]
    public void IdentityAxis_RoundTrips_AndLegacyKindStillMigrates()
    {
        CharacterData original = new() { IsAgent = true };
        string json = SaveUtility.SaveToString(original);

        Assert.Contains("\"IsAgent\"", json);
        Assert.DoesNotContain("\"Kind\"", json); //旧字段只读不写
        Assert.True(SaveUtility.LoadFromString<CharacterData>(json).IsAgent);

        // 老存档：四档枚举字符串仍要能读进来并落到正确的轴上
        Assert.True(SaveUtility.LoadFromString<CharacterData>("{\"Kind\":\"Agent\"}").IsAgent);
        Assert.True(SaveUtility.LoadFromString<CharacterData>("{\"Kind\":\"UserCard\"}").IsUserCard);
        Assert.False(SaveUtility.LoadFromString<CharacterData>("{\"Kind\":\"Tool\"}").IsAgent);
        Assert.False(SaveUtility.LoadFromString<CharacterData>("{\"Kind\":\"Roleplay\"}").IsAgent);

        // 意外的形态只当普通角色,不能让整张卡读不进来
        CharacterData odd = SaveUtility.LoadFromString<CharacterData>("{\"Kind\":2,\"CharacterId\":\"x\"}");
        Assert.False(odd.IsAgent);
        Assert.Equal("x", odd.CharacterId);
    }

    [Fact]
    public void VisionCharacters_RequireVisionModel()
    {
        Assert.True(DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Vision)
            .RequiresVisionModel);
        Assert.True(DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.VisionOcr)
            .RequiresVisionModel);
        Assert.False(DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Translator)
            .RequiresVisionModel);
    }
}
