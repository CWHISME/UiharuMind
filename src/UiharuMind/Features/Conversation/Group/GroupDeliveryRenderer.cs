using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 成员会话里的群投递按发言人画（ADR 0046 代价第二条、待议「群投递按角色渲染」）：
/// 一条合成的 user 消息拆成各人的气泡——成员画左侧（头像 + 名字），用户的话画右侧，
/// 场景说明与主持人提示画旁白。只是呈现轴，存储与供给一字不动。
///
/// 不拆的话整条按用户的 markdown 画：单行的 <c>[名字]: 内容</c> 会被当成链接引用定义整段吞掉，
/// 气泡空白、复制却有字。
/// </summary>
public sealed class GroupDeliveryRenderer
{
    private const string ScenePrefix = "（这是群聊「"; //首次投递以场景说明开头：带标记之前的旧投递靠它认

    private readonly Dictionary<string, CharacterData> _speakers; //显示名 → 角色（成员与用户）
    private readonly string _userName;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="speakers">显示名 → 角色（群里其他成员与用户）</param>
    /// <param name="userName">用户的显示名</param>
    public GroupDeliveryRenderer(string memberSessionId, Dictionary<string, CharacterData> speakers, string userName)
    {
        SessionId = memberSessionId;
        _speakers = speakers;
        _userName = userName;
    }

    /// <summary>为哪个成员会话画</summary>
    public string SessionId { get; }

    /// <summary>
    /// 给某个成员会话建一个渲染器；它不是群成员会话（或群已删）为 null
    /// </summary>
    /// <param name="session">当前会话</param>
    /// <returns>渲染器</returns>
    public static GroupDeliveryRenderer? For(ChatSession? session)
    {
        if (session is not { IsGroupMember: true, GroupId: { } groupId }) return null;
        if (SessionManager.Instance.Load(groupId) is not { IsGroup: true } group) return null;

        Dictionary<string, CharacterData> speakers = new();
        // 退群的人也算：他之前的发言还在投递里
        foreach (GroupRosterMember member in GroupRoster.Of(group).Everyone)
        {
            speakers.TryAdd(member.Name, member.Character);
        }

        CharacterData user = CharacterManager.Instance.UserCharacterData;
        string userName = CharacterManager.Instance.UserCharacterName;
        speakers.TryAdd(userName, user);
        return new GroupDeliveryRenderer(session.SessionId, speakers, userName);
    }

    /// <summary>
    /// 这条要不要按投递拆：带投递标记的一律拆；没标记的旧消息只在以场景说明或已知发言人开头时拆
    /// （私聊里用户自己打的话不会长这样）
    /// </summary>
    /// <param name="message">成员会话里的 user 消息</param>
    /// <returns>要拆为 true</returns>
    public bool IsDelivery(ChatMessage message)
    {
        if (ChatMessageAnnotations.IsGroupDelivery(message)) return true;

        string text = message.Text.TrimStart();
        return text.StartsWith(ScenePrefix, StringComparison.Ordinal)
               || GroupTranscript.SplitDelivery(text, _speakers.Keys).FirstOrDefault().Speaker != null;
    }

    /// <summary>
    /// 拆成各人的气泡（未接来源，调用方负责 Wire 与时间戳）
    /// </summary>
    /// <param name="message">投递</param>
    /// <returns>气泡，按原顺序</returns>
    public IReadOnlyList<TextConversationItem> Render(ChatMessage message)
    {
        string timestamp = ConversationItemFactory.TimestampText(message.CreatedAt ?? DateTimeOffset.Now);
        List<TextConversationItem> items = [];
        foreach (GroupDeliverySegment segment in GroupTranscript.SplitDelivery(message.Text, _speakers.Keys))
        {
            TextConversationItem item = segment.Speaker switch
            {
                null => new TextConversationItem(false, true) { Message = segment.Body },
                _ when segment.Speaker == _userName => ConversationItemFactory.CreateUser(segment.Body),
                _ => SpeakerItem(segment.Speaker, segment.Body),
            };
            item.Timestamp = timestamp;
            items.Add(item);
        }

        return items;
    }

    private TextConversationItem SpeakerItem(string speaker, string body)
    {
        TextConversationItem item = ConversationItemFactory.CreateAssistant(_speakers.GetValueOrDefault(speaker));
        item.Message = body;
        item.IsDone = true;
        return item;
    }
}
