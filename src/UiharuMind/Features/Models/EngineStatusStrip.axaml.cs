using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Models;

/// <summary>
/// 已装模型页签里本地引擎的状态条（语音、生图同一个样子）
/// </summary>
public partial class EngineStatusStrip : UserControl
{
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<EngineStatusStrip, bool>(nameof(IsRunning));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EngineStatusStrip, string?>(nameof(Text));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<EngineStatusStrip, object?>(nameof(Actions));

    public EngineStatusStrip()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 在跑（绿点）；否则灰点
    /// </summary>
    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>
    /// 状态说明
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// 右侧的链接
    /// </summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
