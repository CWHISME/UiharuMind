using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 用于快捷功能设置、包括界面等相关设置
/// </summary>
public class QuickToolSetting : ConfigBase
{
    [SettingConfigDesc(
        "The detection interval for the clipboard, with higher frequency resulting in faster response but greater performance consumption.  \nThis setting is only effective on MacOS, with the unit being seconds, and the default value is 0.5 seconds.")]
    [SettingConfigDesc("剪切板的检测间隔，频率越高反应越快，性能消耗也越大。\n仅 MacOS 有效，单位为秒，默认 0.5 秒", LanguageUtils.ChineseSimplified)]
    [SettingConfigRange(0.01f, 1.0f, 0.01f)]
    public float ClipboardCheckInterval { get; set; } = 0.5f;
}