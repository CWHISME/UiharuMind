using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 角色库导航列表的筛选设置。
///
/// 这里钉的两条都是<b>静默失效</b>的那类：一条错了，老用户的筛选档会悄悄变成另一档
/// （下标数组换成枚举时的经典事故——界面上看不出坏了，只是筛出来的东西不对了）；
/// 另一条错了，改了名字的字段会让存量角色卡读不出「是否内置」，
/// 于是内置卡与用户卡在来源筛选里彻底分不开，而这两件事都不报错。
/// </summary>
public class CharacterListFilterTests
{
    /// <summary>
    /// 枚举值必须等于旧版下标数组的下标。旧设置里存的就是那个整数，
    /// 换枚举后同一个数字得仍指同一档，否则升级即改掉用户已选的筛选。
    /// </summary>
    [Theory]
    [InlineData(ECharacterKindFilter.All, 0)]
    [InlineData(ECharacterKindFilter.Chat, 1)]
    [InlineData(ECharacterKindFilter.Agent, 2)]
    public void KindFilterValues_MatchTheLegacyPositionalArray(ECharacterKindFilter filter, int legacyIndex)
    {
        Assert.Equal(legacyIndex, (int)filter);
    }

    /// <summary>老设置文件读进来仍是原来那一档，不必迁移</summary>
    [Fact]
    public void LegacySettingIndex_StillSelectsTheSameRow()
    {
        SettingConfig setting = SaveUtility.LoadFromString<SettingConfig>("{\"CharacterFilterIndex\":2}");

        Assert.Equal(ECharacterKindFilter.Agent, setting.CharacterKindFilter);
        // 新轴没有旧值可继承，缺省就是不过滤
        Assert.Equal(ECharacterOriginFilter.All, setting.CharacterOriginFilter);
    }

    /// <summary>
    /// 落盘键仍是 <c>IsDefaultCharacter</c>：属性改名不是存档格式变更，
    /// 存量角色卡必须继续读成同一个值。
    /// </summary>
    [Fact]
    public void BuiltInFlag_RoundTripsUnderItsLegacyJsonKey()
    {
        CharacterData builtIn = SaveUtility.LoadFromString<CharacterData>("{\"IsDefaultCharacter\":true}");
        Assert.True(builtIn.IsBuiltIn);

        string json = SaveUtility.SaveToString(new CharacterData { IsBuiltIn = true });
        Assert.Contains("\"IsDefaultCharacter\"", json);
        Assert.DoesNotContain("\"IsBuiltIn\"", json);
        Assert.True(SaveUtility.LoadFromString<CharacterData>(json).IsBuiltIn);
    }

    /// <summary>
    /// 每张内置卡都算内置，用户建的卡都不算——来源筛选全靠这一条判。
    /// 复制一张内置卡出来的是用户卡（新 GUID），不能跟着算内置。
    /// </summary>
    [Fact]
    public void LoadedBuiltInCards_AreAllMarkedBuiltIn()
    {
        DefaultCharacterManager.Instance.OnInitialize();

        Assert.NotEmpty(DefaultCharacterManager.Instance.All);
        foreach ((string id, CharacterData data) in DefaultCharacterManager.Instance.All)
            Assert.True(data.IsBuiltIn, $"{id} 是内置卡，却没标上内置");
    }

    /// <summary>
    /// 搜索词空着或纯空白一律命中（调用方不必先判空），
    /// 有词时名字与描述都比、大小写不敏感。
    /// </summary>
    [Fact]
    public void MatchesSearch_HitsOnNameOrDescription_CaseInsensitively()
    {
        CharacterData card = new() { CharacterName = "一方通行", Description = "审核者（逻辑/可行性）" };

        Assert.True(card.MatchesSearch(null));
        Assert.True(card.MatchesSearch(""));
        Assert.True(card.MatchesSearch("   "));
        Assert.True(card.MatchesSearch("一方"));
        Assert.True(card.MatchesSearch("审核者")); //只在描述里
        Assert.True(card.MatchesSearch("审核者（逻辑"));
        Assert.True(card.MatchesSearch("通行".ToUpperInvariant()));
        Assert.False(card.MatchesSearch("食蜂"));
    }
}
