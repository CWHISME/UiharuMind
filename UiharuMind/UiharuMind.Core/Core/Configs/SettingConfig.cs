using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.Core;

public class SettingConfig : ConfigBase
{
    public const string SaveDataPath = "./SaveData/";
    public const string LogDataPath = "./SaveLog/";

    /// <summary>
    /// 后端路径
    /// </summary>
    public const string BakendPath = "./Bakend/";

    /// <summary>
    /// 是否是本地服务模式
    /// </summary>
    public bool IsLocalServer { get; set; } = true;
}