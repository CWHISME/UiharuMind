namespace UiharuMind.Core.Core.Utils;

public static class PlatformUtils
{
    public static bool IsWindows =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

    public static bool IsLinux => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime
        .InteropServices.OSPlatform
        .Linux);

    public static bool IsMacOS =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform
            .OSX);
}