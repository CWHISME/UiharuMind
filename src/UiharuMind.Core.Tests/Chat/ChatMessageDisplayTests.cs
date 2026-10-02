using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// <c>IsFrameworkInjected</c> 的契约：区分「框架注入」与「真实用户消息」。
/// 点名调用(/技能名)即使历史副本被框架回灌盖上了 _attribution，也是真实用户输入，
/// 必须渲染成折叠气泡，不能被 _attribution 误杀——见 bug 记录（首条点名调用拉到头不可见）。
/// </summary>
public class ChatMessageDisplayTests
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
        Assert.True(ChatMessageDisplay.IsFrameworkInjected(AttributedUser("[todo 快照]")));
    }

    /// <summary>点名调用消息带 _attribution 也不该被当成注入——正文必须常驻并渲染</summary>
    [Fact]
    public void NamedSkillMessage_EvenWithAttribution_IsNotInjected()
    {
        ChatMessage message = NamedSkillWithAttribution("/wayfinder Design/maps/11-core-tiers/map.md", "wayfinder");
        Assert.False(ChatMessageDisplay.IsFrameworkInjected(message));
    }

    /// <summary>审批回应是控制消息，不画成用户气泡</summary>
    [Fact]
    public void ApprovalResponse_IsFrameworkInjected()
    {
        Assert.True(ChatMessageDisplay.IsFrameworkInjected(ApprovalResponse("好的，同意")));
    }

    /// <summary>落盘往返后点名输入是 JsonElement，读出来仍是用户敲的那一行</summary>
    [Fact]
    public void NamedSkillInput_SurvivesRoundTrip()
    {
        ChatMessage message = NamedSkillWithAttribution("/readme", "readme");
        message.AdditionalProperties![ChatMessageAnnotations.NamedSkillInput] =
            System.Text.Json.JsonDocument.Parse("\"/readme\"").RootElement.Clone();

        Assert.Equal("/readme", ChatMessageDisplay.TextOf(message));
    }

    internal static ChatMessage ApprovalResponse(string text) => new(ChatRole.User,
    [
        new TextContent(text),
        new ToolApprovalRequestContent("approval-1", new FunctionCallContent("call-1", "write_file")).CreateResponse(approved: true),
    ]);
}
