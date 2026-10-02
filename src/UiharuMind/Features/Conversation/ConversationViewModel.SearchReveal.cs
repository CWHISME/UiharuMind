/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using UiharuMind.Features.Conversation.Search;

namespace UiharuMind.Features.Conversation;

public partial class ConversationViewModel
{
    private ConversationSearchReveal? _pendingSearchReveal;

    /// <summary>有一条跨会话搜索的跳转在等视图接手（视图已就位时当场接）</summary>
    public event Action? SearchRevealRequested;

    /// <summary>
    /// 请视图在落位之后打开会话内搜索并跳到这一条。
    ///
    /// 只记下、不当场跳：点结果那一刻多半刚换实例，视图还没换绑过来、会话可能还在装载，
    /// 跳转事件发出去没人接。视图在落位（装载完成、切回缓存实例）时取走，已就位则收到通知当场取
    /// </summary>
    /// <param name="reveal">要跳的那条</param>
    public void RequestSearchReveal(ConversationSearchReveal reveal)
    {
        _pendingSearchReveal = reveal;
        SearchRevealRequested?.Invoke();
    }

    /// <summary>有一条在等视图接手</summary>
    public bool HasPendingSearchReveal => _pendingSearchReveal != null;

    /// <summary>取走待跳的那条（取一次就清掉）</summary>
    /// <returns>待跳的那条；没有为 null</returns>
    public ConversationSearchReveal? TakeSearchReveal()
    {
        ConversationSearchReveal? reveal = _pendingSearchReveal;
        _pendingSearchReveal = null;
        return reveal;
    }

    // 切走了:那次装载可能被中途放下(欠账留到切回来),留着这条的话,下回从列表正常点开也会弹搜索栏跳过去
    private void ForgetSearchReveal() => _pendingSearchReveal = null;
}
