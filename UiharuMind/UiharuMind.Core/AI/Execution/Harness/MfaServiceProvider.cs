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

namespace UiharuMind.Core.AI.Execution.Harness;

// [MFA绕坑] 绕:框架中间件只从 IServiceProvider 解析日志器 因:不想为一个日志器引入完整 DI 容器 删除条件:框架提供轻量注入口
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
