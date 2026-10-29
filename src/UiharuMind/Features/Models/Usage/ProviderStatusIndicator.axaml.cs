using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 顶栏的供应商读数：状态点 + 一行读数 + 计费徽章，点开是明细。
///
/// 数据由 <see cref="ProviderStatusViewData"/> 给（会话页的 <c>ProviderStatus</c>，跟着本页会话的模型走），
/// 控件自己只管「显示成什么」——取数、缓存、退避都在 Core 与读数类里。
/// 本类不走 XAML 绑定而是代码填值：形状照 <see cref="LocalUsageIndicator"/>，
/// 这样 <c>Data</c> 可注入，无头测试不用碰真网络。
/// </summary>
public partial class ProviderStatusIndicator : UserControl
{
    /// <summary>读数来源</summary>
    public static readonly StyledProperty<ProviderStatusViewData?> DataProperty =
        AvaloniaProperty.Register<ProviderStatusIndicator, ProviderStatusViewData?>(nameof(Data));

    private ProviderStatusViewData? _subscribed;
    private readonly DispatcherTimer _ageTick = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// 构造
    /// </summary>
    public ProviderStatusIndicator()
    {
        InitializeComponent();
        // 明细里的「数据 X 前」得自己往上爬：面板开着时每秒重算一次文案（不碰网络）。
        // 秒级跳动归这条定时器，不归公共调度器那一拍——那一拍是给取数用的
        _ageTick.Tick += (_, _) => Data?.RefreshAge();
        // 点开明细就拉一次：面板里的数字得是此刻的，不能是上一轮轮询的。
        // 先把年龄换成「更新中…」——否则会先闪一下上一轮的秒数（47 秒前），
        // 紧接着新数据到了变成 0 秒前，看着像倒计时在倒着跑。
        // （Avalonia 的 FlyoutBase 只有 Opened/Closed，没有 Opening，所以是开出来的那一帧换）
        Trigger.Flyout!.Opened += (_, _) =>
        {
            _ageTick.Start();
            Data?.BeginRefresh();
            _ = Data?.RefreshAsync(force: true);
            // 周期统计只在面板开着时要看：它变化慢，没必要进 30 秒的公共拍子
            _ = Data?.RefreshSummaryAsync();
        };
        Trigger.Flyout.Closed += (_, _) => _ageTick.Stop();
        Update();
    }

    /// <summary>
    /// 数据；由外层（会话页壳）绑定给出，没给就整块不显示
    /// </summary>
    public ProviderStatusViewData? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DataProperty) return;
        Subscribe(Data);
        Data?.SetVisible(true);
        Update();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Data);
        // 顶上可视树就等于「用户在看着这一页」：Avalonia 12 的 IsEffectivelyVisible 是普通
        // CLR 属性 + internal 事件，观察不到，所以用挂上/摘下可视树当判据（ADR 0094）
        Data?.SetVisible(true);
        Update();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ageTick.Stop();
        Data?.SetVisible(false);
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe(ProviderStatusViewData? data)
    {
        if (ReferenceEquals(_subscribed, data)) return;
        if (_subscribed != null) _subscribed.PropertyChanged -= OnDataChanged;
        _subscribed = data;
        if (data != null) data.PropertyChanged += OnDataChanged;
    }

    private void OnDataChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        ProviderStatusViewData? data = Data;
        Panel.DataContext = data;
        if (data == null)
        {
            Trigger.IsVisible = false;
            return;
        }

        Trigger.IsVisible = !data.IsEmpty;
        Readout.Text = data.Readout;
        CachePrice.Text = data.CacheReadPriceText;
        CachePrice.IsVisible = data.HasCacheReadPrice;
        Dot.IsVisible = data.HasDot;
        Dot.Tag = data.DotTag;
        Badge.IsVisible = data.HasBadge;
        Badge.Header = data.BadgeText;
    }
}
