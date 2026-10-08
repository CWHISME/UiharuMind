using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.Tests.Character;

/// <summary>
/// 钉死 <see cref="CharacterData.GetPersonaCoda"/> 的 fallback 链：手写锚点（原样返回）
/// → 人名 + 描述自动拼成 <c>你是…</c> → 无名且无锚点则不发。
/// 第二人称是刻意的：系统提示全篇都是对模型说话的"你"，结尾换第三人称等于换声音。
/// </summary>
public class PersonaCodaTests
{
    
    [Fact]
    public void GetPersonaCoda_PrefersExplicitAnchor()
    {
        CharacterData character = new()
        {
            CharacterName = "晨曦",
            Description = "活泼、有冲劲",
            PersonaAnchor = "你是晨曦。短句，先动手再说。",
        };

        Assert.Equal("你是晨曦。短句，先动手再说。", character.GetPersonaCoda());
    }

    [Fact]
    public void GetPersonaCoda_AnchorWithoutName_StillSent()
    {
        CharacterData character = new() { PersonaAnchor = "短、冷，只说漏洞。" };

        Assert.Equal("短、冷，只说漏洞。", character.GetPersonaCoda());
    }
}
