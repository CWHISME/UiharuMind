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
    public static readonly string RootDataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UiharuMind/");

    public static readonly string SaveDataPath = Path.Combine(RootDataPath, "SaveData/");
    public static readonly string SaveChatDataPath = SaveDataPath + "ChatData/";
    public static readonly string SaveCharacterDataPath = SaveDataPath + "CharacterData/";
    public static readonly string SaveDefaultCharacterDataPath = SaveDataPath + "DefaultCharacterData/";
    public static readonly string LogDataPath = Path.Combine(RootDataPath, "SaveLog/");

    /// <summary>
    /// 本地服务引擎目录
    /// </summary>
    public static readonly string
        BackendRuntimeEnginePath = Path.Combine(RootDataPath, "Runtime/"); //"./BackendRuntimeEngine/";

    /// <summary>
    /// 知识库路径
    /// </summary>
    public static readonly string MemoryPath = Path.Combine(RootDataPath, "Memory/");


    /// <summary>
    /// 是否是本地服务模式
    /// </summary>
    public bool IsLocalServer { get; set; } = true;

    /// <summary>
    /// 聊天是否只显示纯文本
    /// </summary>
    public bool IsChatPlainText { get; set; } = false;

    /// <summary>
    /// 聊天是否隐藏模型思考过程
    /// </summary>
    public bool IsChatNotShowThinking { get; set; } = false;

    private bool _isCharacterPhotoListView;

    /// <summary>
    /// 角色列表是否显示以图片形式显示
    /// </summary>
    public bool IsCharacterPhotoListView
    {
        get => _isCharacterPhotoListView;
        set
        {
            _isCharacterPhotoListView = value;
            Save();
        }
    }

    private int _characterFilterIndex;

    /// <summary>
    /// 角色列表筛选方式的索引
    /// </summary>
    public int CharacterFilterIndex
    {
        get => _characterFilterIndex;
        set
        {
            _characterFilterIndex = value;
            Save();
        }
    }
}