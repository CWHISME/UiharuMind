using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Shared.Services.Permissions;

/// <summary>
/// 按当前平台创建权限清单提供器
/// </summary>
public static class PlatformPermissionProviderFactory
{
    /// <summary>
    /// 创建当前平台的权限清单提供器
    /// </summary>
    /// <returns>对应平台的实现</returns>
    public static IPlatformPermissionProvider Create()
    {
        if (PlatformUtils.IsMacOS) return new MacPermissionProvider();
        if (PlatformUtils.IsLinux) return new LinuxPermissionProvider(CopyCommandToClipboard);
        return new GrantedPermissionProvider();
    }

    private static void CopyCommandToClipboard(string command)
    {
        App.Clipboard.CopyToClipboard(command, true);
    }
}
