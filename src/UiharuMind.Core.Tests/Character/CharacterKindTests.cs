using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 角色档位的序列化往返与内置角色的定档。这是最容易静默失效的一点：
/// Kind 若解析不出来会退化为默认值 Roleplay，而 Roleplay 档是零工具的，
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

        Assert.Equal(ECharacterKind.Agent, agent.Kind);
        Assert.Equal(nameof(DefaultCharacter.WorkspaceAgent), agent.CharacterId);
        Assert.False(string.IsNullOrWhiteSpace(agent.Template));
    }

    [Fact]
    public void EveryBuiltInCharacter_DeclaresItsKindExplicitly()
    {
        foreach (DefaultCharacter value in Enum.GetValues<DefaultCharacter>())
        {
            if (value is DefaultCharacter.Max) continue;

            // 缺字段会静默落到默认档 Roleplay:智能体会变成读不了文件的聊天角色,
            // 工具人会凭空长出开场白与用户卡开关。所以每张内置卡都必须自己写明档位
            string json = EmbeddedResourcesUtils.Read(value + ".json");
            Assert.Contains("\"Kind\"", json);
        }
    }

    [Theory]
    [InlineData(DefaultCharacter.UiharuKazari, ECharacterKind.Roleplay)]
    [InlineData(DefaultCharacter.WorkspaceAgent, ECharacterKind.Agent)]
    [InlineData(DefaultCharacter.UserCard, ECharacterKind.UserCard)]
    [InlineData(DefaultCharacter.Translator, ECharacterKind.Tool)]
    [InlineData(DefaultCharacter.Assistant, ECharacterKind.Tool)]
    public void BuiltInCharacters_LandOnTheirIntendedKind(DefaultCharacter character, ECharacterKind expected)
    {
        Assert.Equal(expected, DefaultCharacterManager.Instance.GetCharacterData(character).Kind);
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
    /// 每个能开会话的档位都必须<b>恰好</b>落进一边：聊天页或智能体页。
    ///
    /// 这条是实机踩出来的：装配分支曾写作 <c>== Roleplay</c>、聊天页会话列表曾写作
    /// <c>GetSessions(Roleplay)</c>——两档时代"非扮演即 agent"成立，四档之后工具人两处都漏：
    /// 翻译/识图角色被装上文件、shell、技能与整套 harness，它们的会话则在两个页面都不显示。
    /// 加第五档时这条会立刻炸，而不是等实机发现。
    /// </summary>
    [Fact]
    public void EveryKind_LandsOnExactlyOneSurface()
    {
        foreach (ECharacterKind kind in Enum.GetValues<ECharacterKind>())
        {
            if (!kind.CanStartSession()) continue; //用户卡不开会话

            Assert.True(kind.IsChat() ^ kind.IsAgent(), $"{kind} 没有归页或同时归了两页");
        }
    }

    /// <summary>
    /// 只有智能体档走 agent 装配。工具人是"一段纯提示词干一件事"，
    /// 给它挂上工具与工作目录就是白吃一大段 harness 前言，还多出一堆它用不到的工具。
    /// </summary>
    [Fact]
    public void OnlyAgentKind_TakesTheAgentAssembly()
    {
        Assert.True(ECharacterKind.Agent.IsAgent());
        Assert.False(ECharacterKind.Tool.IsAgent());
        Assert.False(ECharacterKind.Roleplay.IsAgent());
        Assert.False(ECharacterKind.UserCard.IsAgent());
    }

    [Fact]
    public void Kind_RoundTripsAsReadableString()
    {
        CharacterData original = new() { Kind = ECharacterKind.Agent };

        string json = SaveUtility.SaveToString(original);
        CharacterData restored = SaveUtility.LoadFromString<CharacterData>(json);

        // 存成可读字符串而非数字：内置角色卡是手写的，数字枚举既不可读也易错
        Assert.Contains("\"Agent\"", json);
        Assert.Equal(ECharacterKind.Agent, restored.Kind);
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
