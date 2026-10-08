using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 给会话里的文本气泡接上朗读，并把朗读服务的状态映回正在念的那一条。
/// 朗读不改历史，所以不和编辑 / 删除那组动作放在一起；条目进集合即接，不等一轮结束
/// </summary>
public sealed class ConversationReadAloud : IDisposable
{
    private readonly ObservableCollection<ConversationItemBase> _items;
    private readonly IReadAloudService _service;
    private readonly Func<string?, SpeechStyle?>? _styleOf;
    private ConversationItemBase? _active; //上次标成在念的那一条

    /// <param name="items">界面条目集合（与视图模型共用）</param>
    /// <param name="service">朗读服务</param>
    /// <param name="styleOf">角色 id → 它的声音（念时现取，改了角色声音旧消息也跟着变）；null 一律用默认</param>
    public ConversationReadAloud(ObservableCollection<ConversationItemBase> items, IReadAloudService service,
        Func<string?, SpeechStyle?>? styleOf = null)
    {
        _items = items;
        _service = service;
        _styleOf = styleOf;
        foreach (ConversationItemBase item in items) Attach(item);
        items.CollectionChanged += OnItemsChanged;
        service.StateChanged += OnStateChanged;
        OnStateChanged();
    }

    public void Dispose()
    {
        _items.CollectionChanged -= OnItemsChanged;
        _service.StateChanged -= OnStateChanged;
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
            item.ReadAloudCallback = x => _service.ToggleAsync(x, x.Message, _styleOf?.Invoke(x.CharacterId));
    }

    private void OnStateChanged()
    {
        if (_active != null && !ReferenceEquals(_active, _service.Owner)) _active.ReadAloudState = EReadAloudState.Idle;
        _active = _service.Owner as ConversationItemBase;
        if (_active != null && _items.Contains(_active)) _active.ReadAloudState = _service.State;
        else _active = null;
    }
}
