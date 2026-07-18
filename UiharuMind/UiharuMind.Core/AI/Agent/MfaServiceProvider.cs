/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 极简 IServiceProvider,仅暴露我们注入的 ILoggerFactory,供插件库中间件
/// (如 FunctionInvokingChatClient)通过 services.GetService&lt;ILoggerFactory&gt;() 获取日志器。
/// </summary>
internal sealed class MfaServiceProvider : IServiceProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public MfaServiceProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Microsoft.Extensions.Logging.ILoggerFactory) || serviceType == typeof(Microsoft.Extensions.Logging.ILoggerProvider))
            return _loggerFactory;
        return null;
    }
}
