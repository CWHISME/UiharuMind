namespace UiharuMind.Core.Core.SimpleLog;

public static class Log
{
    // public static ILogger? Logger { get; set; }

    public static void Debug(object message)
    {
        // Logger?.Debug(message.ToString() ?? $"{message} Null message");
        LogManager.Instance.Log(message.ToString() ?? $"{message} Null message");
    }

    public static void Warning(object message)
    {
        LogManager.Instance.LogWarning(message.ToString() ?? $"{message} Null message");
    }

    public static void Error(object message)
    {
        LogManager.Instance.LogError(message.ToString() ?? $"{message} Null message");
    }

    public static void CloseAndFlush()
    {
        LogManager.Instance.SaveLog(SettingConfig.LogDataPath);
    }
}