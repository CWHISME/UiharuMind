using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiharuMind.Features.Models;

/// <summary>
/// 已装模型页签里本地引擎的状态条（语音、生图同一个样子）。看得见时每隔几秒刷一次，占用与 CPU 跟着变
/// </summary>
public partial class EngineStatusStrip : UserControl
{
    public static readonly StyledProperty<EngineStatusData?> StatusProperty =
        AvaloniaProperty.Register<EngineStatusStrip, EngineStatusData?>(nameof(Status));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<EngineStatusStrip, object?>(nameof(Actions));

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _refresh = new() { Interval = RefreshInterval };

    public EngineStatusStrip()
    {
        InitializeComponent();
        // 页面切走时视图仍在视觉树里，只是藏着
        _refresh.Tick += (_, _) =>
        {
            if (IsEffectivelyVisible) Status?.Refresh();
        };
    }

    /// <summary>
    /// 引擎状态
    /// </summary>
    public EngineStatusData? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>
    /// 右侧的链接
    /// </summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    // 只在页签看得见时刷（占用要现取，起停有事件）
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _refresh.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _refresh.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
