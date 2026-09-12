using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// <c>IsFrameworkInjected</c> 的契约：区分「框架注入」与「真实用户消息」。
/// 点名调用(/技能名)即使历史副本被框架回灌盖上了 _attribution，也是真实用户输入，
/// 必须渲染成折叠气泡，不能被 _attribution 误杀——见 bug 记录（首条点名调用拉到头不可见）。
/// </summary>
public class ConversationItemFactoryInjectionTests
{
    private static ChatMessage AttributedUser(string text) => new(ChatRole.User, text)
    {
        AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ChatMessageAnnotations.Attribution] = "framework",
        },
    };

    private static ChatMessage NamedSkillWithAttribution(string input, string skillName) =>
        new(ChatRole.User, $"# Skill: {skillName}\nThe user invoked this skill explicitly.")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.Attribution] = "framework",
                [ChatMessageAnnotations.NamedSkill] = skillName,
                [ChatMessageAnnotations.NamedSkillInput] = input,
            },
        };

    /// <summary>普通框架注入（todo 快照等）仍被识别为注入</summary>
    [Fact]
    public void PlainAttributedMessage_IsFrameworkInjected()
    {
        Assert.True(ConversationItemFactory.IsFrameworkInjected(AttributedUser("[todo 快照]")));
    }

    /// <summary>点名调用消息带 _attribution 也不该被当成注入——正文必须常驻并渲染</summary>
    [Fact]
    public void NamedSkillMessage_EvenWithAttribution_IsNotInjected()
    {
        ChatMessage message = NamedSkillWithAttribution("/wayfinder Design/maps/11-core-tiers/map.md", "wayfinder");
        Assert.False(ConversationItemFactory.IsFrameworkInjected(message));
    }
}