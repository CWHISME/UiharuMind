/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 指针悬停到宿主行时才实例化内容的占位容器。
/// 消息操作按钮这类"绝大多数时候不可见"的重内容(每行多个 SVG 图标按钮)用它避免逐行预构建——
/// 长会话的构建与拆除成本都随之下降;首次悬停时以数据上下文套用 ContentTemplate 生成内容。
/// </summary>
public class HoverDeferredContent : ContentControl
{
    public static readonly StyledProperty<string> HostClassProperty =
        AvaloniaProperty.Register<HoverDeferredContent, string>(nameof(HostClass), "message-row");

    /// <summary>触发实例化的宿主祖先的样式类名(默认消息行)</summary>
    public string HostClass
    {
        get => GetValue(HostClassProperty);
        set => SetValue(HostClassProperty, value);
    }

    private Control? _host; //已订阅悬停事件的宿主

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Content != null || _host != null) return;

        _host = FindHost();
        if (_host != null) _host.PointerEntered += OnHostPointerEntered;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnhookHost();
    }

    private void OnHostPointerEntered(object? sender, PointerEventArgs e)
    {
        UnhookHost();
        Content ??= DataContext;
    }

    private void UnhookHost()
    {
        if (_host == null) return;
        _host.PointerEntered -= OnHostPointerEntered;
        _host = null;
    }

    private Control? FindHost()
    {
        Visual? current = this.GetVisualParent();
        while (current != null)
        {
            if (current is Control control && control.Classes.Contains(HostClass)) return control;
            current = current.GetVisualParent();
        }

        return null;
    }
}
