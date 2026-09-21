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
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// Linux 剪贴板监听器：轮询 Avalonia 的 IClipboard 检测文本变化。
/// 与 MacClipboardMonitor 同为轮询策略——Linux 普通应用没有 OS 推送的剪贴板变更事件。
/// 直接复用应用既有的剪贴板读取路径（Avalonia 已封装 X11/Wayland 后端），不依赖外部 CLI。
/// </summary>
public class LinuxClipboardMonitor : IClipboardMonitor
{
    private readonly Func<Task<string?>> _readClipboard;
    private readonly DispatcherTimer _timer;
    private string? _lastText;
    private bool _disposed;
    private bool _isChecking;
    private DateTime _lastErrorLog = DateTime.MinValue;

    public event Action? OnClipboardChanged;

    public LinuxClipboardMonitor(Func<Task<string?>> readClipboard, double interval = 500)
    {
        _readClipboard = readClipboard ?? throw new ArgumentNullException(nameof(readClipboard));
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(interval)
        };
        _timer.Tick += CheckClipboard;
        _timer.Start();
    }

    private async void CheckClipboard(object? sender, EventArgs e)
    {
        if (_disposed || _isChecking) return;

        _isChecking = true;
        try
        {
            var text = await _readClipboard();
            if (string.IsNullOrEmpty(text)) return;

            if (text == _lastText) return;
            _lastText = text;
            if (!_disposed) OnClipboardChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // 瞬失败（如窗口尚未就绪、Wayland 拒绝后台读取）仅偶发记录，避免刷屏
            if ((DateTime.Now - _lastErrorLog).TotalSeconds > 10)
            {
                Log.Debug($"Linux 剪贴板轮询失败：{ex.Message}");
                _lastErrorLog = DateTime.Now;
            }
        }
        finally
        {
            _isChecking = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= CheckClipboard;
        OnClipboardChanged = null;
        GC.SuppressFinalize(this);
    }
}
