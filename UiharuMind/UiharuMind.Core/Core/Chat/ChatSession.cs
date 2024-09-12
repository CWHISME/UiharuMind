using System.Collections;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.ChatCompletion;

namespace UiharuMind.Core.Core.Chat;

/// <summary>
/// 表示一个对话
/// </summary>
public class ChatSession //: IEnumerable<ChatMessage>
{
    /// <summary>
    /// 本条对话名字
    /// </summary>
    [JsonInclude]
    public string Name { get; set; }

    /// <summary>
    /// 本条对话描述
    /// </summary>
    [JsonInclude]
    public string Description { get; set; }

    /// <summary>
    /// 对应的角色 ID
    /// </summary>
    [JsonInclude]
    public long CharaterId { get; set; }

    [JsonInclude] private ChatHistory History { get; set; } = new ChatHistory();

    //以 UTC 格式存储的时间戳
    [JsonInclude] private List<long> TimeStamps { get; set; } = new List<long>();

    public DateTime FirstTime => TimeStamps.Count > 0 ? new DateTime(TimeStamps[0], DateTimeKind.Utc) : DateTime.Now;
    public DateTime LastTime => TimeStamps.Count > 0 ? new DateTime(TimeStamps[^1], DateTimeKind.Utc) : DateTime.Now;

    private readonly SessionEnumerator _enumerator;

    public ChatSession()
    {
        _enumerator = new SessionEnumerator(this);
    }

    public int Count => History.Count;

    public ChatMessage this[int index] => new ChatMessage()
    {
        Message = History[index],
        Timestamp = TimeStamps[index]
    };

    public void AddMessage(AuthorRole authorRole, string message)
    {
        History.AddMessage(authorRole, message);
        TimeStamps.Add(DateTime.UtcNow.Ticks);
    }

    public void Save()
    {
        SaveUtility.SaveChat(this);
    }

    public IEnumerator<ChatMessage> GetEnumerator()
    {
        return _enumerator;
    }

    // IEnumerator IEnumerable.GetEnumerator()
    // {
    //     return GetEnumerator();
    // }

    private class SessionEnumerator(ChatSession session) : IEnumerator<ChatMessage>
    {
        private int _index = -1;
        private ChatMessage _current;

        // _current = default(T);

        public ChatMessage Current => _current;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (++_index >= session.Count)
            {
                return false;
            }

            _current = session[_index];
            return true;
        }

        public void Reset()
        {
            _index = -1;
            // _current = default(T);
        }

        public void Dispose()
        {
        }
    }
}