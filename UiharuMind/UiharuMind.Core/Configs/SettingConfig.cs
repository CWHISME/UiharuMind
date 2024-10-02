using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Core;

public class SettingConfig : ConfigBase
{
    public const string SaveDataPath = "./SaveData/";
    public const string SaveChatDataPath = SaveDataPath + "ChatData/";
    public const string LogDataPath = "./SaveLog/";

    /// <summary>
    /// 本地服务引擎目录
    /// </summary>
    public const string BackendRuntimeEnginePath = "./BackendRuntimeEngine/";

    /// <summary>
    /// 是否是本地服务模式
    /// </summary>
    public bool IsLocalServer { get; set; } = true;
}