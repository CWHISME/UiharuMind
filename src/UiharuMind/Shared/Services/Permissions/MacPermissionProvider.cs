using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;

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
            Name = Loc.Text(LangKey.PermissionAccessibility),
            Description = Loc.Text(LangKey.PermissionAccessibilityDesc),
            IconName = "settings",
            ActionLabel = Loc.Text(LangKey.PermissionOpenSettings),
            ActionCommand = new RelayCommand(MacPermissionService.OpenAccessibilitySettings),
            Hint = Loc.Text(LangKey.PermissionAccessibilityStaleHint)
        };

        _screenRecording = new PermissionItem
        {
            Name = Loc.Text(LangKey.PermissionScreenRecording),
            Description = Loc.Text(LangKey.PermissionScreenRecordingDesc),
            IconName = "image",
            ActionLabel = Loc.Text(LangKey.PermissionOpenSettings),
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
