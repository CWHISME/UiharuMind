/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Linq;
using System;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 消息 → 气泡条目。实时流与历史回放共用这一处构造，
/// 「同一条消息两副面孔」那类不一致只有在唯一的构造点才消得掉。
/// </summary>
public static class ConversationItemFactory
{
    /// <summary>气泡上那一行时间的格式,只此一处定义</summary>
    /// <param name="at">时刻</param>
    /// <returns>显示文本</returns>
    public static string TimestampText(DateTimeOffset at) => at.LocalDateTime.ToString("HH:mm");

    /// <summary>条目与标题用的显示文本:点名调用取用户敲的那一行,其余取消息正文</summary>
    /// <param name="message">消息</param>
    /// <returns>显示文本</returns>
    public static string DisplayTextOf(ChatMessage message) =>
        NamedSkillAnnotations.InputOf(message) ?? message.Text;

    /// <summary>
    /// 用户气泡
    /// </summary>
    /// <param name="text">显示文本</param>
    /// <param name="source">来源消息;实时发送尚未落历史时为 null</param>
    /// <param name="attachments">本轮附件;非视觉模型下气泡靠它回落显示图片</param>
    /// <returns>条目</returns>
    public static TextConversationItem CreateUser(string text, ChatMessage? source = null,
        List<ConversationAttachment>? attachments = null)
    {
        TextConversationItem item = new(true)
        {
            Message = text,
            SenderName = LocalizationManager.Instance.GetString("AgentSenderUser"),
            SenderColor = Avalonia.Media.Brushes.LightGreen,
            Icon = IconUtils.DefaultUserIcon,
            Timestamp = TimestampText(source?.CreatedAt ?? DateTimeOffset.Now),
        };

        // 点名调用:消息正文是注入的技能全文,气泡只显示用户敲的那一行,正文折叠备查
        if (source != null && NamedSkillAnnotations.InputOf(source) is { } typedLine)
        {
            item.Message = typedLine;
            item.InjectedText = source.Text;
        }

        // 优先显示**真正发出去的那一份**(消息里的 DataContent):它是缩放重编码之后的结果,
        // 界面因此所见即所得——模型看到什么,你就看到什么。
        // 这同时消掉了一处不一致:原先实时发送显示原图、重载会话后显示压缩图,同一条消息两副面孔
        // 一条消息可以带多张图,全都要显示——只取第一张的话,一次发四张图气泡里就只剩一张
        List<DataContent> images = source?.Contents
            .OfType<DataContent>()
            .Where(x => x.HasTopLevelMediaType("image"))
            .ToList() ?? [];
        if (images.Count > 0)
        {
            foreach (DataContent image in images) item.AddImage(image.Data);
            return item;
        }

        // 没内联字节的情况:非视觉模型下 BuildUserMessage 把附件降级成了文本引用。
        // 但用户附了图就该在界面上看到,与模型能否看图无关,所以回落到附件本身
        if (attachments == null) return item;
        foreach (ConversationAttachment attached in attachments.Where(x => x.IsImage))
        {
            item.AddImage(AttachmentTrayViewData.ReadAttachmentBytes(attached));
        }

        return item;
    }

    /// <summary>助手条目:名字与头像取自当前会话的角色</summary>
    /// <param name="character">当前会话角色;无会话时为 null</param>
    /// <returns>条目</returns>
    public static TextConversationItem CreateAssistant(CharacterData? character)
    {
        return new TextConversationItem(false)
        {
            SenderName = string.IsNullOrEmpty(character?.CharacterName)
                ? "Agent"
                : character!.CharacterName,
            SenderColor = Avalonia.Media.Brushes.DeepSkyBlue,
            Icon = character == null
                ? IconUtils.DefaultCharIcon
                : IconUtils.GetCharacterBitmapOrDefault(character),
            //流式产出的壳:此刻确实就是现在。回放历史时由 BuildHistoryItems 按源消息校准
            Timestamp = TimestampText(DateTimeOffset.Now),
            IsDone = false,
        };
    }

    /// <summary>消息里是否带图片(回放时据此决定空文本的用户消息要不要渲染)</summary>
    /// <param name="message">消息</param>
    /// <returns>是否带图</returns>
    public static bool HasImage(ChatMessage message) =>
        message.Contents.OfType<DataContent>().Any(x => x.HasTopLevelMediaType("image"));

    /// <summary>
    /// 识别非真实用户输入的 user 角色消息:框架上下文提供器注入的消息
    /// (todo 快照、模式切换通知等)带 _attribution 溯源标记;审批回应为控制消息。
    /// 它们是模型上下文的一部分(持久化属正常),但不应渲染为用户气泡。
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是否为框架注入</returns>
    public static bool IsFrameworkInjected(ChatMessage message)
    {
        if (message.AdditionalProperties?.ContainsKey(ChatMessageAnnotations.Attribution) == true) return true;
        return message.Contents.Any(x => x is ToolApprovalResponseContent);
    }

    /// <summary>
    /// 知识库检索卡片。复用 <see cref="ToolCallItem"/> 而不是新开一种条目：
    /// 注入路径与 <c>knowledge_search</c> 工具路径展示的是同一件事，
    /// 长成两种样子只会让「换个后端界面就变了」，而工具那条路已经是这张卡。
    /// </summary>
    /// <param name="snippets">片段全文</param>
    /// <returns>已完成态的工具卡片，默认折叠</returns>
    public static ToolCallItem CreateKnowledgeCard(string snippets) => new()
    {
        ToolName = KnowledgeTool.ToolName,
        IconGlyph = "🔍",
        IsRunning = false,
        IsSuccess = true,
        ResultText = snippets,
    };
}
