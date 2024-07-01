namespace UiharuMind.Core.Core.SimpleLog;

public static class Log
{
    public static ILogger? Logger { get; set; }

    public static void Debug(object message)
    {
        Logger?.Debug(message.ToString() ?? $"{message} Null message");
    }

    public static void Error(object message)
    {
        Logger?.Error(message.ToString() ?? $"{message} Null message");
    }
}