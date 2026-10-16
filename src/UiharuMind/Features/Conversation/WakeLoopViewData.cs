using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 后台唤醒循环（离席、目标模式）视图数据的公共部分：状态跟着控制器的 StatusChanged 回 UI 线程刷新，
/// 延迟唤醒的倒计时每秒走一格，结束前先确认
/// </summary>
public abstract partial class WakeLoopViewData : ObservableObject, IDisposable
{
    private readonly string _sessionId;
    private readonly IMessageService _messages;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>构造。派生类填好状态后调 <see cref="SyncTicker"/></summary>
    /// <param name="sessionId">循环挂在哪个会话上（群壳或单聊）</param>
    /// <param name="messages">结束前弹确认用的消息服务</param>
    /// <param name="now">当前时刻；null 取本地时间</param>
    protected WakeLoopViewData(string sessionId, IMessageService messages, Func<DateTimeOffset>? now)
    {
        _sessionId = sessionId;
        _messages = messages;
        Now = now ?? (() => DateTimeOffset.Now);
        _ticker.Tick += (_, _) => OnPropertyChanged(nameof(CountdownLine));
    }

    /// <summary>循环在进行</summary>
    public abstract bool IsActive { get; }

    /// <summary>没在进行（开始按钮的显隐）</summary>
    public bool IsIdle => !IsActive;

    /// <summary>有没有倒计时</summary>
    public bool HasCountdown => WakeAt != null;

    /// <summary>倒计时行；没在等为空</summary>
    public abstract string CountdownLine { get; }

    /// <summary>当前时刻</summary>
    protected Func<DateTimeOffset> Now { get; }

    /// <summary>确认与提示用的消息服务（派生类要弹自己的确认框时用它）</summary>
    protected IMessageService Messages => _messages;

    /// <summary>投到 UI 线程的刷新可能在视图关掉之后才到</summary>
    protected bool IsDisposed { get; private set; }

    /// <summary>延迟唤醒的到点时刻；没在等为 null</summary>
    protected abstract DateTimeOffset? WakeAt { get; }

    /// <summary>结束确认的文案</summary>
    protected abstract LangKey EndConfirmKey { get; }

    /// <summary>
    /// 倒计时的写法：不足一小时写「分:秒」，否则「时:分:秒」；过了点按 0 算
    /// </summary>
    /// <param name="remaining">剩余时间</param>
    /// <returns>文本</returns>
    public static string CountdownText(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes}:{remaining.Seconds:00}";
    }

    /// <summary>结束前先确认</summary>
    /// <returns>确认并结束了为 true；取消为 false（设置弹窗据此决定关窗时机）</returns>
    public async Task<bool> ConfirmEndAsync()
    {
        if (!await _messages.ConfirmAsync(Loc.Text(EndConfirmKey))) return false;
        EndCore();
        return true;
    }

    /// <summary>摘掉订阅、停掉倒计时（派生类摘自己的订阅后调本方法）</summary>
    public virtual void Dispose()
    {
        IsDisposed = true;
        _ticker.Stop();
    }

    /// <summary>直接结束（不确认）</summary>
    protected abstract void EndCore();

    /// <summary>不等倒计时，立刻唤醒</summary>
    protected abstract void WakeNowCore();

    /// <summary>从控制器重取状态（不发通知）</summary>
    protected abstract void ReloadStatus();

    /// <summary>重取状态后通知派生类自己的属性</summary>
    protected virtual void OnStatusReloaded()
    {
    }

    /// <summary>按格式键写倒计时行；没在等为空</summary>
    /// <param name="format">带一个占位的格式文案</param>
    /// <returns>倒计时行</returns>
    protected string CountdownOf(LangKey format) =>
        WakeAt is { } wakeAt ? Loc.Text(format, CountdownText(wakeAt - Now())) : string.Empty;

    /// <summary>控制器的 StatusChanged 处理：只认本会话，回 UI 线程重取并刷新</summary>
    /// <param name="sessionId">状态变了的会话</param>
    protected void OnStatusChanged(string sessionId)
    {
        if (sessionId != _sessionId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsDisposed) return;
            ReloadStatus();
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(HasCountdown));
            OnPropertyChanged(nameof(CountdownLine));
            OnStatusReloaded();
            SyncTicker();
        });
    }

    /// <summary>有倒计时才走表</summary>
    protected void SyncTicker()
    {
        if (HasCountdown) _ticker.Start();
        else _ticker.Stop();
    }

    [RelayCommand]
    private void End() => EndCore();

    [RelayCommand]
    private void WakeNow() => WakeNowCore();

    [RelayCommand]
    private Task ConfirmEnd() => ConfirmEndAsync();
}
