/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Input;
using Avalonia.Interactivity;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Features.Conversation.Items;

/// <summary>
/// 思考细节窗口：把一条思考段的<b>全文</b>从卡片里请出来单独看。
///
/// 为什么会有这个窗口：卡片上的思考预览在流式期间只上头部一截（防重排拖死 UI），
/// 想看全量就得有地方去。它跟 <see cref="FullTextWindow"/> 是同一套路子，区别只在
/// 正文是<b>增量追着流走</b>的——通过 <see cref="ThinkingItem.SubscribeContent"/> 订阅，
/// 每次拿到新增片段直接 append 到 <see cref="LongTextView"/>（AvaloniaEdit 内核,
/// 按行虚拟化,几十万字符不卡）。
///
/// 跟底/让开收敛在 <see cref="LongTextView.AppendText"/> 里：追加前在底部才滚底，
/// 用户上翻读旧内容时不打扰，滚回底部自然重新跟上，无状态自收敛。
/// </summary>
public partial class ThinkingDetailWindow : QuickWindowBase
{
    private ThinkingItem? _source;

    /// <summary>
    /// 打开一条思考段的全文窗
    /// </summary>
    /// <param name="item">思考条目（卡片 DataContext）</param>
    public static void Show(ThinkingItem item)
    {
        UIManager.ShowWindow<ThinkingDetailWindow>(x => x.SetSource(item), isMulti: true);
    }

    public ThinkingDetailWindow()
    {
        InitializeComponent();
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
    }

    /// <summary>
    /// 装载思考条目。窗口可被复用（<see cref="UiharuWindowBase.IsCacheWindow"/>），
    /// 换源时退订旧条目、全量装载新条目
    /// </summary>
    /// <param name="item">思考条目</param>
    public void SetSource(ThinkingItem item)
    {
        if (_source == item) return;
        Unsubscribe();
        _source = item;

        TitleTextBlock.Text = Loc.Text("AgentThinking");
        // 订阅与取全量在同一锁内完成:不会因为推理线程在两步之间追加而丢段
        TextView.Text = item.SubscribeContent(OnContentChanged);
        // 思考中打开默认跟底:读者看的是它"正在想什么",最新内容在尾部。
        // 先设 FollowTail 再滚底,后续增量照常跟;用户上翻会让开(见 LongTextView)
        TextView.FollowTail = true;
        TextView.ScrollToEnd();
    }

    private void OnContentChanged(string delta)
    {
        // 订阅回调在 UI 线程触发(StreamFlushPump 的节拍)
        TextView.AppendText(delta);
    }

    private void Unsubscribe()
    {
        if (_source == null) return;
        _source.UnsubscribeContent(OnContentChanged);
        _source = null;
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        // 缓存窗口只是 Hide,不释放——源条目可能已随会话切换被丢弃,必须退订
        Unsubscribe();
    }

    private void InputElement_OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    private void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_source != null) App.Clipboard.CopyToClipboard(_source.CurrentText, true, true);
    }

    private void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        TextView.OpenSearch();
    }
}