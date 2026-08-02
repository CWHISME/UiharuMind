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
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Linq;
using UiharuMind.Utils;
using UiharuMind.ViewModels.Conversation;
using UiharuMind.ViewModels.UIHolder;

namespace UiharuMind.Views.Common.Conversation;

/// <summary>
/// 通用会话组件(以 ChatView 为模板):头部/消息流/composer 三段式,
/// 经 HeaderExtra / ComposerTop / ComposerTools / EmptyContent 四个内容槽由宿主页定制。
/// 绑定契约为 ConversationViewModel;特型条目模板由宿主页资源提供。
/// </summary>
public partial class ConversationView : UserControl
{
    public static readonly StyledProperty<object?> HeaderExtraProperty =
        AvaloniaProperty.Register<ConversationView, object?>(nameof(HeaderExtra));

    public static readonly StyledProperty<object?> ComposerTopProperty =
        AvaloniaProperty.Register<ConversationView, object?>(nameof(ComposerTop));

    public static readonly StyledProperty<object?> ComposerToolsProperty =
        AvaloniaProperty.Register<ConversationView, object?>(nameof(ComposerTools));

    public static readonly StyledProperty<object?> EmptyContentProperty =
        AvaloniaProperty.Register<ConversationView, object?>(nameof(EmptyContent));

    /// <summary>头部右侧动作区内容</summary>
    public object? HeaderExtra
    {
        get => GetValue(HeaderExtraProperty);
        set => SetValue(HeaderExtraProperty, value);
    }

    /// <summary>输入框上方内容(如附件 chips)</summary>
    public object? ComposerTop
    {
        get => GetValue(ComposerTopProperty);
        set => SetValue(ComposerTopProperty, value);
    }

    /// <summary>composer 工具行内容(模式/权限/附件按钮等)</summary>
    public object? ComposerTools
    {
        get => GetValue(ComposerToolsProperty);
        set => SetValue(ComposerToolsProperty, value);
    }

    /// <summary>消息为空时的引导内容</summary>
    public object? EmptyContent
    {
        get => GetValue(EmptyContentProperty);
        set => SetValue(EmptyContentProperty, value);
    }

    public ConversationView()
    {
        InitializeComponent();
        _ = new ScrollViewerAutoScrollHolder(Viewer);
        InputBox.PastingFromClipboard += OnPastingFromClipboard;
        DragDrop.SetAllowDrop(ComposerBorder, true);
        ComposerBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ComposerBorder.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnLoadEarlierClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        // 前插会把现有内容整体下推,按前插高度补偿 Offset 以保持视口内容不动
        double extentBefore = Viewer.Extent.Height;
        double offsetBefore = Viewer.Offset.Y;
        vm.LoadEarlierMessages();
        Viewer.UpdateLayout();
        Viewer.Offset = new Vector(Viewer.Offset.X, offsetBefore + Viewer.Extent.Height - extentBefore);
    }

    private async void OnPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        // 剪贴板含图片:以内存字节加入附件,并拦截默认文本粘贴
        var bitmap = await App.Clipboard.GetImageFromClipboard();
        if (bitmap == null) return;

        vm.AddAttachmentBytes(bitmap.BitmapToBytes());
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Any(f => f == DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetRaw(DataFormat.File) is IStorageItem storageItem)
            {
                string? path = storageItem.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) vm.AddAttachmentPath(path);
            }
        }

        e.Handled = true;
    }
}
