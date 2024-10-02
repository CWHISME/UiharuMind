using System.Runtime.InteropServices;
using UiharuMind.Core.Configs;
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
    public QuickToolSetting QuickToolSetting { get; private set; }

    public UiharuCoreManager()
    {
        CheckPlatform();
        Setting = SaveUtility.Load<SettingConfig>(typeof(SettingConfig));
        QuickToolSetting = SaveUtility.Load<QuickToolSetting>(typeof(QuickToolSetting));
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

    /// <summary>
    /// 保存设置
    /// </summary>
    public void SaveSetting()
    {
        SaveUtility.Save(typeof(SettingConfig), Setting);
        SaveUtility.Save(typeof(QuickToolSetting), QuickToolSetting);
    }

    private void CheckPlatform()
    {
        IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows); //os.Contains("Windows");
        IsMacOs = !IsWindows && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        //&& (os.Contains("macOS", StringComparison.Ordinal) ||
        //  os.Contains("OS X", StringComparison.Ordinal) ||
        //os.Contains("Darwin", StringComparison.Ordinal));
    }
}