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
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Configs;

public class ConfigManager : Singleton<ConfigManager>, IInitialize
{
    public SettingConfig Setting { get; private set; }
    public QuickToolSetting QuickToolSetting { get; private set; }
    public ChatSettingConfig ChatSetting { get; private set; }

    public void OnInitialize()
    {
        Setting = SaveUtility.Load<SettingConfig>(typeof(SettingConfig));
        QuickToolSetting = SaveUtility.Load<QuickToolSetting>(typeof(QuickToolSetting));
        ChatSetting = SaveUtility.Load<ChatSettingConfig>(typeof(ChatSettingConfig));
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    public void SaveSetting()
    {
        SaveUtility.Save(typeof(SettingConfig), Setting);
        SaveUtility.Save(typeof(QuickToolSetting), QuickToolSetting);
        SaveUtility.Save(typeof(ChatSettingConfig), ChatSetting);
    }
}