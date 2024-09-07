using Microsoft.SemanticKernel;

namespace UiharuMind.Core.Core.Chat;

public struct ChatMessage
{
    /// <summary>
    /// The timestamp of the message in UTC time.
    /// </summary>
    public long Timestamp;

    public ChatMessageContent Message;

    /// <summary>
    /// The local time string of the message in the format of "yyyy-MM-dd HH:mm:ss".
    /// </summary>
    public string LocalTimeString
    {
        get
        {
            DateTime utcTime = new DateTime(Timestamp, DateTimeKind.Utc);
            DateTime localTime = utcTime.ToLocalTime();
            return localTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}