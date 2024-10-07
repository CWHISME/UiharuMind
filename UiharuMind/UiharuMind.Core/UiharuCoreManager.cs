/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Runtime.InteropServices;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.UiharuScreenCapture;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Input;
using UiharuMind.Core.LLamaCpp;

namespace UiharuMind.Core;

public class UiharuCoreManager : Singleton<UiharuCoreManager>, IInitialize
{
    public bool IsWindows => PlatformUtils.IsWindows;
    public bool IsMacOs => PlatformUtils.IsMacOS;

    // public UiharuCoreManager()
    // {
    //     CheckPlatform();
    //     // Setting = SaveUtility.Load<SettingConfig>(typeof(SettingConfig));
    //     // QuickToolSetting = SaveUtility.Load<QuickToolSetting>(typeof(QuickToolSetting));
    // }

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

    // /// <summary>
    // /// 保存设置
    // /// </summary>
    // public void SaveSetting()
    // {
    //     SaveUtility.Save(typeof(SettingConfig), Setting);
    //     SaveUtility.Save(typeof(QuickToolSetting), QuickToolSetting);
    // }

    // private void CheckPlatform()
    // {
    //     IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows); //os.Contains("Windows");
    //     IsMacOs = !IsWindows && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    //     //&& (os.Contains("macOS", StringComparison.Ordinal) ||
    //     //  os.Contains("OS X", StringComparison.Ordinal) ||
    //     //os.Contains("Darwin", StringComparison.Ordinal));
    // }
}