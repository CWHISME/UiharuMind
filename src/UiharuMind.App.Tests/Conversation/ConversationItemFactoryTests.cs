using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Features.Conversation.Composer;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 用户气泡构造点的显示文本规则（<see cref="ConversationItemFactory.UserMessageDisplayText"/>）。
/// </summary>
public class ConversationItemFactoryTests
{
    /// <summary>
    /// 拖拽/选文件发送：消息正文是「[Attached file: 路径]」引用。显示文本必须取来源消息
    /// ——与重开会话的历史回放同一副面孔——而不是乐观显示时传进来的输入框原文（拖文件后是空串）。
    /// 回归：实时气泡曾因此不显示附件路径，重开会话才看得到。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("看看这份文件")]
    public void UserMessageDisplayText_PrefersSourceText_OverTypedText(string typed)
    {
        ChatMessage message = new(ChatRole.User, $"{typed}\n[Attached file: /tmp/notes.txt]");

        // 模拟实时发送的乐观路径:第一个参数是输入框原文,来源消息才含有附件路径引用
        string display = ConversationItemFactory.UserMessageDisplayText(typed, message);

        Assert.Contains("[Attached file: /tmp/notes.txt]", display);
    }

    /// <summary>点名调用:正文是注入的技能全文,气泡仍只显示用户敲的那一行,不能被消息正文顶掉</summary>
    [Fact]
    public void UserMessageDisplayText_NamedInvocation_PrefersTypedInput()
    {
        ChatMessage message = new(ChatRole.User, "/readme 技能全文注入内容...");
        NamedSkillAnnotations.Mark(message, new SkillInvocation { SkillName = "readme", InjectedText = "注入全文" }, "/readme");

        Assert.Equal("/readme", ConversationItemFactory.UserMessageDisplayText("/readme", message));
    }

    /// <summary>记忆清单只挂在注解上（ADR 0076）：气泡仍是用户原话，清单折进注入区备查</summary>
    [Fact]
    public void ApplyInjected_MemorySnapshot_FoldsTheListBesideTheTypedText()
    {
        ChatMessage message = new(ChatRole.User, "开始干活");
        message.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ChatMessageAnnotations.MemoryIndexSnapshot] = "- a.md — 偏好\n- b.md — 踩坑",
        };
        TextConversationItem item = new(true);

        ConversationItemFactory.ApplyInjected(item, message);

        Assert.Equal("开始干活", ConversationItemFactory.UserMessageDisplayText("开始干活", message));
        Assert.Contains("- b.md — 踩坑", item.InjectedText);
        Assert.Contains("2", item.InjectedLabel);
    }
}