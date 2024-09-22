using System.Runtime.InteropServices;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.UiharuScreenCapture;
using UiharuMind.Core.Input;
using UiharuMind.Core.LLamaCpp;

namespace UiharuMind.Core;

public class UiharuCoreManager : Singleton<UiharuCoreManager>, IInitialize
{
    public bool IsWindows { get; private set; }
    public bool IsMacOs { get; private set; }

    public SettingConfig Setting { get; private set; }
    // public InputManager Input { get; private set; } = new InputManager();
    // public ChatManager Chat { get; private set; } = new ChatManager();

    public UiharuCoreManager()
    {
        CheckPlatform();
        Setting = SaveUtility.Load<SettingConfig>(typeof(SettingConfig));
    }

    public void OnInitialize()
    {
        // Input.Start();
    }

    /// <summary>
    /// 主动初始化
    /// </summary>
    public void Init()
    {
        Log.Debug("UiharuCoreManager initialized");
    }

    // public async Task CaptureScreen()
    // {
    //     await ScreenCaptureMac.Capture();
    // }
    //
    // /// <summary>
    // /// 对指定屏幕进行全屏截图
    // /// </summary>
    // /// <param name="screenId"></param>
    // public async Task CaptureScreen(int screenId)
    // {
    //     await ScreenCaptureMac.Capture(screenId);
    // }

    private void CheckPlatform()
    {
        IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows); //os.Contains("Windows");
        IsMacOs = !IsWindows && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        //&& (os.Contains("macOS", StringComparison.Ordinal) ||
        //  os.Contains("OS X", StringComparison.Ordinal) ||
        //os.Contains("Darwin", StringComparison.Ordinal));
    }
}