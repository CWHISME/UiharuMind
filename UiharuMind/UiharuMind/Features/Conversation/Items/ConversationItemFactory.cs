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
using UiharuMind.Generated;
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
    /// <summary>气泡上那一行时间的格式,只此一处定义;当天只给时分,隔天补上日期</summary>
    /// <param name="at">时刻</param>
    /// <returns>显示文本</returns>
    public static string TimestampText(DateTimeOffset at)
    {
        DateTime local = at.LocalDateTime;
        return local.Date == DateTime.Now.Date
            ? local.ToString("HH:mm")
            : local.ToString("yyyy/MM/dd HH:mm");
    }

    /// <summary>条目与标题用的显示文本:点名调用取用户敲的那一行,其余取消息正文</summary>
    /// <param name="message">消息</param>
    /// <returns>显示文本</returns>
    public static string DisplayTextOf(ChatMessage message) =>
        NamedSkillAnnotations.InputOf(message) ?? message.Text;

    /// <summary>
    /// 用户气泡的显示文本。以来源消息为准：点名调用取用户敲的那一行，其余取消息正文
    /// （正文里包含附件转成的路径引用）。调用方传入的敲入文本只在来源没有正文时兜底——
    /// 实时乐观气泡与历史回放共用这一条规则，同一条 <c>ChatMessage</c> 才不会
    /// 「实时一张脸、重开另一张脸」（拖文件发送的附件引用曾只在重开会话时出现）。
    /// </summary>
    /// <param name="typedText">输入框原文；拖文件未打字时为空串</param>
    /// <param name="source">来源消息；尚未构造时为 null</param>
    /// <returns>显示文本</returns>
    public static string UserMessageDisplayText(string typedText, ChatMessage? source)
    {
        if (source == null) return typedText;
        if (NamedSkillAnnotations.InputOf(source) is { } typedLine) return typedLine;
        return source.Text ?? typedText;
    }

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
            Message = UserMessageDisplayText(text, source),
            SenderName = Loc.Text(LangKey.AgentSenderUser),
            SenderColor = Avalonia.Media.Brushes.LightGreen,
            Icon = IconUtils.DefaultUserIcon,
            Timestamp = TimestampText(source?.CreatedAt ?? DateTimeOffset.Now),
        };

        // 点名调用:显示文本已由 UserMessageDisplayText 定为用户敲的那一行,
        // 消息正文是注入的技能全文,折叠起来备查
        if (source != null && NamedSkillAnnotations.InputOf(source) is { } typedLine)
        {
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

    /// <summary>
    /// 旁白条目（开场白）。不带头像与名字，因此也不需要知道是哪个角色——
    /// 它是场景描述，不是「某某在说话」
    /// </summary>
    /// <param name="message">源消息</param>
    /// <returns>条目</returns>
    public static TextConversationItem CreateNarration(ChatMessage message) =>
        new(false, true)
        {
            Message = message.Text,
            Timestamp = TimestampText(message.CreatedAt ?? DateTimeOffset.Now),
        };

    /// <summary>消息里是否带图片(回放时据此决定空文本的用户消息要不要渲染)</summary>
    /// <param name="message">消息</param>
    /// <returns>是否带图</returns>
    public static bool HasImage(ChatMessage message) =>
        message.Contents.OfType<DataContent>().Any(x => x.HasTopLevelMediaType("image"));

    /// <summary>
    /// 识别非真实用户输入的 user 角色消息:框架上下文提供器注入的消息
    /// (todo 快照、模式切换通知等)带 _attribution 溯源标记;审批回应为控制消息。
    /// 它们是模型上下文的一部分(持久化属正常),但不应渲染为用户气泡。
    /// 点名调用(/技能名)是例外:它明确定义为要落盘 + 渲染成折叠气泡,即便历史副本
    /// 被框架回灌时盖上了 _attribution,也不该被当成框架注入滤掉。
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>是否为框架注入</returns>
    public static bool IsFrameworkInjected(ChatMessage message)
    {
        // 点名调用是用 _namedSkill 兜底的(见 NamedSkillAnnotations.Mark),不受 _attribution 屏蔽
        if (NamedSkillAnnotations.InputOf(message) != null) return false;
        if (message.AdditionalProperties?.ContainsKey(ChatMessageAnnotations.Attribution) == true) return true;
        return message.Contents.Any(x => x is ToolApprovalResponseContent);
    }

    /// <summary>
    /// 知识库检索卡片。复用 <see cref="ToolCallItem"/> 而不是新开一种条目：
    /// 注入路径与 <c>KnowledgeSearch</c> 工具路径展示的是同一件事，
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
