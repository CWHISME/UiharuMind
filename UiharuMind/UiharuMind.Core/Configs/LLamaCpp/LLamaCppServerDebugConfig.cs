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

using System.ComponentModel;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

[DisplayName("Debug Config")]
public class LLamaCppServerDebugConfig : ConfigBase
{
    // [SettingConfigDesc("log disable (default: true)")]
    // [SettingConfigNoneValue]
    // public bool LogDisable { get; set; } = true;

    [SettingConfigDesc("log running info (default: false)")]
    [SettingConfigDesc("运行过程中是否打印日志信息", LanguageUtils.ChineseSimplified)]
    [SettingConfigIgnoreValue]
    public bool LogRunningInfo { get; set; } = false;

    [SettingConfigDesc("Set verbosity level to infinity (i.e. log all messages, useful for debugging)")]
    [SettingConfigNoneValue]
    public bool LogVerbose { get; set; }

    // [SettingConfigDesc("Enable prefx in log messages")]
    // [SettingConfigNoneValue]
    // public bool LogPrefix { get; set; }
}