using System.ComponentModel;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

[DisplayName("Debug Config")]
public class LLamaCppServerDebugConfig : ConfigBase
{
    // [SettingConfigDesc("log disable (default: true)")]
    // [SettingConfigNoneValue]
    // public bool LogDisable { get; set; } = true;

    [SettingConfigIgnore] public bool LogRunningInfo { get; set; } = false;

    [SettingConfigDesc("Set verbosity level to infinity (i.e. log all messages, useful for debugging)")]
    [SettingConfigNoneValue]
    public bool LogVerbose { get; set; }

    [SettingConfigDesc("Enable prefx in log messages")]
    [SettingConfigNoneValue]
    public bool LogPrefix { get; set; }
}