using System;
using System.Collections.Generic;
using Avalonia.Threading;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 供应商读数的<b>公共调度</b>：全应用一个定时拍，所有界面挂着的那几份读数都由它驱动。
///
/// 为什么不能「每个界面自己轮询」：开着五个同供应商的窗口就是五份定时器，
/// 同一把 key 被问五次。HTTP 那一层由 <c>ProviderStatusService</c> 按 key 的 TTL 缓存挡着
/// （五个窗口一共一次请求），但每个界面各自醒来、各自解析目标、各自刷界面——还是五份开销，
/// 而且「谁先取到」决定谁先显示，数字会先后跳一下。
///
/// 这里一个拍子：每个订阅者各自解析自己的目标（跟着各自的会话走），HTTP 仍按 key 合并。
/// 最后一个订阅者走掉，拍子就停。
/// </summary>
public sealed class ProviderStatusHub
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _tick = new() { Interval = TickInterval };
    private readonly List<ProviderStatusViewData> _subscribers = [];
    private bool _running;

    /// <summary>全应用共用的一份</summary>
    public static ProviderStatusHub Shared { get; } = new();

    private ProviderStatusHub()
    {
        _tick.Tick += (_, _) => Tick();
    }

    /// <summary>挂上一份读数（挂上就立刻取一次，不用等下一拍）</summary>
    /// <param name="view">读数</param>
    public void Subscribe(ProviderStatusViewData view)
    {
        if (_subscribers.Contains(view)) return;
        _subscribers.Add(view);
        if (!_running)
        {
            _running = true;
            _tick.Start();
        }
        view.Pump();
    }

    /// <summary>摘掉一份读数</summary>
    /// <param name="view">读数</param>
    public void Unsubscribe(ProviderStatusViewData view)
    {
        if (!_subscribers.Remove(view)) return;
        if (_subscribers.Count == 0)
        {
            _running = false;
            _tick.Stop();
        }
    }

    private void Tick()
    {
        // 复制一份再走：订阅者可能在拍子里退订
        foreach (ProviderStatusViewData view in _subscribers.ToArray()) view.Pump();
    }
}
