using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Shared.Services.Permissions;

/// <summary>
/// macOS 权限清单：辅助功能与屏幕录制，修复入口是跳转到对应的系统设置页。
/// </summary>
public sealed class MacPermissionProvider : IPlatformPermissionProvider
{
    private readonly PermissionItem _accessibility;
    private readonly PermissionItem _screenRecording;

    public IReadOnlyList<PermissionItem> Items { get; }

    public bool IsInputHookAllowed => MacPermissionService.IsAccessibilityGranted();

    public MacPermissionProvider()
    {
        _accessibility = new PermissionItem
        {
            Name = LocalizationManager.Instance.GetString("PermissionAccessibility"),
            Description = LocalizationManager.Instance.GetString("PermissionAccessibilityDesc"),
            IconName = "settings",
            ActionLabel = LocalizationManager.Instance.GetString("PermissionOpenSettings"),
            ActionCommand = new RelayCommand(MacPermissionService.OpenAccessibilitySettings)
        };

        _screenRecording = new PermissionItem
        {
            Name = LocalizationManager.Instance.GetString("PermissionScreenRecording"),
            Description = LocalizationManager.Instance.GetString("PermissionScreenRecordingDesc"),
            IconName = "image",
            ActionLabel = LocalizationManager.Instance.GetString("PermissionOpenSettings"),
            ActionCommand = new RelayCommand(MacPermissionService.OpenScreenRecordingSettings)
        };

        Items = new List<PermissionItem> { _accessibility, _screenRecording };
    }

    public Task RefreshAsync()
    {
        _accessibility.IsGranted = MacPermissionService.IsAccessibilityGranted();
        _screenRecording.IsGranted = MacPermissionService.IsScreenRecordingGranted();
        return Task.CompletedTask;
    }
}
