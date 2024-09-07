using System.Collections;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.ChatCompletion;

namespace UiharuMind.Core.Core.Chat;

/// <summary>
/// 表示一个对话
/// </summary>
public class ChatSession //: IEnumerable<ChatMessage>
{
    [JsonInclude] private ChatHistory _history { get; set; } = new ChatHistory();
    [JsonInclude] private List<long> _timeStamps { get; set; } = new List<long>();

    private SessionEnumerator _enumerator;

    public ChatSession()
    {
        _enumerator = new SessionEnumerator(this);
    }

    public int Count => _history.Count;

    public ChatMessage this[int index] => new ChatMessage()
    {
        Message = _history[index],
        Timestamp = _timeStamps[index]
    };

    public void AddMessage(AuthorRole authorRole, string message)
    {
        _history.AddMessage(authorRole, message);
        _timeStamps.Add(DateTime.Now.Ticks);
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