using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 钉死 <see cref="CharacterData.GetPersonaCoda"/> 的推导规则：人名 + 描述自动拼成
/// <c>你是…</c>，不用填字段。描述本就是定位语，无名则不发。
/// 第二人称是刻意的：系统提示全篇都是对模型说话的"你"，结尾换第三人称等于换声音。
/// </summary>
public class PersonaCodaTests
{
    [Fact]
    public void GetPersonaCoda_JoinsNameAndDescription()
    {
        CharacterData character = new() { CharacterName = "晨曦", Description = "活泼、有冲劲" };

        Assert.Equal("你是晨曦，活泼、有冲劲", character.GetPersonaCoda());
    }

    [Fact]
    public void GetPersonaCoda_NameOnly_WithoutDescription()
    {
        CharacterData character = new() { CharacterName = "晨曦", Description = "" };

        Assert.Equal("你是晨曦。", character.GetPersonaCoda());
    }

    [Fact]
    public void GetPersonaCoda_Empty_WithoutName()
    {
        CharacterData character = new() { CharacterName = "", Description = "活泼、有冲劲" };

        Assert.Equal(string.Empty, character.GetPersonaCoda());
    }
}
