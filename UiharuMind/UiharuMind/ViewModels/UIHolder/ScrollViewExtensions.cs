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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.UIHolder;

/// <summary>
/// 用于绑定 Model 层控制 ScrollViewer 自动滚动到底部的属性，发送聊天使用，发送后自动滚动到底部
/// </summary>
public static class ScrollViewExtensions
{
    public static readonly AttachedProperty<bool> ScrollToEndProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("ScrollToEnd", typeof(ScrollViewer),
            defaultBindingMode: BindingMode.TwoWay);

    public static void SetScrollToEnd(ScrollViewer element, bool value)
    {
        element.SetValue(ScrollToEndProperty, value);
    }

    public static bool GetScrollToEnd(ScrollViewer element)
    {
        return (bool)element.GetValue(ScrollToEndProperty);
    }

    static ScrollViewExtensions()
    {
        var observer = new ScrollViewPropertyObserver();
        ScrollToEndProperty.Changed.Subscribe(observer);
    }
}

public class ScrollViewPropertyObserver : IObserver<AvaloniaPropertyChangedEventArgs<bool>>
{
    public void OnNext(AvaloniaPropertyChangedEventArgs<bool> value)
    {
        if (value.Sender is ScrollViewer scrollView && value.NewValue.Value)
        {
            // 手动滚动到最底部
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                scrollView.Offset = new Vector(scrollView.Offset.X,
                    scrollView.Extent.Height - scrollView.Viewport.Height);
                ScrollViewExtensions.SetScrollToEnd(scrollView, false); // 重置属性
                // Log.Debug("Scroll to end");
            });
        }
    }

    public void OnError(Exception error)
    {
        // 处理错误
    }

    public void OnCompleted()
    {
        // 处理完成
    }
}