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

namespace UiharuMind.Core.Core.SimpleLog;

public static class Log
{
    // public static ILogger? Logger { get; set; }

    public static void Debug(object? message)
    {
        // Logger?.Debug(message.ToString() ?? $"{message} Null message");
        LogManager.Instance.Log(message?.ToString() ?? "Log Print Error: Null message");
    }

    public static void Warning(object? message)
    {
        LogManager.Instance.LogWarning(message?.ToString() ?? "Log Print Error: Null message");
    }

    public static void Error(object? message)
    {
        LogManager.Instance.LogError(message?.ToString() ?? "Log Print Error: Null message");
    }

    public static void CloseAndFlush()
    {
        LogManager.Instance.SaveLog(SettingConfig.LogDataPath);
    }
}