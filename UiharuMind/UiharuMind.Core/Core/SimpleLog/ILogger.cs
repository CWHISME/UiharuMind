namespace UiharuMind.Core.Core.SimpleLog;

public interface ILogger
{
    public void Debug(string rawMessage, LogItem message);
    public void Warning(string rawMessage, LogItem message);

    public void Error(string rawMessage, LogItem message);
}