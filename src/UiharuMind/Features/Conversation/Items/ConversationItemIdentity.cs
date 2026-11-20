/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 会话条目的<b>稳定身份</b>：一条历史消息的某个内容块，在界面上至多对应一个条目。
///
/// <para>
/// 从前判重靠三样都不稳的东西：对象引用（<c>SessionManager</c> 冷卸载会换实例）、
/// 显示文本（思考卡是截断预览、知识卡正文在 <c>ResultText</c>、图片气泡不含图）、
/// 以及「按角色与段长猜」。三者都会失配，失配的两个方向就是两种 bug——猜漏是重复显示，
/// 猜过是真消息被吞。
/// </para>
/// <para>
/// 这里换成消息自带的标识（<see cref="ChatMessage.MessageId"/>，见 <see cref="ChatMessageIdentity"/>）
/// 加内容块序号。流式阶段消息还没落盘、拿不到标识，用 <see cref="TemporaryKey"/> 占位，
/// 落盘配对时 <see cref="Rebind"/> 换成正式身份——条目对象本身不动，界面因此不会闪。
/// </para>
/// </summary>
public readonly struct ConversationItemIdentity : IEquatable<ConversationItemIdentity>
{
    private readonly string? _messageId; //正式身份：历史消息的标识
    private readonly string? _temporaryKey; //临时身份：流式阶段还没有消息标识时用

    private ConversationItemIdentity(string? messageId, string? temporaryKey, int contentOrdinal,
        EConversationItemKind kind)
    {
        _messageId = messageId;
        _temporaryKey = temporaryKey;
        ContentOrdinal = contentOrdinal;
        Kind = kind;
    }

    /// <summary>内容块在消息里的序号；同一条消息拆出的同类条目靠它区分</summary>
    public int ContentOrdinal { get; }

    /// <summary>条目种类</summary>
    public EConversationItemKind Kind { get; }

    /// <summary>是否已经拿到正式身份（消息已落盘、标识已知）</summary>
    public bool IsFormal => _messageId != null;

    /// <summary>正式身份的消息标识；临时身份为 null</summary>
    public string? MessageId => _messageId;

    /// <summary>临时身份键；正式身份为 null</summary>
    public string? TemporaryKey => _temporaryKey;

    /// <summary>
    /// 按历史消息建正式身份
    /// </summary>
    /// <param name="message">历史消息（标识缺失时现盖一个）</param>
    /// <param name="kind">条目种类</param>
    /// <param name="contentOrdinal">内容块序号</param>
    /// <returns>正式身份</returns>
    public static ConversationItemIdentity ForMessage(ChatMessage message, EConversationItemKind kind,
        int contentOrdinal = 0) =>
        new(ChatMessageIdentity.Ensure(message), null, contentOrdinal, kind);

    /// <summary>
    /// 按临时键建临时身份（流式阶段：消息还没落盘，标识还不存在）
    /// </summary>
    /// <param name="temporaryKey">临时键，同一轮内唯一</param>
    /// <param name="kind">条目种类</param>
    /// <param name="contentOrdinal">内容块序号</param>
    /// <returns>临时身份</returns>
    public static ConversationItemIdentity Temporary(string temporaryKey, EConversationItemKind kind,
        int contentOrdinal = 0) =>
        new(null, temporaryKey, contentOrdinal, kind);

    /// <summary>
    /// 按<b>内容</b>建身份：给那些「实时与落盘两侧内容同源、但实时侧拿不到消息标识」的条目用
    /// （目前只有知识卡）。
    ///
    /// <para>
    /// 这不是回到「按正文猜」：两侧用的是<b>同一份文本</b>（检索片段由会话本体一次性交给两边），
    /// 所以键相等就是同一份内容，不存在猜错的方向。代价是两轮检索出逐字相同的片段会被合并显示——
    /// 知识卡是纯展示、没有编辑/删除按钮，合并无副作用。
    /// </para>
    /// </summary>
    /// <param name="content">内容全文</param>
    /// <param name="kind">条目种类</param>
    /// <returns>身份</returns>
    public static ConversationItemIdentity ForContent(string content, EConversationItemKind kind) =>
        new(null, "content:" + ContentKey(content), 0, kind);

    /// <summary>内容键：稳定哈希，不依赖字符串驻留或运行时随机化</summary>
    private static string ContentKey(string content)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash, 0, 16);
    }

    /// <summary>
    /// 换成正式身份。<b>只换标识，不动序号与种类</b>——序号与种类是条目自己的属性，
    /// 配对只解决「它属于哪条消息」
    /// </summary>
    /// <param name="message">配对到的历史消息</param>
    /// <returns>正式身份</returns>
    public ConversationItemIdentity Rebind(ChatMessage message) =>
        new(ChatMessageIdentity.Ensure(message), null, ContentOrdinal, Kind);

    /// <summary>判等：正式身份比标识，临时身份比临时键；两者都带序号与种类</summary>
    /// <param name="other">另一个身份</param>
    /// <returns>相等返回 true</returns>
    public bool Equals(ConversationItemIdentity other) =>
        ContentOrdinal == other.ContentOrdinal && Kind == other.Kind &&
        string.Equals(_messageId, other._messageId, StringComparison.Ordinal) &&
        string.Equals(_temporaryKey, other._temporaryKey, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ConversationItemIdentity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_messageId, _temporaryKey, ContentOrdinal, Kind);

    /// <summary>相等运算符</summary>
    public static bool operator ==(ConversationItemIdentity left, ConversationItemIdentity right) =>
        left.Equals(right);

    /// <summary>不等运算符</summary>
    public static bool operator !=(ConversationItemIdentity left, ConversationItemIdentity right) =>
        !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        $"{_messageId ?? "tmp:" + _temporaryKey}#{ContentOrdinal}/{Kind}";
}

/// <summary>
/// 身份的比较器（放进 <see cref="HashSet{T}"/> / <see cref="Dictionary{TKey,TValue}"/> 用）
/// </summary>
public sealed class ConversationItemIdentityComparer : IEqualityComparer<ConversationItemIdentity>
{
    /// <summary>共用实例</summary>
    public static readonly ConversationItemIdentityComparer Instance = new();

    /// <inheritdoc />
    public bool Equals(ConversationItemIdentity x, ConversationItemIdentity y) => x.Equals(y);

    /// <inheritdoc />
    public int GetHashCode(ConversationItemIdentity obj) => obj.GetHashCode();
}
