/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.SimpleLog;

public static class Log
{
    public static void Debug(object? message, ELogCategory category = ELogCategory.General)
    {
        LogManager.Instance.Log(message?.ToString() ?? "Log Print Error: Null message", category);
    }

    public static void Warning(object? message, ELogCategory category = ELogCategory.General)
    {
        LogManager.Instance.LogWarning(message?.ToString() ?? "Log Print Error: Null message", category);
    }

    public static void Error(object? message, ELogCategory category = ELogCategory.General)
    {
        LogManager.Instance.LogError(message?.ToString() ?? "Log Print Error: Null message", category);
    }

    /// <summary>
    /// 把缓冲推给操作系统。<b>只刷不轮换</b>——旧实现顺带做了轮换与配置保存，
    /// 而它挂在会吞掉异常、不退出进程的 UI 线程异常处理器上，
    /// 一次被吞掉的异常就会覆盖掉上一次运行的日志
    /// </summary>
    public static void Flush()
    {
        LogManager.Instance.Flush();
    }

    /// <summary>停止写入线程并落盘。只在进程退出时调用</summary>
    public static void Shutdown()
    {
        LogManager.Instance.Shutdown();
    }
}
