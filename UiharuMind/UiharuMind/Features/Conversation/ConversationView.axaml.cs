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
using System.IO;
using System.Linq;
using Avalonia.VisualTree;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Diagnostics;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;

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
        // 整块会话区域都接受文件拖放(不只是输入区):消息流、空白、头部都能放手,
        // 统一进附件盘/输入框。非文件拖放(文本等)仍交给子控件自己处理,见 OnDragOver
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

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

            // 换会话就恢复跟底。Viewer 与跟底状态是全局唯一那一份,上一个会话里"用户上滚过"
            // 这件事不该跟着传给下一个——空态尤其:它没有内容可贴底,也就没有那次
            // AnchorToBottom 帮它恢复,而首轮回复要靠跟底才跟得上
            _autoScrollHolder.Resume();

            // 切回一个缓存实例(后台跑着的那些)是**不走加载的**:IsSessionLoading 一次都不翻,
            // 于是下面那个处理器收不到通知,贴底与实体化整个丢掉——而 Viewer 与跟底状态
            // 是全局唯一那一份,还留着上一个会话的 offset。这里补上
            if (_viewModel is { IsSessionLoading: false } cached && cached.Items.Count > 0) ScheduleSettle(cached);
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

        SettleNow(vm);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 会话构建完成后恢复跟底。不能在集合 Reset 时恢复——
        // 清空布局把 Offset 钳回去的事件可能晚于 Reset 到达,会把刚恢复的跟随再关掉;
        // 构建完成之后只剩内容增长事件,跟随不会再被误关
        if (e.PropertyName == nameof(ConversationViewModel.IsSessionLoading) &&
            _viewModel is { IsSessionLoading: false } vm)
        {
            SettleNow(vm);
        }
    }

    /// <summary>
    /// 换 DataContext 之后再落位。
    ///
    /// <b>不能在 <c>DataContextChanged</c> 里当场做。</b>那一刻 <c>MessageList.ItemsSource</c>
    /// 还是上一个会话的集合——Avalonia 把本控件的 <c>DataContextChanged</c> 排在子控件绑定
    /// 更新<b>之前</b>（实测：事件里读到的是 null，赋值语句返回后才是新集合）。当场贴底就是
    /// 贴在旧内容上，等绑定落地、列表整体重建，offset 成了陈旧值，看着就是「从底部闪上来又弹回去」。
    ///
    /// 优先级取 <see cref="DispatcherPriority.Normal"/>(8)：它是个新的派发任务，绑定必已落地；
    /// 而它<b>高于</b> <see cref="DispatcherPriority.Render"/>(4)，中间不会渲染出错的一帧。
    /// 换成 <c>Loaded</c>(1) 或 <c>Background</c>(-2) 都低于 Render，那才会先画错一帧再纠正
    /// （<see cref="AnchorToBottom"/> 注释里记的就是这个坑）。
    /// </summary>
    /// <param name="vm">已就绪的视图模型</param>
    private void ScheduleSettle(ConversationViewModel vm)
    {
        long queuedAt = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_viewModel, vm)) return;

            global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End("conversation/settle-delay", queuedAt);
            SettleNow(vm);
        }, DispatcherPriority.Normal);
    }

    /// <summary>
    /// 让消息列表就位：同步贴到底并把可见那一屏转完，恢复自动跟底，再把不足一屏的部分补上。
    ///
    /// 三个调用点（首启、加载完成、切回缓存实例）此前各写一份，而<b>第三个当初漏了</b>——
    /// 收敛成一处，下一次加一个入口就不会再漏。切回缓存实例那一路必须经
    /// <see cref="ScheduleSettle"/> 进来，不能直接调本方法。
    /// </summary>
    /// <param name="vm">已就绪的视图模型</param>
    private void SettleNow(ConversationViewModel vm)
    {
        long settleBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
        AnchorToBottom();
        _autoScrollHolder.Resume();
        global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End($"conversation/settle:items={vm.Items.Count}", settleBegin);
        ScheduleViewportTopUp(vm);
    }

    /// <summary>
    /// 贴底之后把不足一屏的部分补上。
    ///
    /// 切会话的冻结压倒性地在布局上（见 <see cref="AnchorToBottom"/> 的实测），所以关键路径上
    /// 只给一屏：加载路径给首屏（<c>HistoryWindow.FirstScreenSize</c>），切回缓存实例则由运行期裁剪
    /// 压到首屏量级（<c>ConversationItemWindowTrimmer.DefaultBackgroundMaxItems</c>）。
    /// 两条路径欠下的都在这里还——用户已经看见内容了，这段布局落在他读第一屏的时间里。
    /// 排到 Background 而不是当场做：当场做等于没分批
    /// </summary>
    private void ScheduleViewportTopUp(ConversationViewModel vm)
    {
        const int maxTopUpPasses = 4;

        Dispatcher.UIThread.Post(() =>
        {
            // 期间可能已经换了会话:那份补齐属于旧的视图模型,别插到新会话的列表上。
            // 不能用 _isLoadingEarlier 挡在派发之前——那样会连着把下一个会话的补齐也吞掉
            if (!ReferenceEquals(_viewModel, vm)) return;

            long topUpBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
            int topUpPasses = 0;
            for (int pass = 0; pass < maxTopUpPasses; pass++)
            {
                topUpPasses++;
                // 已够一屏就能滚:剩下的交给"滚到顶自动续窗",不用在这里多插一次布局
                if (Viewer.Extent.Height > Viewer.Viewport.Height) break;

                // 与"滚到顶自动续窗"互斥:补齐期间来的滚动不该再续一窗(标志由补偿路径解锁)
                _isLoadingEarlier = true;
                // 先还首屏那笔账,不够一屏再按窗续。续窗这一路是必须的兜底:
                // 运行期裁剪之后首屏账已经清零(SetStart),而裁到不足一屏就滚不动,
                // 滚不动则 OnViewerScrollChanged 那条自动续窗永远不触发——更早的消息就再也回不来了
                if (!PrependKeepingViewport(() => vm.FillFirstWindow() || vm.LoadEarlierMessages())) break;
            }

            global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End($"conversation/topup:passes={topUpPasses},items={MessageList.ItemCount}", topUpBegin);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 滚到顶自动续一窗更早的消息。取代原先那个「加载更早」按钮——要用户自己去点才回得去，
    /// 是长会话里最难用的一处。
    /// </summary>
    private void OnViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isLoadingEarlier) return;
        // extent 增长是"内容变多要贴底",不是"用户滚到顶"。初始贴底时 AnchorToBottom 第一次
        // UpdateLayout 会让 extent 首度长高,此刻 Offset 还在顶部(0),若不当心会把这一窗续掉,
        // 表现为首屏 5 条贴完底后进度条又涨成 10 条
        if (e.ExtentDelta.Y > 0) return;
        if (DataContext is not ConversationViewModel vm) return;
        if (!vm.HasEarlierMessages || vm.IsSessionLoading) return;
        // 内容还没多到能滚就不续:此时 Offset 恒为 0,不判这一条会一路把整段历史续完
        if (Viewer.Extent.Height <= Viewer.Viewport.Height) return;
        if (Viewer.Offset.Y > EarlierLoadThreshold) return;

        // 不在滚动回调里当场做:前插要跟一次同步 UpdateLayout 才能算补偿量,
        // 而在 ScrollChanged 里同步跑整棵树的布局是自找麻烦。
        // 排到 Loaded 去做,整段落在同一个派发任务里——中间不会渲染出一帧错位的视口
        _isLoadingEarlier = true;
        Dispatcher.UIThread.Post(() => PrependKeepingViewport(vm.LoadEarlierMessages), DispatcherPriority.Loaded);
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

        long anchorBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();

        // 这一段的开销压倒性地来自布局:实测两轮共 274ms,其中 231ms 是四次 UpdateLayout,
        // 实体化连 markdown 解析只占 43ms(解析本身仅 14ms)。试过把每轮的两次布局并成一次,
        // 三次实测 258/277/258ms —— 没有收益,因为钱都在"加了一窗条目之后的第一次布局"上,
        // 后续几次的 measure 缓存大多有效。而且那样会在高度变化后用过时的 extent 贴底。
        int passes = 0;

        for (int pass = 0; pass < maxSettlePasses; pass++)
        {
            passes++;
            Viewer.UpdateLayout(); //拿到真实高度
            ScrollToBottom();
            Viewer.UpdateLayout(); //贴底后视口换了内容,让新的几何落地
            if (!RealizeVisibleBubbles()) break;
        }

        ScrollToBottom();
        global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End($"conversation/anchor:passes={passes},items={MessageList.ItemCount}", anchorBegin);
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
    /// 前插一段历史，并按前插高度补偿 Offset 以保持视口内容不动。
    /// 跟底时同一个补偿量正好把视口留在底部，所以续窗与首屏补齐共用这一条路径
    /// </summary>
    /// <param name="prepend">真正做前插的动作（续更早 / 补齐首屏），什么都没插时返回 false</param>
    /// <returns>真的插了并补偿过返回 true（调用方据此决定要不要再补一轮）</returns>
    private bool PrependKeepingViewport(Func<bool> prepend)
    {
        try
        {
            double extentBefore = Viewer.Extent.Height;
            double offsetBefore = Viewer.Offset.Y;
            if (!prepend()) return false; //没插进东西就不必为补偿跑一次全量布局
            Viewer.UpdateLayout();
            Viewer.Offset = new Vector(Viewer.Offset.X, offsetBefore + Viewer.Extent.Height - extentBefore);
            return true;
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
        // 只在文件拖放时介入:文本/URI 等拖放交由子控件(如输入框)自己决定,
        // 根控件把 effects 统一设成 None 会覆盖掉它们的处理。非文件拖放顺带收起残留在高亮
        bool isFile = e.DataTransfer.Formats.Any(f => f == DataFormat.File);
        DropOverlay.IsVisible = isFile;
        if (!isFile) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave 是冒泡路由事件:指针在根内部跨过子控件边界也会冒泡上来,不能见着就收。
        // 只有指针真的离开整个会话区(位置超出本控件 bounds)才收起蒙版,否则内部移动会反复闪烁
        Point p = e.GetPosition(this);
        if (p.X < 0 || p.Y < 0 || p.X > Bounds.Width || p.Y > Bounds.Height)
            DropOverlay.IsVisible = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (!e.DataTransfer.Formats.Any(f => f == DataFormat.File)) return;
        if (DataContext is not ConversationViewModel vm) return;

        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetRaw(DataFormat.File) is IStorageItem storageItem)
            {
                string? path = storageItem.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) continue;

                // 目录:不进附件盘(附件盘只收文件,目录没有可预览/可读取的内容),
                // 直接把路径文本附加到输入框,由用户自己决定何时发给谁
                if (Directory.Exists(path))
                {
                    vm.InputText = string.IsNullOrWhiteSpace(vm.InputText)
                        ? path
                        : $"{vm.InputText}\n{path}";
                    continue;
                }

                vm.Tray.AddAttachmentPath(path);
            }
        }

        e.Handled = true;
    }
}
