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

using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Runtime.Backends;

public class DebugSettingConfig : TConfigBase<DebugSettingConfig>
{
    [SettingConfigDesc("log running info to console")]
    [SettingConfigDesc("运行过程中打印日志等级", LanguageUtils.ChineseSimplified)]
    [SettingConfigIgnoreValue]
    public ELogType LogTypeInfo { get; set; } = ELogType.Warning;

    /// <summary>
    /// 会话流式性能探针。开着才采样，关着时连事件都不挂——
    /// 它是长期回归看板，不是一次性排查代码，所以留在设置里而不是靠改代码开关。
    /// </summary>
    [SettingConfigDesc("log conversation streaming performance metrics")]
    [SettingConfigDesc("打印会话流式性能指标(条目数/上屏延迟/布局耗时)", LanguageUtils.ChineseSimplified)]
    public bool IsConversationPerfProbeEnabled { get; set; } = true;
}