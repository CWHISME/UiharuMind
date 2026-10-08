using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 给会话里的文本气泡接上朗读，并把朗读服务的状态映回正在念的那一条；按说话的角色取声音。
/// 开了「新回复自动朗读」的角色，新落盘的回复排队念（连着几条按顺序，被停就不再往下念）。
/// 朗读不改历史，所以不和编辑 / 删除那组动作放在一起；条目进集合即接，不等一轮结束
/// </summary>
public sealed class ConversationReadAloud : IDisposable
{
    private readonly ObservableCollection<ConversationItemBase> _items;
    private readonly IReadAloudService _service;
    private readonly CharacterSpeechStyles? _styles;
    private readonly Queue<(object Owner, string Text, SpeechStyle? Style)> _pending = new(); //等着自动念的
    private ConversationItemBase? _active; //上次标成在念的那一条
    private TaskCompletionSource? _idle; //等别处的朗读念完
    private object? _autoOwner; //正在自动念的那一条
    private bool _isDraining;

    /// <param name="items">界面条目集合（与视图模型共用）</param>
    /// <param name="service">朗读服务</param>
    /// <param name="styles">角色的声音与自动朗读开关（念时现取，改了角色声音旧消息也跟着变）；null 一律用默认、不自动念</param>
    public ConversationReadAloud(ObservableCollection<ConversationItemBase> items, IReadAloudService service,
        CharacterSpeechStyles? styles = null)
    {
        _items = items;
        _service = service;
        _styles = styles;
        foreach (ConversationItemBase item in items) Attach(item);
        items.CollectionChanged += OnItemsChanged;
        service.StateChanged += OnStateChanged;
        OnStateChanged();
    }

    public void Dispose()
    {
        _items.CollectionChanged -= OnItemsChanged;
        _service.StateChanged -= OnStateChanged;
        _pending.Clear();
    }

    /// <summary>
    /// 历史里新落了消息：说话的角色开了自动朗读的回复排队念。只认助手正文——旁白、后续报告、工具、化身代发都不念
    /// </summary>
    /// <param name="messages">新落盘的消息</param>
    /// <param name="sessionCharacterId">会话的角色（群壳里按每条的发言人）</param>
    public void ReadNew(IEnumerable<ChatMessage> messages, string? sessionCharacterId)
    {
        if (_styles == null) return;
        foreach (ChatMessage message in messages)
        {
            if (message.Role != ChatRole.Assistant || string.IsNullOrWhiteSpace(message.Text) ||
                ConversationMessageOrigin.KindOf(message) != EHistoryItemKind.StreamContents ||
                ChatMessageAnnotations.GroupAvatarPostOf(message) != null) continue;
            string? speaker = ChatMessageAnnotations.GroupSpeakerOf(message) ?? sessionCharacterId;
            if (!_styles.ReadsAloud(speaker)) continue;
            // 有气泡就让气泡显示「在念」；还没配上就拿消息本身当来源
            object owner = _items.OfType<TextConversationItem>().LastOrDefault(x => ReferenceEquals(x.SourceMessage, message))
                           ?? (object)message;
            _pending.Enqueue((owner, message.Text, _styles.For(speaker)));
        }

        if (_pending.Count > 0 && !_isDraining) _ = DrainAsync();
    }

    /// <summary>
    /// 不再自动念：丢掉排着的，正在自动念的也停（手点的不动）
    /// </summary>
    public void StopAuto()
    {
        _pending.Clear();
        if (_autoOwner != null && ReferenceEquals(_service.Owner, _autoOwner)) _service.Stop();
    }

    // 别处在念（手点的、试听）就等它念完；自动念的被停了，后面排着的也不念
    private async Task DrainAsync()
    {
        _isDraining = true;
        try
        {
            while (_pending.TryDequeue(out (object Owner, string Text, SpeechStyle? Style) next))
            {
                if (_service.State != EReadAloudState.Idle)
                {
                    _idle = new TaskCompletionSource();
                    await _idle.Task;
                }

                _autoOwner = next.Owner;
                bool finished = await _service.ToggleAsync(next.Owner, next.Text, next.Style);
                _autoOwner = null;
                if (!finished) _pending.Clear();
            }
        }
        finally
        {
            _isDraining = false;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ConversationItemBase item in e.NewItems?.OfType<ConversationItemBase>() ?? [])
        {
            Attach(item);
            if (ReferenceEquals(item, _service.Owner)) item.ReadAloudState = _service.State;
        }
    }

    private void Attach(ConversationItemBase item)
    {
        if (item is TextConversationItem)
            item.ReadAloudCallback = x => _service.ToggleAsync(x, x.Message, _styles?.For(x.CharacterId));
    }

    private void OnStateChanged()
    {
        if (_active != null && !ReferenceEquals(_active, _service.Owner)) _active.ReadAloudState = EReadAloudState.Idle;
        _active = _service.Owner as ConversationItemBase;
        if (_active != null && _items.Contains(_active)) _active.ReadAloudState = _service.State;
        else _active = null;
        if (_service.State == EReadAloudState.Idle) _idle?.TrySetResult();
    }
}
