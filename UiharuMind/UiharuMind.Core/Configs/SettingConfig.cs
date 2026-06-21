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
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core;

public class SettingConfig : ConfigBase
{
    public const string DefaultCaptureScreenShortcut = "Alt+Shift+Z";
    public const string DefaultQuickStartChatShortcut = "Alt+Shift+A";
    public const string DefaultClipboardHistoryShortcut = "Alt+Shift+S";
    public const string DefaultQuickTranslationShortcut = "Alt+Shift+Q";
    public const string DefaultQuickAutoClickShortcut = "Alt+Shift+G";

    public static readonly string RootDataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UiharuMind/");

    public static readonly string SaveDataPath = Path.Combine(RootDataPath, "SaveData/");
    public static readonly string SaveChatDataPath = SaveDataPath + "ChatData/";
    public static readonly string SaveCharacterDataPath = SaveDataPath + "CharacterData/";
    public static readonly string SaveDefaultCharacterDataPath = SaveDataPath + "DefaultCharacterData/";
    public static readonly string SaveAutoClickDataPath = SaveDataPath + "AutoClickData/";
    public static readonly string LogDataPath = Path.Combine(RootDataPath, "SaveLog/");
    public static readonly string MemoryDataPath = Path.Combine(SaveDataPath, "MemoryData/");
    public static readonly string SaveClipboardHistoryImagePath = Path.Combine(SaveDataPath, "ClipboardHistoryImage/");

    /// <summary>
    /// 本地服务引擎目录
    /// </summary>
    public static readonly string
        BackendRuntimeEnginePath = Path.Combine(RootDataPath, "Runtime/"); //"./BackendRuntimeEngine/";

    /// <summary>
    /// 知识库路径
    /// </summary>
    public static readonly string MemoryEmbededPath = Path.Combine(SaveDataPath, "MemoryEmbededCache/");

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

    private string _languageCode = LanguageUtils.GetSupportedCultureOrDefault(null).Name;
    private string _themeMode = "Default";

    /// <summary>
    /// 应用界面语言
    /// </summary>
    public string LanguageCode
    {
        get => _languageCode;
        set
        {
            _languageCode = LanguageUtils.GetSupportedCultureOrDefault(value).Name;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>
    /// 应用主题模式：Default / Light / Dark / Aquatic / Desert / Dusk / NightSky
    /// </summary>
    public string ThemeMode
    {
        get => _themeMode;
        set
        {
            _themeMode = value is "Light" or "Dark" or "Aquatic" or "Desert" or "Dusk" or "NightSky"
                ? value
                : "Default";
            OnPropertyChanged();
            Save();
        }
    }

    private bool _enableFullscreenGameInputSupport;

    /// <summary>
    /// Windows 下是否启动时请求管理员权限，以提升全屏/管理员游戏输入兼容性
    /// </summary>
    public bool EnableFullscreenGameInputSupport
    {
        get => _enableFullscreenGameInputSupport;
        set
        {
            if (_enableFullscreenGameInputSupport == value) return;
            _enableFullscreenGameInputSupport = value;
            OnPropertyChanged();
            Save();
        }
    }

    private string _captureScreenShortcut = DefaultCaptureScreenShortcut;
    private string _quickStartChatShortcut = DefaultQuickStartChatShortcut;
    private string _clipboardHistoryShortcut = DefaultClipboardHistoryShortcut;
    private string _quickTranslationShortcut = DefaultQuickTranslationShortcut;
    private string _quickAutoClickShortcut = DefaultQuickAutoClickShortcut;

    public string CaptureScreenShortcut
    {
        get => _captureScreenShortcut;
        set
        {
            if (_captureScreenShortcut == value) return;
            _captureScreenShortcut = value;
            OnPropertyChanged();
        }
    }

    public string QuickStartChatShortcut
    {
        get => _quickStartChatShortcut;
        set
        {
            if (_quickStartChatShortcut == value) return;
            _quickStartChatShortcut = value;
            OnPropertyChanged();
        }
    }

    public string ClipboardHistoryShortcut
    {
        get => _clipboardHistoryShortcut;
        set
        {
            if (_clipboardHistoryShortcut == value) return;
            _clipboardHistoryShortcut = value;
            OnPropertyChanged();
        }
    }

    public string QuickTranslationShortcut
    {
        get => _quickTranslationShortcut;
        set
        {
            if (_quickTranslationShortcut == value) return;
            _quickTranslationShortcut = value;
            OnPropertyChanged();
        }
    }

    public string QuickAutoClickShortcut
    {
        get => _quickAutoClickShortcut;
        set
        {
            if (_quickAutoClickShortcut == value) return;
            _quickAutoClickShortcut = value;
            OnPropertyChanged();
        }
    }

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
