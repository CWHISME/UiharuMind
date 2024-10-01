using System.Collections;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;

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

    private bool _isFinished = true;

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

    /// <summary>
    /// 添加一条消息
    /// </summary>
    /// <param name="authorRole"></param>
    /// <param name="message"></param>
    public void AddMessage(AuthorRole authorRole, string message)
    {
        History.AddMessage(authorRole, message);
        TimeStamps.Add(DateTime.UtcNow.Ticks);
    }

    // public IAsyncEnumerable<string> GenerateCompletionAsync(Action<ChatMessage> onStartCallback,
    //     Action<string> onStepCallback,
    //     Action<string> onCompletionCallback, CancellationToken token)

    /// <summary>
    /// 执行生成
    /// 如果当前最后一条消息不是 AI 回复，则生成 AI 回复
    /// 若当前最后一条消息是 AI 回复，则删除最后一条消息，并重新生成 AI 回复
    /// </summary>
    public IAsyncEnumerable<string> GenerateCompletionAsync(CancellationToken token)
    {
        if (History.Count == 0)
        {
            // Log.Warning("No message in chat session");
            return new AsyncEnumerableWithMessage("No message in chat session");
        }

        // if (History[History.Count - 1].Role ==AuthorRole.Assistant )
        // {
        //     History.RemoveAt(History.Count - 1);
        //     TimeStamps.RemoveAt(TimeStamps.Count - 1);
        // }
        // if (History.Count == 0) return;

        // LlmManager.Instance.CurrentRunningModel?.SendMessage(History, x =>
        // {
        //     if (_isFinished)
        //     {
        //         _isFinished = false;
        //         AddMessage(AuthorRole.Assistant, x);
        //         onStartCallback?.Invoke(this[^1]);
        //     }
        //     onStepCallback?.Invoke(x);
        // }, (x) =>
        // {
        //     _isFinished = true;
        //     History[^1].Content = x;
        //     onCompletionCallback(x);
        // }, token);

        if (LlmManager.Instance.CurrentRunningModel == null || !LlmManager.Instance.CurrentRunningModel.IsRunning)
        {
            return new AsyncEnumerableWithMessage("No running model");
        }

        // await foreach (string x in LlmManager.Instance.CurrentRunningModel.SendMessageAsync(History, token))
        // {
        //     yield return x;
        // }
        return LlmManager.Instance.CurrentRunningModel.SendMessageAsync(History, token);


        // LlmManager.Instance.CurrentRunningModel.SendMessage(History, x =>
        // {
        //     if (_isFinished)
        //     {
        //         _isFinished = false;
        //         AddMessage(AuthorRole.Assistant, x);
        //         onStartCallback?.Invoke(this[^1]);
        //     }
        //
        //     onStepCallback?.Invoke(x);
        // }, (x) =>
        // {
        //     _isFinished = true;
        //     History[^1].Content = x;
        //     onCompletionCallback(x);
        // }, token);
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