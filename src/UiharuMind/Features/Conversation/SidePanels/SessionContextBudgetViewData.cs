/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 本会话上下文上限的编辑：可调区间、当前值、以及「恢复跟随模型」。
///
/// <b>下限不是 0，也不是当前占用</b>：低于 <see cref="SessionContextBudget.MinimumFor"/> 的话，
/// 下一次请求就会越过交接线，用户只想收紧一点，换来的是当场写一份记忆压缩
/// （一次完整请求 + 前缀缓存整段作废）。因此滑杆的可调区间就是
/// <c>[下限, 模型窗口]</c>，下限以左的部分置灰、拖不进去。
///
/// 数据那一半是只读的投影，写只走 <see cref="Value"/> 这一个入口，落盘走防抖。
/// </summary>
public partial class SessionContextBudgetViewData : ObservableObject
{
    /// <summary>拖动时别每个刻度都写一次盘：元数据是整份重写的</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly Func<ChatSession?> _session; //当前会话；切会话时换人，值跟着换
    private readonly Func<int> _modelContextLength; //模型真实窗口，可调区间的上界
    private readonly Func<long> _usage; //当前占用，下限由它算出来
    private readonly Action _afterChanged; //分母变了：环与面板要重刷
    private bool _isRefreshing; //回填值不是用户改的，别写回
    private CancellationTokenSource? _saveDebounce;

    /// <summary>可调区间的下界（含）。低于它下一次请求就会触发交接</summary>
    [ObservableProperty] private int _minimum;

    /// <summary>可调区间的上界（含），即模型真实窗口</summary>
    [ObservableProperty] private int _maximum;

    /// <summary>当前生效上限。拖动它就会写进会话</summary>
    [ObservableProperty] private int _value;

    /// <summary>当前值的显示文本</summary>
    [ObservableProperty] private string _valueText = string.Empty;

    /// <summary>可调区间是否非空。占用已经顶到窗口时没有可调空间，滑杆整条不可用</summary>
    [ObservableProperty] private bool _hasRoom;

    /// <summary>有没有模型。没有模型就没有可调的上界，整块设置不出现（上面的面板已说明原因）</summary>
    [ObservableProperty] private bool _hasModel;

    /// <summary>滑杆的吸附步长：1k 一档，免得调出 83714 这种数</summary>
    [ObservableProperty] private double _tickFrequency = 1000;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="session">取当前会话（可能为空：还没建会话）</param>
    /// <param name="modelContextLength">取模型真实窗口</param>
    /// <param name="usage">取当前占用（有效占用）</param>
    /// <param name="afterChanged">改完之后通知外面重刷</param>
    public SessionContextBudgetViewData(Func<ChatSession?> session, Func<int> modelContextLength, Func<long> usage,
        Action afterChanged)
    {
        _session = session;
        _modelContextLength = modelContextLength;
        _usage = usage;
        _afterChanged = afterChanged;
    }

    /// <summary>按当前会话、模型与占用重算区间。打开面板时调一次即可</summary>
    public void Refresh()
    {
        ChatSession? session = _session();
        int model = _modelContextLength();
        int floor = SessionContextBudget.MinimumFor(_usage(), model);
        int stored = session?.ContextLengthOverride ?? 0;

        // 存着的上限落到下限之下（一轮里占用涨过去了）：夹到下限并写回本体。
        // 留着的话环会顶在 100%，而下一发就会触发交接——那个数已经没有意义了。
        // 写回是必须的：Usage 与压缩读的都是会话上这个字段，只夹显示就等于让两边各说各话
        if (session != null && stored > 0 && stored < floor)
        {
            stored = floor;
            session.ContextLengthOverride = floor;
        }

        _isRefreshing = true;
        try
        {
            Maximum = model;
            Minimum = Math.Clamp(floor, 0, model);
            Value = SessionContextBudget.Resolve(model, stored);
            ValueText = Value > 0 ? TurnUsageLedger.Format(Value) : "—";
            HasModel = model > 0;
            HasRoom = model > 0 && Maximum > Minimum;
            // 吸附步长按区间缩放：区间只有几 k 时 1k 一档会只剩两三个位置
            TickFrequency = Math.Max(1, Math.Round((Maximum - Minimum) / 40.0 / 1000.0) * 1000);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>停掉还没落盘的保存</summary>
    public void Dispose()
    {
        _saveDebounce?.Cancel();
        _saveDebounce = null;
    }

    partial void OnValueChanged(int value)
    {
        if (_isRefreshing) return;
        ChatSession? session = _session();
        if (session == null) return;
        // 拖到顶就是「跟随模型」：留着一条与窗口等值的覆写，换到更大的模型时会白白压住它。
        // 因此这里不需要一个单独的「恢复」按钮
        int normalized = value >= Maximum ? 0 : Math.Max(value, 0);
        if (session.ContextLengthOverride == normalized) return;

        session.ContextLengthOverride = normalized;
        ValueText = TurnUsageLedger.Format(SessionContextBudget.Resolve(Maximum, normalized));
        ScheduleSave(session);
        _afterChanged();
    }

    private void ScheduleSave(ChatSession session)
    {
        _saveDebounce?.Cancel();
        CancellationTokenSource debounce = new();
        _saveDebounce = debounce;
        CancellationToken token = debounce.Token;
        DispatcherTimer.RunOnce(() =>
        {
            if (token.IsCancellationRequested) return;
            SaveNow(session);
        }, SaveDelay);
    }

    private static void SaveNow(ChatSession session)
    {
        // 只动了头字段：Save() 会把整份历史重新序列化一遍，而这个值随时可能被再拖一次
        session.SaveMeta();
    }
}
