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
/// <item><b>只在没有视口会被抽走时裁。</b>用户上滚在读旧消息时裁，他正看的内容会当场消失、
/// 视口跳走；而这条同时解掉「滚到顶自动续一窗、下一轮又被裁掉」的乒乓——一旦上滚，跟底就已经关了。
/// 判据由外部给（见构造参数 <c>canTrimSource</c>）：<b>不在界面上的实例没有视口，因此照裁</b>——
/// 后台还在跑的会话恰恰是最需要裁的那个。代价是裁剪<b>不保证及时</b>，
/// 长时间挂在上面读旧消息的会话会临时超过上限。</item>
/// <item><b>边界优先对齐到用户消息，长轮次退到消息起点。</b>按条目数硬切会把一次助手回复
/// 和它的工具卡切成两半，所以首选从用户消息处切，一轮不会被切开。但 agent 的一轮能长到几百条，
/// 整轮之内一条用户消息都没有——只认用户消息就等于整轮不裁，上限形同虚设。因此超过硬顶
/// （<see cref="MidTurnAnchorFactor"/>）时改按<b>消息起点</b>切，代价是这一轮被切开。
/// 从中间切就得自己认那两条本来由「用户锚点」顺带保住的边界：待决审批卡不许摘走
/// （摘走等于让那一轮永远等不到回应，见 <see cref="LimitByPendingApproval"/>），
/// 还没回填来源消息的流式条目天然不会被选中（它们给不出锚点）。</item>
/// <item><b>锚点必须在历史里找得到。</b>找不到就不裁——裁错窗口起点比不裁坏得多，
/// 那会让「加载更早」取回错的一段。</item>
/// </list>
///
/// 长轮次为什么可以从中间切，见 ADR 0037（长轮次允许从中间裁，硬顶优先于不切开一轮）。
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

    /// <summary>
    /// 切走之后的条目上限，比 <see cref="DefaultMaxItems"/> 紧得多。
    ///
    /// 切回一个缓存实例是<b>不走回放的</b>（它的 <c>Items</c> 就是后台攒出来的那一份），
    /// 于是切回的代价就是这些条目一次性重新实体化——非虚拟化列表里这是纯布局开销，
    /// 留多少直接等于卡多久（实测一窗 20 条里，四次布局占 274ms 中的 231ms）。
    ///
    /// 取的是<b>首屏量级</b>，跟 <see cref="HistoryWindow.FirstScreenSize"/> 同一个思路：
    /// 切换的关键路径上只付一屏，不足一屏的部分由视图在贴底之后按窗补
    /// （<c>ConversationView.ScheduleViewportTopUp</c>），那段布局落在用户读第一屏的时间里。
    /// 少了那条补足兜底，裁到不足一屏会让列表滚不动，「滚到顶自动续窗」就永远不触发。
    ///
    /// 单位是<b>条目</b>不是消息，两者差好几倍（一次助手回复 = 思考段 + 正文 + 若干工具卡），
    /// 所以这个数不该去对齐 <see cref="HistoryWindow"/> 那两个消息数。
    /// </summary>
    public const int DefaultBackgroundMaxItems = 10;

    /// <summary>
    /// 一轮长到什么程度才允许从它中间切开（相对上限的倍数）。
    ///
    /// 首选锚点是用户消息，而 agent 的一轮可以长到几百个条目——整轮之内一条用户消息都没有，
    /// 于是往头部找到的永远是本轮开头那一条，「保留量」等于整整一轮，上限形同虚设。
    /// 这正是「切回后台跑着的会话，条目依旧几百条」的由来：它不是没裁，是裁了也等于没裁。
    ///
    /// 所以留一道硬顶：用户锚点保下来的条目超过上限这么多倍时，改按消息起点切。
    /// 取 2 而不是 1 是为了让正常长度的一轮永远走首选那条路——一轮不被切开仍是默认口径，
    /// 从中间切只发生在「这一轮本身就比上限大一个量级」的时候。
    /// </summary>
    private const int MidTurnAnchorFactor = 2;

    private readonly IList<ConversationItemBase> _items;
    private readonly HistoryWindow _window;
    private readonly Func<IReadOnlyList<ChatMessage>> _historySource;
    private readonly Func<bool> _canTrimSource;
    private readonly int _maxItems;
    private readonly int _backgroundMaxItems;

    /// <param name="items">界面条目集合（就地裁剪）</param>
    /// <param name="window">与加载期共用的那个渲染窗口</param>
    /// <param name="historySource">当前完整历史的来源（现取现用：中途换会话也能跟上）</param>
    /// <param name="canTrimSource">此刻裁剪是否不会抽走用户正在读的视口</param>
    /// <param name="maxItems">条目上限，非正值按默认处理</param>
    /// <param name="backgroundMaxItems">切走之后的条目上限，非正值按默认处理</param>
    public ConversationItemWindowTrimmer(
        IList<ConversationItemBase> items,
        HistoryWindow window,
        Func<IReadOnlyList<ChatMessage>> historySource,
        Func<bool> canTrimSource,
        int maxItems = DefaultMaxItems,
        int backgroundMaxItems = DefaultBackgroundMaxItems)
    {
        _items = items;
        _window = window;
        _historySource = historySource;
        _canTrimSource = canTrimSource;
        _maxItems = maxItems > 0 ? maxItems : DefaultMaxItems;
        _backgroundMaxItems = backgroundMaxItems > 0 ? backgroundMaxItems : DefaultBackgroundMaxItems;
    }

    /// <summary>
    /// 需要且允许时裁一次。
    ///
    /// 常规时机是一轮结束、来源消息已回填之后（见
    /// <see cref="ConversationItemActions.WireStreamed"/>）；从 ADR 0038 起，
    /// 轮内消息边界也是一处合法时机（<see cref="ConversationViewModel.OnMessageBoundaryReached"/>），
    /// 那刻流段刚收尾、来源消息也刚落盘，锚点与滚动位置都是确定的。
    /// 唯一下不得手的地方是<b>流式中途</b>：会改变滚动区高度，把跟底与 Offset 一起打乱；
    /// 来源消息没回填时也找不到锚点，等于白跑一趟。
    /// </summary>
    /// <returns>真的裁掉了条目返回 true（调用方据此刷新「有更早消息」状态）</returns>
    public bool TrimIfNeeded() => TrimTo(_maxItems);

    /// <summary>
    /// 按「切走之后」的上限裁一次，见 <see cref="DefaultBackgroundMaxItems"/>。
    ///
    /// 调用时机是<b>切走之后</b>而不是切走那一刻：切走那一刻视图还绑在本实例上，
    /// 此时裁就是在「让切换变快」这件事上反倒先付一次布局。
    /// </summary>
    /// <returns>真的裁掉了条目返回 true</returns>
    public bool TrimToBackgroundBudget() => TrimTo(_backgroundMaxItems);

    /// <param name="maxItems">本次的条目上限</param>
    /// <returns>真的裁掉了条目返回 true</returns>
    private bool TrimTo(int maxItems)
    {
        if (_items.Count <= maxItems) return false;
        if (!_canTrimSource()) return false;

        int from = LimitByPendingApproval(_items.Count - maxItems);
        IReadOnlyList<ChatMessage> history = _historySource();

        // 锚点可能在历史里找不到(用户删过消息),备选依次试,而不是一试不中就整轮不裁
        foreach (int anchor in AnchorCandidates(from, maxItems))
        {
            if (anchor <= 0) continue; //没有可用锚点,或锚点就是第一条(没东西可裁)
            if (_items[anchor].SourceMessage is not { } source) continue;

            int historyIndex = IndexOfSame(history, source);
            if (historyIndex < 0) continue;

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

        return false;
    }

    /// <summary>
    /// 本次可用的锚点，按<b>优先级</b>给出。
    ///
    /// 首选永远是用户消息（一轮不会被切开）；只有当它保下来的条目仍然超过硬顶
    /// （见 <see cref="MidTurnAnchorFactor"/>）时，才把消息起点排到它前面——
    /// 那种情形下「不切开一轮」已经无从谈起，一轮本身就比上限大一个量级。
    /// </summary>
    /// <param name="from">理想切点</param>
    /// <param name="maxItems">本次的条目上限</param>
    /// <returns>锚点下标，按优先级排列</returns>
    private IEnumerable<int> AnchorCandidates(int from, int maxItems)
    {
        int userAnchor = FindUserAnchorIndex(from);
        if (userAnchor > 0 && _items.Count - userAnchor <= maxItems * MidTurnAnchorFactor)
        {
            yield return userAnchor;
            yield break;
        }

        yield return FindMessageStartIndex(from);
        yield return userAnchor;
    }

    /// <summary>
    /// 从理想切点<b>往头部</b>找最近的一个用户消息条目。
    ///
    /// 方向是这里唯一要紧的事。往尾部找等于只在「要保留的那一段」里找锚点，
    /// 而一轮 agent 回复的尾部十几条全是助手正文、思考段与工具卡——一条用户消息都没有，
    /// 于是找不到锚点、整轮不裁：上限越紧越必然失败，紧到一屏就是彻底不工作。
    ///
    /// 往头部找则只要历史里有过用户消息就一定找得到，代价是<b>多留几条</b>（保留量 ≥ 上限）。
    /// 那个代价是好的：少留才会把用户还想看的一轮切开，而多留至多是这次少省一点。
    /// </summary>
    /// <param name="from">理想切点（此处之前的条目是超出上限的那些）</param>
    /// <returns>锚点条目下标；没有可用锚点时为 -1</returns>
    private int FindUserAnchorIndex(int from)
    {
        for (int i = Math.Min(from, _items.Count - 1); i > 0; i--)
        {
            if (_items[i].SourceMessage is { } source && source.Role == ChatRole.User) return i;
        }

        return -1;
    }

    /// <summary>
    /// 从理想切点往头部找最近的一个<b>消息起点</b>——它的来源消息与前一条条目的不是同一条。
    ///
    /// 这是一轮内部唯一安全的切点：一条助手消息可以摊成思考段 + 正文 + 若干工具卡，
    /// 它们共享同一个来源消息，从中间切开会让窗口起点指向一条<b>已经渲染出一半</b>的消息，
    /// 「加载更早」把它整条取回来时就会重复。按来源消息换人处切，切出来的两段各自完整。
    /// </summary>
    /// <param name="from">理想切点</param>
    /// <returns>锚点条目下标；没有可用锚点时为 -1</returns>
    private int FindMessageStartIndex(int from)
    {
        for (int i = Math.Min(from, _items.Count - 1); i > 0; i--)
        {
            if (_items[i].SourceMessage is not { } source) continue;
            if (!ReferenceEquals(_items[i - 1].SourceMessage, source)) return i;
        }

        return -1;
    }

    /// <summary>
    /// 把切点收到第一张<b>待决</b>审批卡之前。
    ///
    /// 摘走待决审批卡等于让那一轮永远等不到回应：卡片没了，回应口
    /// （<c>ApprovalRequestItem.Response</c>）却还挂在运行循环上，用户按不到、轮次也不结束。
    /// 用户锚点那条路顺带保住了它（审批总在本轮之内），消息起点那条路不会——
    /// 它敢从一轮中间切，就必须自己认这条边界。
    /// </summary>
    /// <param name="from">理想切点</param>
    /// <returns>收紧后的切点</returns>
    private int LimitByPendingApproval(int from)
    {
        int limit = Math.Min(from, _items.Count - 1);
        for (int i = 0; i <= limit; i++)
        {
            if (_items[i] is ApprovalRequestItem { IsResolved: false }) return i;
        }

        return from;
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
