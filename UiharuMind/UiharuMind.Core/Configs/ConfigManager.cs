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

using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.AutoClick;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Configs;

public class ConfigManager : Singleton<ConfigManager>
{
    public SettingConfig Setting => SettingConfig.Current;
    public QuickToolSetting QuickToolSetting => QuickToolSetting.Current;
    public QuickToolPromptSetting QuickToolPromptSetting => QuickToolPromptSetting.Current;
    public ChatSettingConfig ChatSetting => ChatSettingConfig.Current;
    public DebugSettingConfig DebugSetting => DebugSettingConfig.Current;

    // public ConfigManager()
    // {
    //     Setting = SaveUtility.LoadOrNew<SettingConfig>(typeof(SettingConfig));
    //     QuickToolSetting = SaveUtility.LoadOrNew<QuickToolSetting>(typeof(QuickToolSetting));
    //     QuickToolPromptSetting = SaveUtility.LoadOrNew<QuickToolPromptSetting>(typeof(QuickToolPromptSetting));
    //     ChatSetting = SaveUtility.LoadOrNew<ChatSettingConfig>(typeof(ChatSettingConfig));
    //     DebugSetting = SaveUtility.LoadOrNew<DebugSettingConfig>(typeof(DebugSettingConfig));
    // }

    // /// <summary>
    // /// 保存设置
    // /// </summary>
    // public void SaveSetting()
    // {
    //     Setting.Save();
    //     QuickToolSetting.Save();
    //     QuickToolPromptSetting.Save();
    //     ChatSetting.Save();
    // }
}