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
using Avalonia.Threading;
using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.VisualTree;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Diagnostics;
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

    /// <summary>离顶多少像素以内算"滚到顶了"。留一点余量,让续窗在用户撞到顶之前就开始</summary>
    private const double EarlierLoadThreshold = 32.0;

    private readonly ScrollViewerAutoScrollHolder _autoScrollHolder;
    private ConversationViewModel? _viewModel;
    private bool _isLoadingEarlier; //正在续一窗更早的消息(防抖)

    public ConversationView()
    {
        InitializeComponent();
        _autoScrollHolder = new ScrollViewerAutoScrollHolder(Viewer);
        Viewer.ScrollChanged += OnViewerScrollChanged;
        // 探针关着时连事件都不挂:布局回调是每次布局都会跑的路径,不该为一个默认关闭的诊断付钱
        if (StreamPerfProbe.IsEnabled) Viewer.LayoutUpdated += OnViewerLayoutUpdated;
        DataContextChanged += OnDataContextChanged;
        InputBox.PastingFromClipboard += OnPastingFromClipboard;
        DragDrop.SetAllowDrop(ComposerBorder, true);
        ComposerBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ComposerBorder.AddHandler(DragDrop.DropEvent, OnDrop);

        // 点名补全:↑↓ 与 Esc 没有对应的 KeyBinding,走路由事件即可。
        // 回车与 Tab 则相反——它们绑在输入框的 KeyBindings 上,而 Avalonia 由
        // KeyboardDevice.ProcessRawEvent 沿视觉父链在 raise 路由事件之前就处理掉 KeyBindings,
        // 因此那两个键是在 SendMessage/InputExtra 命令入口改道的,不在这里。
        // 发送是 Ctrl+Enter(KeyBinding),裸回车在补全关闭时落回 TextBox 默认换行,
        // 补全开着时由下面的回车分支兜底采纳候选
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
            _viewModel.IsStuckToBottomSource = null;
        }

        _viewModel = DataContext as ConversationViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Palette.SkillCandidateAccepted += OnSkillCandidateAccepted;
            // 滚动状态只有本视图知道,而运行期裁剪要靠它决定能不能裁(见 ConversationItemWindowTrimmer)
            _viewModel.IsStuckToBottomSource = () => _autoScrollHolder.IsStuckToBottom;
        }
    }

    /// <summary>
    /// 首启补一次贴底与实体化。
    ///
    /// 会话是在 <c>ConversationPageDataBase</c> 的构造里就即发即忘启动加载的，视图晚于它创建，
    /// 所以首次打开时 <c>IsSessionLoading</c> 那次翻转很可能<b>没人在听</b>——
    /// <see cref="OnViewModelPropertyChanged"/> 收不到，那一次的贴底与同步实体化就整个丢了，
    /// 表现为第一次打开先显示原文再变 markdown、之后切换都正常。
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_viewModel is not { IsSessionLoading: false } vm || vm.Items.Count == 0) return;

        AnchorToBottom();
        _autoScrollHolder.Resume();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 会话构建完成后恢复跟底。不能在集合 Reset 时恢复——
        // 清空布局把 Offset 钳回去的事件可能晚于 Reset 到达,会把刚恢复的跟随再关掉;
        // 构建完成之后只剩内容增长事件,跟随不会再被误关
        if (e.PropertyName == nameof(ConversationViewModel.IsSessionLoading) &&
            _viewModel is { IsSessionLoading: false })
        {
            AnchorToBottom();
            _autoScrollHolder.Resume();
        }
    }

    /// <summary>
    /// 滚到顶自动续一窗更早的消息。取代原先那个「加载更早」按钮——要用户自己去点才回得去，
    /// 是长会话里最难用的一处。
    /// </summary>
    private void OnViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isLoadingEarlier) return;
        if (DataContext is not ConversationViewModel vm) return;
        if (!vm.HasEarlierMessages || vm.IsSessionLoading) return;
        // 内容还没多到能滚就不续:此时 Offset 恒为 0,不判这一条会一路把整段历史续完
        if (Viewer.Extent.Height <= Viewer.Viewport.Height) return;
        if (Viewer.Offset.Y > EarlierLoadThreshold) return;

        // 不在滚动回调里当场做:前插要跟一次同步 UpdateLayout 才能算补偿量,
        // 而在 ScrollChanged 里同步跑整棵树的布局是自找麻烦。
        // 排到 Loaded 去做,整段落在同一个派发任务里——中间不会渲染出一帧错位的视口
        _isLoadingEarlier = true;
        Dispatcher.UIThread.Post(() => LoadEarlierKeepingViewport(vm), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 同步贴到底，并把可见那一屏的 markdown 一并转完。
    ///
    /// 两件事都必须同步做完再交出去：交给 <c>Loaded</c> 去贴底的话，布局与渲染的优先级
    /// 都高于它，中间必然先画出一帧顶部视口；而贴底后视口内那几条若留给逐帧放行的队列，
    /// 高度会一格一格长高，内容看着从底部闪出来。
    ///
    /// 转 markdown 会改高度、改高度会换掉视口内容，所以要迭代到稳定；
    /// <c>MaxSettlePasses</c> 是保险，防止病态内容把这里拖成死循环
    /// </summary>
    private void AnchorToBottom()
    {
        const int maxSettlePasses = 4;

        for (int pass = 0; pass < maxSettlePasses; pass++)
        {
            Viewer.UpdateLayout(); //拿到真实高度
            ScrollToBottom();
            Viewer.UpdateLayout(); //贴底后视口换了内容,让新的几何落地
            if (!RealizeVisibleBubbles()) break;
        }

        ScrollToBottom();
    }

    /// <summary>
    /// 把此刻落在视口内、还没转 markdown 的气泡当场转掉。
    ///
    /// 不走 <c>SimpleMarkdownViewer</c> 的排队机制：那个队列靠视口通知填充，而视口通知的
    /// 处理器是在气泡 <c>OnLoaded</c> 时订阅的——列表刚建出来时气泡还没 <c>Loaded</c>，
    /// 队列因此是空的，这一屏就会退回逐帧放行，表现为先显示原文再变 markdown。
    /// 这里直接按几何判断，不依赖任何事件时序。
    /// </summary>
    /// <returns>真的转了至少一个返回 true</returns>
    private bool RealizeVisibleBubbles()
    {
        double viewportHeight = Viewer.Viewport.Height;
        if (viewportHeight <= 0) return false;

        bool realizedAny = false;
        foreach (SimpleMarkdownViewer bubble in MessageList.GetVisualDescendants().OfType<SimpleMarkdownViewer>())
        {
            // 换算到 Viewer 自身坐标系,这一步已经把滚动偏移算进去了
            Point? topLeft = bubble.TranslatePoint(default, Viewer);
            if (topLeft == null) continue;

            double top = topLeft.Value.Y;
            if (top + bubble.Bounds.Height < 0 || top > viewportHeight) continue;
            if (bubble.RealizeNow()) realizedAny = true;
        }

        return realizedAny;
    }

    private void ScrollToBottom()
    {
        Viewer.Offset = new Vector(Viewer.Offset.X,
            Math.Max(0, Viewer.Extent.Height - Viewer.Viewport.Height));
    }

    /// <summary>
    /// 续一窗更早的消息，并按前插高度补偿 Offset 以保持视口内容不动
    /// </summary>
    private void LoadEarlierKeepingViewport(ConversationViewModel vm)
    {
        try
        {
            double extentBefore = Viewer.Extent.Height;
            double offsetBefore = Viewer.Offset.Y;
            vm.LoadEarlierMessages();
            Viewer.UpdateLayout();
            Viewer.Offset = new Vector(Viewer.Offset.X, offsetBefore + Viewer.Extent.Height - extentBefore);
        }
        finally
        {
            // 防抖:补偿之后 Offset 通常已经离开顶部,但新的一窗不足一屏高时它仍在顶部——
            // 再排一轮才解锁,免得一次滚动手势连锁把整段历史续完
            Dispatcher.UIThread.Post(() => _isLoadingEarlier = false, DispatcherPriority.Background);
        }
    }

    private void OnViewerLayoutUpdated(object? sender, EventArgs e)
    {
        StreamPerfProbe.ReportLayoutUpdated(_viewModel?.Items.Count ?? 0);
    }

    private async void OnPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        // 剪贴板含图片:以内存字节加入附件,并拦截默认文本粘贴。
        // 取剪贴板图是"产出"语义,这张整屏级的位图归本方法所有,转成字节后当场释放——
        // 附件盘只吃字节,不留位图,所以没人会再用它
        using Bitmap? bitmap = await App.Clipboard.GetImageFromClipboard();
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
