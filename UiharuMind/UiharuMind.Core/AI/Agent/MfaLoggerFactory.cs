/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Microsoft.Extensions.Logging;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 创建 <see cref="MfaLogger"/> 的工厂,供插件库(HarnessAgent / FunctionInvokingChatClient 等)通过
/// <see cref="ILoggerFactory"/> 与 <see cref="IServiceProvider"/> 获取,从而把库内部日志接入 UiharuMind 日志。
/// </summary>
internal sealed class MfaLoggerFactory : Microsoft.Extensions.Logging.ILoggerFactory
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new MfaLogger(categoryName);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }
}
