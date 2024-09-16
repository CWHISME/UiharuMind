namespace UiharuMind.Core.Core.SimpleLog;

public class DefaultLogger : ILogger
{
    public void Debug(string message)
    {
        Console.WriteLine(message);
    }

    public void Warning(string message)
    {
        Console.WriteLine(message);
    }

    public void Error(string message)
    {
        Console.WriteLine(message);
    }
}