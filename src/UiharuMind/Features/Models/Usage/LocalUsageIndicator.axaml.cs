using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 本机资源读数：默认显示整机可用内存与 CPU（模型页右上角）；<see cref="ShowsModel"/> 时只显示 <see cref="ModelName"/>
/// 这个本地对话模型的占用，它没在跑就藏起来（对话页模型栏）。点开都是同一张明细。
/// 看得见时每隔几秒刷一次，页面藏着（视图常驻、只切可见性）就不刷
/// </summary>
public partial class LocalUsageIndicator : UserControl
{
    public static readonly StyledProperty<LocalUsageData?> DataProperty =
        AvaloniaProperty.Register<LocalUsageIndicator, LocalUsageData?>(nameof(Data));

    public static readonly StyledProperty<string?> ModelNameProperty =
        AvaloniaProperty.Register<LocalUsageIndicator, string?>(nameof(ModelName));

    public static readonly StyledProperty<bool> ShowsModelProperty =
        AvaloniaProperty.Register<LocalUsageIndicator, bool>(nameof(ShowsModel));

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _refresh = new() { Interval = RefreshInterval };
    private LocalUsageData? _subscribed;

    public LocalUsageIndicator()
    {
        InitializeComponent();
        _refresh.Tick += (_, _) => RefreshIfVisible();
        Trigger.Flyout!.Opened += (_, _) => _ = Data?.RefreshAsync(true);
        Update();
    }

    /// <summary>
    /// 数据；不给就用全应用共用的那份
    /// </summary>
    public LocalUsageData? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// 只显示这个对话模型的占用时，它的名字
    /// </summary>
    public string? ModelName
    {
        get => GetValue(ModelNameProperty);
        set => SetValue(ModelNameProperty, value);
    }

    /// <summary>
    /// 只显示 <see cref="ModelName"/> 的占用，而不是整机余量
    /// </summary>
    public bool ShowsModel
    {
        get => GetValue(ShowsModelProperty);
        set => SetValue(ShowsModelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty) Subscribe(Data);
        if (change.Property == DataProperty || change.Property == ModelNameProperty ||
            change.Property == ShowsModelProperty)
            Update();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Data ??= LocalUsageData.Shared;
        Subscribe(Data);
        _refresh.Start();
        RefreshIfVisible();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _refresh.Stop();
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshIfVisible()
    {
        if (IsEffectivelyVisible) _ = Data?.RefreshAsync();
    }

    private void Subscribe(LocalUsageData? data)
    {
        if (ReferenceEquals(_subscribed, data)) return;
        if (_subscribed != null) _subscribed.PropertyChanged -= OnDataChanged;
        _subscribed = data;
        if (data != null) data.PropertyChanged += OnDataChanged;
    }

    private void OnDataChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LocalUsageData.Summary) or nameof(LocalUsageData.Rows)
            or nameof(LocalUsageData.PressureTag))
            Update();
    }

    // 藏的是按钮不是自己：自己藏了 IsEffectivelyVisible 就一直是假，再也刷不回来
    private void Update()
    {
        LocalUsageData? data = Data;
        Panel.DataContext = data;
        string text = data == null
            ? ""
            : ShowsModel
                ? data.RowOf(ModelName)?.Usage is { } usage ? LocalUsageRow.UsageText(usage) : ""
                : data.Summary;
        Readout.Text = text;
        Dot.Tag = data?.PressureTag ?? "Ready";
        Trigger.IsVisible = text.Length > 0;
    }
}
