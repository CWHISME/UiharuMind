namespace UiharuMind.Core.Core;

public class Log
{
    public static void Debug(object message)
    {
        UiharuCoreManager.Instance.Log(message);
    }
}