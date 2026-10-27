using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 通用子代理的默认角色卡（设置页那一项）：
/// 配置了就用那张卡当身份与人格来源，没配置就走内置基座卡。
///
/// 钉住三件事：<b>只认通用档</b>（探索与脑各用各的内置卡）、
/// <b>不该由它担任的卡不认</b>（内部卡、用户卡、不可见的屏蔽卡、已删除的卡）、
/// 以及认不出来时<b>永远降级到内置卡</b>而不是抛——派活路径不能因为一个配置项坏掉。
///
/// 角色库与内置卡都是懒加载单例（<c>Singleton.Instance</c> 在锁内跑完 <c>OnInitialize</c> 才发布），
/// 所以这里<b>不显式调 OnInitialize</b>：<c>CharacterManager</c> 那个实现是「Clear 再逐条 Add」，
/// 手动再跑一次会让并行测试看到半填充的字典（实测全量偶发红过一次）。
/// </summary>
public class SubAgentDefaultCharacterTests
{

    /// <summary>未配置 = 内置基座卡，与加这一项之前逐字一致</summary>
    [Fact]
    public void General_Unconfigured_UsesAnonymousBaseCard()
    {
        AgentSettingConfig config = new();

        Assert.Equal(nameof(DefaultCharacter.AnonymousAgent),
            SubAgentDefaultCharacter.IdentityIdOf(ESubAgentType.General, config));
        Assert.Null(SubAgentDefaultCharacter.Resolve(config));
    }

    /// <summary>配了一张普通角色卡：通用档挂它，人格也从它取</summary>
    [Fact]
    public void General_Configured_UsesThatCard()
    {
        AgentSettingConfig config = new() { DefaultSubAgentCharacterId = "Assistant" };

        Assert.Equal("Assistant", SubAgentDefaultCharacter.IdentityIdOf(ESubAgentType.General, config));
        Assert.Equal("Assistant", SubAgentDefaultCharacter.Resolve(config)?.CharacterId);
    }

    /// <summary>
    /// 探索档与脑不吃这一项：探索档恒定只读，共用一张卡会让用户分不清这次委派能不能改东西；
    /// 脑不挂人格。
    /// </summary>
    [Fact]
    public void ExplorerAndBrain_IgnoreTheDefaultCard()
    {
        AgentSettingConfig config = new() { DefaultSubAgentCharacterId = "Assistant" };

        Assert.Equal(nameof(DefaultCharacter.LegacyExploreAgent),
            SubAgentDefaultCharacter.IdentityIdOf(ESubAgentType.Explorer, config));
        Assert.Equal(nameof(DefaultCharacter.AnonymousAgent),
            SubAgentDefaultCharacter.IdentityIdOf(ESubAgentType.Brain, config));
    }

    /// <summary>内部卡是程序点名的载体，用户没在角色库里见过它——即使配置里写了也不认</summary>
    [Fact]
    public void InternalCard_IsRejected_AndFallsBack()
    {
        AgentSettingConfig config = new() { DefaultSubAgentCharacterId = nameof(DefaultCharacter.TranslationPrompt) };

        Assert.Null(SubAgentDefaultCharacter.Resolve(config));
        Assert.Equal(nameof(DefaultCharacter.AnonymousAgent),
            SubAgentDefaultCharacter.IdentityIdOf(ESubAgentType.General, config));
    }

    /// <summary>卡被删了：配置里留着标识也不认，降级到内置卡</summary>
    [Fact]
    public void MissingCard_IsRejected_AndFallsBack()
    {
        AgentSettingConfig config = new() { DefaultSubAgentCharacterId = "no-such-card" };

        Assert.Null(SubAgentDefaultCharacter.Resolve(config));
        Assert.Equal(nameof(DefaultCharacter.AnonymousAgent),
            SubAgentDefaultCharacter.IdentityIdOf(ESubAgentType.General, config));
    }

    /// <summary>
    /// 屏蔽卡与「能不能当子代理人格」无关：它管的是「内容先不给你看」，解锁后就是一张正常卡。
    /// 判据必须跟着可见性闸门走——硬看 <c>IsShielded</c> 会出现「在列表里选中了它，
    /// 再展开一次就被退回未设置」（实测踩过）。
    ///
    /// 两个状态直接传给纯判据，不去翻全局的解锁标记：那个标记是进程级共享的，
    /// 测试里改它会和并行跑的其它类抢状态（实测偶发红过一次）。
    /// </summary>
    [Fact]
    public void ShieldedCard_IsEligibleOnlyWhileVisible()
    {
        CharacterData arona = CharacterManager.Instance.CharacterDataDictionary["AronaAgent"];

        Assert.True(SubAgentDefaultCharacter.IsEligible(arona, shieldedVisible: true));
        Assert.False(SubAgentDefaultCharacter.IsEligible(arona, shieldedVisible: false));
    }

    /// <summary>内部卡（程序点名的载体）与用户卡（「我是谁」单例）无论可见性如何都不认</summary>
    [Fact]
    public void InternalAndUserCards_AreNeverEligible()
    {
        CharacterData internalCard = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.TranslationPrompt);
        CharacterData userCard = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.UserCard);

        Assert.False(SubAgentDefaultCharacter.IsEligible(internalCard, shieldedVisible: true));
        Assert.False(SubAgentDefaultCharacter.IsEligible(userCard, shieldedVisible: true));
    }

    /// <summary>空串与空白都算未设置</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_CountsAsUnset(string characterId)
    {
        AgentSettingConfig config = new() { DefaultSubAgentCharacterId = characterId };

        Assert.Null(SubAgentDefaultCharacter.Resolve(config));
    }
}
