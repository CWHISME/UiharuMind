/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 日志条目的内容性质。<b>刻意不分文件</b>——分文件会砍断时序，
/// 而时序是日志唯一不可替代的东西。分类只用于面板筛选与差异化保留。
/// </summary>
public enum ELogCategory
{
    /// <summary>通用事件</summary>
    General,

    /// <summary>发给模型的请求正文</summary>
    LlmRequest,

    /// <summary>模型返回的响应正文</summary>
    LlmResponse,
}
