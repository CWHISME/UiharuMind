using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiharuMind.Features.Models.AudioModels;

/// <summary>
/// 「语音模型」页签：本机装了的语音合成 / 识别模型
/// </summary>
public partial class AudioModelListView : UserControl
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _statusTimer = new() { Interval = StatusInterval };

    public AudioModelListView()
    {
        InitializeComponent();
        _statusTimer.Tick += (_, _) => _ = (DataContext as AudioModelListViewData)?.RefreshStatusAsync();
    }

    // 只在页签看得见时轮询引擎状态（加载、闲置卸载都在引擎那边悄悄发生）
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _statusTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _statusTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
