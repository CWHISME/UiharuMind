namespace UiharuMind.Core.Core.SimpleLog;

public interface ILogger
{
    public void Debug(string message);
    public void Error(string message);
}