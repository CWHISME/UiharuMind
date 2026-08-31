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
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 运行期渲染窗口裁剪：把长会话在<b>直播过程中</b>涨上来的条目按上限裁回去。
///
/// <see cref="HistoryWindow"/> 只在加载会话时开窗，此后 <c>Items</c> 就一路只增不减——
/// 于是一个聊下去的会话最终会把几百条消息全部实化在那个非虚拟化列表里。
/// 本类补上缺的那一半：裁掉最早的一段，把窗口起点挪到对应的历史下标，
/// 那一段由「加载更早」按同一套开窗语义取回。
///
/// 三条口径值得单独记：
/// <list type="number">
/// <item><b>只在跟底时裁。</b>用户上滚在读旧消息时裁，他正看的内容会当场消失、视口跳走；
/// 而这条同时解掉「滚到顶自动续一窗、下一轮又被裁掉」的乒乓——一旦上滚，跟底就已经关了。
/// 代价是裁剪<b>不保证及时</b>，长时间挂在上面读旧消息的会话会临时超过上限。</item>
/// <item><b>边界对齐到用户消息。</b>按条目数硬切会把一次助手回复和它的工具卡切成两半，
/// 从用户消息处切，一轮不会被切开。</item>
/// <item><b>锚点必须在历史里找得到。</b>找不到就不裁——裁错窗口起点比不裁坏得多，
/// 那会让「加载更早」取回错的一段。</item>
/// </list>
///
/// 与 <see cref="ConversationItemActions"/> 是同一层的兄弟：都只吃 <c>Items</c> 加一两个
/// 窄依赖，由 <see cref="ConversationViewModel"/> 组合进来，彼此不知道对方存在。
/// </summary>
public sealed class ConversationItemWindowTrimmer
{
    /// <summary>
    /// 运行期条目上限。刻意<b>不分页面、不做设置项</b>：聊天页与 agent 页一轮的条目数
    /// 差一个量级，但在探针给出「条目数 → 布局耗时」的实测曲线之前分档只是凭感觉造魔法数。
    /// </summary>
    public const int DefaultMaxItems = 80;

    private readonly IList<ConversationItemBase> _items;
    private readonly HistoryWindow _window;
    private readonly Func<IReadOnlyList<ChatMessage>> _historySource;
    private readonly Func<bool> _isStuckToBottomSource;
    private readonly int _maxItems;

    /// <param name="items">界面条目集合（就地裁剪）</param>
    /// <param name="window">与加载期共用的那个渲染窗口</param>
    /// <param name="historySource">当前完整历史的来源（现取现用：中途换会话也能跟上）</param>
    /// <param name="isStuckToBottomSource">界面是否跟在底部</param>
    /// <param name="maxItems">条目上限，非正值按默认处理</param>
    public ConversationItemWindowTrimmer(
        IList<ConversationItemBase> items,
        HistoryWindow window,
        Func<IReadOnlyList<ChatMessage>> historySource,
        Func<bool> isStuckToBottomSource,
        int maxItems = DefaultMaxItems)
    {
        _items = items;
        _window = window;
        _historySource = historySource;
        _isStuckToBottomSource = isStuckToBottomSource;
        _maxItems = maxItems > 0 ? maxItems : DefaultMaxItems;
    }

    /// <summary>
    /// 需要且允许时裁一次。
    ///
    /// <b>只该在一轮结束、来源消息已回填之后调用</b>（见
    /// <see cref="ConversationItemActions.WireStreamed"/>）：流式中途裁会改变滚动区高度，
    /// 把跟底与 Offset 一起打乱；而来源消息没回填时找不到锚点，等于白跑一趟。
    /// </summary>
    /// <returns>真的裁掉了条目返回 true（调用方据此刷新「有更早消息」状态）</returns>
    public bool TrimIfNeeded()
    {
        if (_items.Count <= _maxItems) return false;
        if (!_isStuckToBottomSource()) return false;

        int anchor = FindAnchorIndex(_items.Count - _maxItems);
        if (anchor <= 0) return false; //没有可用锚点,或锚点就是第一条(没东西可裁)

        ChatMessage? source = _items[anchor].SourceMessage;
        if (source == null) return false;

        int historyIndex = IndexOfSame(_historySource(), source);
        if (historyIndex < 0) return false;

        // 摘出集合再释放:还挂在界面上的位图一释放,下一帧渲染就撞上去(见 ReleaseImages 的注释)。
        // 与 ConversationItemActions 里重跑截断历史那一处同一套顺序
        List<ConversationItemBase> discarded = new(anchor);
        for (int i = anchor - 1; i >= 0; i--)
        {
            discarded.Add(_items[i]);
            _items.RemoveAt(i);
        }

        foreach (ConversationItemBase item in discarded) item.ReleaseImages();

        _window.SetStart(historyIndex);
        return true;
    }

    /// <summary>
    /// 从理想切点往后找第一个用户消息条目。往后找而不是往前：
    /// 宁可多留几条也不少留——少留就是把用户还想看的一轮切掉了。
    /// </summary>
    /// <param name="from">理想切点（此处之前的条目是超出上限的那些）</param>
    /// <returns>锚点条目下标；没有可用锚点时为 -1</returns>
    private int FindAnchorIndex(int from)
    {
        for (int i = Math.Max(1, from); i < _items.Count; i++)
        {
            if (_items[i].SourceMessage is { } source && source.Role == ChatRole.User) return i;
        }

        return -1;
    }

    /// <summary>
    /// 按引用在历史里定位消息。<b>刻意不用 Equals</b>：<see cref="ChatMessage"/> 没有值语义，
    /// 而条目持有的正是历史里那一个实例，引用比较既准确又不受内容改写影响。
    /// </summary>
    private static int IndexOfSame(IReadOnlyList<ChatMessage> history, ChatMessage target)
    {
        for (int i = 0; i < history.Count; i++)
        {
            if (ReferenceEquals(history[i], target)) return i;
        }

        return -1;
    }
}
