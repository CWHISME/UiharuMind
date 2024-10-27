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

using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Core;

public class SettingConfig : ConfigBase
{
    public const string SaveDataPath = "./SaveData/";
    public const string SaveChatDataPath = SaveDataPath + "ChatData/";
    public const string SaveCharacterDataPath = SaveDataPath + "CharacterData/";
    public const string SaveDefaultCharacterDataPath = SaveDataPath + "DefaultCharacterData/";
    public const string LogDataPath = "./SaveLog/";

    /// <summary>
    /// 本地服务引擎目录
    /// </summary>
    public const string BackendRuntimeEnginePath = "./BackendRuntimeEngine/";

    /// <summary>
    /// 是否是本地服务模式
    /// </summary>
    public bool IsLocalServer { get; set; } = true;

    /// <summary>
    /// 聊天是否显示纯文本
    /// </summary>
    public bool IsChatPlainText { get; set; } = false;
}