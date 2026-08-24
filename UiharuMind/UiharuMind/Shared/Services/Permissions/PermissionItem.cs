using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Shared.Services.Permissions;

/// <summary>
/// 权限引导界面上的一行。
/// 修复入口刻意抽象成「一个按钮 + 一条命令」而不是「打开系统设置」：
/// macOS 能直接跳到系统设置页，Linux 没有对应页面，只能把命令交到用户手上。
/// </summary>
public sealed class PermissionItem : ObservableObject
{
    private bool _isGranted;

    /// <summary>权限名称</summary>
    public required string Name { get; init; }

    /// <summary>这项权限用来做什么</summary>
    public required string Description { get; init; }

    /// <summary>图标名，取自 Assets 中的 SVG 图标集</summary>
    public required string IconName { get; init; }

    /// <summary>修复按钮文案</summary>
    public string? ActionLabel { get; init; }

    /// <summary>点击修复按钮时执行的命令；为 null 表示本项没有可执行的修复入口</summary>
    public ICommand? ActionCommand { get; init; }

    /// <summary>是否已获得该权限</summary>
    public bool IsGranted
    {
        get => _isGranted;
        set
        {
            if (!SetProperty(ref _isGranted, value)) return;
            OnPropertyChanged(nameof(IsActionVisible));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>状态文案</summary>
    public string StatusText => LocalizationManager.Instance.GetString(
        IsGranted ? "PermissionGranted" : "PermissionNotGranted");

    /// <summary>修复按钮是否可见：仅在缺权限且确有修复入口时出现</summary>
    public bool IsActionVisible => !IsGranted && ActionCommand != null;
}
