using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 钉住开场白的显式注入：角色页「开始对话」下拉选了哪份开场白，
/// 就必须以哪份作为会话旁白与副标题；null 回落卡上默认。
/// </summary>
public class ChatSessionGreetingTests
{
    private static CharacterData CharacterWithGreetings() => new()
    {
        CharacterId = "c",
        CharacterName = "A",
        FirstGreeting = "默认开场白",
        AlternateGreetings = ["备选一", "备选二"],
    };

    [Fact]
    public void Ctor_ExplicitGreeting_UsesItAsNarrationAndDescription()
    {
        ChatSession session = new("t", CharacterWithGreetings(), "备选一");

        Assert.Equal("备选一", session.Description);
        ChatMessage first = Assert.Single(session.History);
        Assert.Equal(ChatRole.Assistant, first.Role);
        Assert.Equal("备选一", first.Text);
        Assert.Equal(true, first.AdditionalProperties?[ChatMessageAnnotations.Narration]);
    }

    [Fact]
    public void Ctor_NoExplicitGreeting_FallsBackToFirstGreeting()
    {
        ChatSession session = new("t", CharacterWithGreetings());

        Assert.Equal("默认开场白", session.Description);
        Assert.Equal("默认开场白", Assert.Single(session.History).Text);
    }

    [Fact]
    public void AddNarration_ExplicitGreeting_RendersParams()
    {
        ChatSession session = new("t", new CharacterData { CharacterId = "x", CharacterName = "X" });
        CharacterData character = CharacterWithGreetings();

        session.AddNarration(character, "你好，{{$char}}");

        Assert.Equal("你好，A", session.History[^1].Text);
    }
}