namespace UiharuMind.Core.Core.SimpleLog;

public class DefaultLogger : ILogger
{
    public void Debug(string rawMessage, LogItem message)
    {
        Console.WriteLine(message);
    }

    public void Warning(string rawMessage, LogItem message)
    {
        Console.WriteLine(message);
    }

    public void Error(string rawMessage, LogItem message)
    {
        Console.WriteLine(message);
    }
}