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
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Runtime.Backends;

public class ChatSettingConfig : TConfigBase<ChatSettingConfig>
{
    
    [SettingConfigDesc("Token for model name")]
    [SettingConfigDesc("用于计算 Token 数量的模型", LanguageUtils.ChineseSimplified)]
    [SettingConfigOptions("gpt-4o", "gpt-4")]
    public string TokenForModelName { get; set; } = "gpt-4o";

    [SettingConfigDesc("alowed multi answer window")]
    [SettingConfigDesc("是否同时允许多个回答窗口存在(如果允许，每次都会开启新界面展示结果)", LanguageUtils.ChineseSimplified)]
    public bool IsAllowMultiAnswerWindow { get; set; } = false;
    
    /// <summary>
    /// 聊天是否只显示纯文本
    /// </summary>
    public bool IsChatPlainText { get; set; } = false;

    /// <summary>
    /// 思考段结束后是否自动折叠(流式进行中始终展开)
    /// </summary>
    public bool IsChatAutoCollapseThinking { get; set; } = true;

    /// <summary>
    /// 一次请求里图片总量越过多少 MiB 就开始裁掉最老的几张。<b>0 表示不按字节裁</b>（张数那条仍生效）。
    ///
    /// 口径是<b>请求体里的长度</b>（base64 之后，约等于原始字节的 4/3），因为撞网关的是请求体。
    /// 默认 8：图片随历史每轮重传，而预览图只进不出；实测两个会话涨到 12.3MB / 14.3MB 之后，
    /// 上游每一发都只回一帧「Error processing stream start」，怎么重试都是同一个 400。
    /// 官方文档给的请求体上限（DeepSeek 48MiB）在这里不作数——撞的是中转网关，限比官方紧得多
    /// </summary>
    [SettingConfigDesc("Trim the oldest images once a request's images exceed this many MiB (0 = not by size)")]
    [SettingConfigDesc("一次请求的图片总量越过多少 MiB 就开始裁掉最老的几张(0 表示不按体积裁)", LanguageUtils.ChineseSimplified)]
    public int ImageBudgetTriggerMiB { get; set; } = 8;

    /// <summary>
    /// 裁就一次裁到这条线以下。与上一项之差就是回差：每裁一次都要断掉一段前缀缓存，
    /// 所以要让下一刀等新图攒够这个差额，而不是每来一张就裁。0 表示用上一项的一半
    /// </summary>
    [SettingConfigDesc("Trim down to this many MiB in one go; the gap to the trigger is the hysteresis")]
    [SettingConfigDesc("一次裁到这条线以下;与上一项的差额就是回差(0 表示取上一项的一半)", LanguageUtils.ChineseSimplified)]
    public int ImageBudgetTargetMiB { get; set; } = 4;

    /// <summary>
    /// 一次请求最多留几张图。纯兜底，正常由字节那一档先触发；挡的是「很多张很小的图」
    /// 以及按张数计费的中转。0 表示不设
    /// </summary>
    [SettingConfigDesc("Keep at most this many images per request (0 = no cap)")]
    [SettingConfigDesc("一次请求最多保留几张图(0 表示不设)", LanguageUtils.ChineseSimplified)]
    public int ImageBudgetMaxCount { get; set; } = 48;

}