/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Threading;
using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Shared.Services.Native;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// macOS 没有剪贴板变更通知，只能轮询 changeCount（本地整数读取，实测约 0.5µs）。
/// 用户复制总以一次按键或鼠标点击收尾，所以键鼠活动后的一小段时间里密集轮询，
/// 空闲时放慢，只兜底脚本写入、接力等无键鼠操作的情况；全局钩子没跑起来时拿不到活动信号，退回固定间隔。
/// 事件在后台线程触发，订阅方自行封送到 UI 线程。
/// </summary>
public class MacClipboardMonitor : IClipboardMonitor
{
    private static readonly IntPtr GeneralPasteboardSelector = MacNative.Selector("generalPasteboard");
    private static readonly IntPtr ChangeCountSelector = MacNative.Selector("changeCount");

    private const int ActiveFirstCheckMs = 20; //活动后首次检查：应用写剪贴板通常十几毫秒内完成
    private const int ActiveIntervalMs = 50;
    private const int ActiveWindowMs = 600; //最后一次活动后保持密集轮询的时长
    private const int IdleIntervalMs = 1000;
    private const int NoHookIntervalMs = 150;

    private readonly Timer _pollTimer;
    private readonly IntPtr _pasteboard;
    private readonly InputManager _inputManager;
    private readonly object _scheduleLock = new();
    private long _lastChangeCount;
    private long _activeUntil; //Environment.TickCount64，此前处于活跃期
    private int _period; //定时器当前间隔
    private volatile bool _disposed;

    public event Action? OnClipboardChanged;

    public MacClipboardMonitor(InputManager inputManager)
    {
        IntPtr nsPasteboardClass = MacNative.GetClass("NSPasteboard");
        _pasteboard = MacNative.SendPtr(nsPasteboardClass, GeneralPasteboardSelector);
        _lastChangeCount = ReadChangeCount();

        _inputManager = inputManager;
        // 钩子此时多半还没启动，先按无钩子的间隔跑，之后每次检查时校正
        _period = NoHookIntervalMs;
        _pollTimer = new Timer(_ => OnPollTick(), null, _period, _period);

        _inputManager.EventOnKeyDown += OnKeyDown;
        _inputManager.EventOnMouseReleased += OnMouseReleased;
    }

    private int IdlePeriod => _inputManager.IsRunning ? IdleIntervalMs : NoHookIntervalMs;

    private long ReadChangeCount() => MacNative.SendNint(_pasteboard, ChangeCountSelector);

    // 以下两个跑在钩子线程上，必须立刻返回
    private void OnKeyDown(KeyCode keyCode) => OnUserActivity();

    private void OnMouseReleased(MouseEventData data) => OnUserActivity();

    private void OnUserActivity()
    {
        lock (_scheduleLock)
        {
            if (_disposed) return;
            _activeUntil = Environment.TickCount64 + ActiveWindowMs;
            if (_period != ActiveIntervalMs) Reschedule(ActiveFirstCheckMs, ActiveIntervalMs);
        }
    }

    private void OnPollTick()
    {
        try
        {
            CheckClipboard();
        }
        catch (Exception e)
        {
            // 线程池上的异常没人接，会直接带崩进程
            Log.Warning($"Mac clipboard check failed: {e.Message}");
        }

        lock (_scheduleLock)
        {
            if (_disposed) return;
            int period = Environment.TickCount64 < _activeUntil ? ActiveIntervalMs : IdlePeriod;
            if (_period != period) Reschedule(period, period);
        }
    }

    private void Reschedule(int dueTime, int period)
    {
        _period = period;
        _pollTimer.Change(dueTime, period);
    }

    // 换档时定时器回调可能重叠，用 CAS 保证同一次变化只通知一次
    private void CheckClipboard()
    {
        if (_disposed) return;

        long last = Interlocked.Read(ref _lastChangeCount);
        long current = ReadChangeCount();
        if (current == last) return;
        if (Interlocked.CompareExchange(ref _lastChangeCount, current, last) != last) return;

        if (!_disposed) OnClipboardChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (_scheduleLock)
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer.Dispose();
        }

        _inputManager.EventOnKeyDown -= OnKeyDown;
        _inputManager.EventOnMouseReleased -= OnMouseReleased;
        OnClipboardChanged = null;
        GC.SuppressFinalize(this);
    }
}
