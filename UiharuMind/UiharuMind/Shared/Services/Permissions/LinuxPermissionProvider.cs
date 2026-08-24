using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using UiharuMind.Core.Core.UiharuScreenCapture;
using UiharuMind.Core.Input.Linux;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Shared.Services.Permissions;

/// <summary>
/// Linux 权限清单。
///
/// 与 macOS 的根本差别在于没有可跳转的系统授权页：evdev/uinput 的准入靠用户组与 udev 规则，
/// 只能把命令交到用户手上，因此修复动作退化为「复制命令」。
/// Portal 一项则不是权限而是组件是否安装，同样没有可点的授权入口。
/// </summary>
public sealed class LinuxPermissionProvider : IPlatformPermissionProvider
{
    /// 加入 input 组即可读 /dev/input/event*，重新登录后生效
    private const string InputGroupCommand = "sudo usermod -aG input $USER";

    /// uinput 默认没有对应用户组，需自建组、加规则再重载 udev
    private const string UinputSetupCommand =
        "sudo groupadd -f uinput && sudo usermod -aG uinput $USER && " +
        "echo 'KERNEL==\"uinput\", GROUP=\"uinput\", MODE=\"0660\"' | " +
        "sudo tee /etc/udev/rules.d/99-uiharumind-uinput.rules && " +
        "sudo udevadm control --reload-rules && sudo udevadm trigger";

    private const string PortalInstallCommand = "sudo apt install xdg-desktop-portal xdg-desktop-portal-gnome";

    private readonly PermissionItem _inputDevices;
    private readonly PermissionItem _uinput;
    private readonly PermissionItem _portal;

    public IReadOnlyList<PermissionItem> Items { get; }

    /// Linux 上钩子失败是软失败（收不到事件而已，不会被系统拦），
    /// 因此无条件放行，让它自己跑出结果，缺权限的提示交给本清单
    public bool IsInputHookAllowed => true;

    public LinuxPermissionProvider(Action<string> copyCommand)
    {
        _inputDevices = new PermissionItem
        {
            Name = LocalizationManager.Instance.GetString("PermissionLinuxInputDevices"),
            Description = LocalizationManager.Instance.GetString("PermissionLinuxInputDevicesDesc"),
            IconName = "settings",
            ActionLabel = LocalizationManager.Instance.GetString("PermissionCopyCommand"),
            ActionCommand = new RelayCommand(() => copyCommand(InputGroupCommand))
        };

        _uinput = new PermissionItem
        {
            Name = LocalizationManager.Instance.GetString("PermissionLinuxUinput"),
            Description = LocalizationManager.Instance.GetString("PermissionLinuxUinputDesc"),
            IconName = "settings",
            ActionLabel = LocalizationManager.Instance.GetString("PermissionCopyCommand"),
            ActionCommand = new RelayCommand(() => copyCommand(UinputSetupCommand))
        };

        _portal = new PermissionItem
        {
            Name = LocalizationManager.Instance.GetString("PermissionLinuxPortal"),
            Description = LocalizationManager.Instance.GetString("PermissionLinuxPortalDesc"),
            IconName = "image",
            ActionLabel = LocalizationManager.Instance.GetString("PermissionCopyCommand"),
            ActionCommand = new RelayCommand(() => copyCommand(PortalInstallCommand))
        };

        Items = new List<PermissionItem> { _inputDevices, _uinput, _portal };
    }

    public async Task RefreshAsync()
    {
        var capabilities = LinuxInputCapabilities.Probe();
        _inputDevices.IsGranted = capabilities.CanReadInputDevices;
        _uinput.IsGranted = capabilities.CanWriteUinput;
        _portal.IsGranted = await ScreenCaptureLinux.IsPortalAvailableAsync();
    }
}
