namespace UiharuMind.Core.AI.Core;

public struct ChatStreamingMessageInfo
{
    public string Message = String.Empty;
    public int TokenCount = 0;

    public ChatStreamingMessageInfo()
    {
    }

    public ChatStreamingMessageInfo(string message)
    {
        Message = message;
    }
}