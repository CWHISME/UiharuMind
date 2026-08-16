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
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Specialized;
using System.Linq;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.UIHolder;

namespace UiharuMind.Features.Conversation;

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

    private readonly ScrollViewerAutoScrollHolder _autoScrollHolder;
    private ConversationViewModel? _viewModel;

    public ConversationView()
    {
        InitializeComponent();
        _autoScrollHolder = new ScrollViewerAutoScrollHolder(Viewer);
        DataContextChanged += OnDataContextChanged;
        InputBox.PastingFromClipboard += OnPastingFromClipboard;
        DragDrop.SetAllowDrop(ComposerBorder, true);
        ComposerBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ComposerBorder.AddHandler(DragDrop.DropEvent, OnDrop);

        // 点名补全:↑↓ 与 Esc 没有对应的 KeyBinding,走路由事件即可。
        // 回车与 Tab 则相反——它们绑在输入框的 KeyBindings 上,而 Avalonia 由
        // KeyboardDevice.ProcessRawEvent 沿视觉父链在 raise 路由事件之前就处理掉 KeyBindings,
        // 因此那两个键是在 SendMessage/InputExtra 命令入口改道的,不在这里。
        // 这里仍留一个回车分支作兜底:SendGesture 若改成 Ctrl+Enter,裸回车就没有 KeyBinding 了
        ComposerBorder.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        SkillPicker.PointerReleased += OnSkillPickerPointerReleased;
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm || !vm.Palette.IsSkillPickerOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                vm.Palette.MoveSkillSelection(1);
                break;
            case Key.Up:
                vm.Palette.MoveSkillSelection(-1);
                break;
            case Key.Escape:
                vm.Palette.CloseSkillPicker();
                break;
            case Key.Enter:
                if (!vm.AcceptSkillCandidate()) return;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnSkillPickerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // 选中在 PointerPressed 阶段已经落到 SelectedIndex,这里直接采纳
        if (DataContext is ConversationViewModel vm) vm.AcceptSkillCandidate();
    }

    /// <summary>采纳补全后把焦点与光标交还输入框末尾,否则用户得自己点一下才能接着写参数</summary>
    private void OnSkillCandidateAccepted()
    {
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text?.Length ?? 0;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Palette.SkillCandidateAccepted -= OnSkillCandidateAccepted;
        }

        _viewModel = DataContext as ConversationViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Palette.SkillCandidateAccepted += OnSkillCandidateAccepted;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 会话构建完成后恢复跟底。不能在集合 Reset 时恢复——
        // 清空布局把 Offset 钳回去的事件可能晚于 Reset 到达,会把刚恢复的跟随再关掉;
        // 构建完成之后只剩内容增长事件,跟随不会再被误关
        if (e.PropertyName == nameof(ConversationViewModel.IsSessionLoading) &&
            _viewModel is { IsSessionLoading: false })
        {
            _autoScrollHolder.Resume();
        }
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

        vm.Tray.AddAttachmentBytes(bitmap.BitmapToBytes());
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
                if (!string.IsNullOrEmpty(path)) vm.Tray.AddAttachmentPath(path);
            }
        }

        e.Handled = true;
    }
}
