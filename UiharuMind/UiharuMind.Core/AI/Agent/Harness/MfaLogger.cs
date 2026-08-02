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

namespace UiharuMind.Core.AI.Agent.Harness;

// [MFA绕坑] 绕:框架内部日志(含工具执行失败的真实异常)默认无处可去 因:框架只认 ILoggerFactory/IServiceProvider 注入 删除条件:框架提供更直接的日志回调
/// <summary>
/// 把 Microsoft.Agents.AI / Microsoft.Extensions.AI 等插件库的内部日志转发到 UiharuMind 自有日志,
/// 使框架运行期异常(如工具执行失败)可见。
/// </summary>
internal sealed class MfaLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly string _category;

    public MfaLogger(string category)
    {
        _category = category;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);
        string prefix = $"[MFA:{_category}] {message}";

        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
            case LogLevel.Information:
                UiharuMind.Core.Core.SimpleLog.Log.Debug(prefix);
                break;
            case LogLevel.Warning:
                UiharuMind.Core.Core.SimpleLog.Log.Warning(prefix);
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                if (exception is not null)
                    UiharuMind.Core.Core.SimpleLog.Log.Error($"{prefix}\n{exception}");
                else
                    UiharuMind.Core.Core.SimpleLog.Log.Error(prefix);
                break;
            default:
                UiharuMind.Core.Core.SimpleLog.Log.Debug(prefix);
                break;
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
