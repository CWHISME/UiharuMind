/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Chat;

/// <summary>
/// 表示一个对话
/// </summary>
public class ChatSession //: INotifyPropertyChanged //: IEnumerable<ChatMessage>
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

    public ChatHistory History { get; set; } = new ChatHistory();
    public CharacterData CharacterData { get; set; } = new CharacterData();

    //以 UTC 格式存储的时间戳
    public List<long> TimeStamps { get; set; } = new List<long>();

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
    public void GenerateCompletionStreaming(Action onMessageStart, Action<ChatStreamingMessageInfo> onMessageReceived,
        Action<ChatStreamingMessageInfo> onMessageStopped,
        CancellationToken token)
    {
        if (History.Count == 0)
        {
            // Log.Warning("No message in chat session");
            // return new AsyncEnumerableWithMessage("Error: No message in chat session");
            // onMessageReceived.Invoke(new ChatStreamingMessageInfo("Error: No message in chat session"));
            onMessageStopped.Invoke(new ChatStreamingMessageInfo("Error: No message in chat session"));
            return;
        }

        if (LlmManager.Instance.CurrentRunningModel == null || !LlmManager.Instance.CurrentRunningModel.IsRunning)
        {
            // return new AsyncEnumerableWithMessage("Error: No running model");
            // onMessageReceived.Invoke(new ChatStreamingMessageInfo("Error: No running model"));
            onMessageStopped.Invoke(new ChatStreamingMessageInfo("Error: No running model"));
            return;
        }

        if (!_isFinished)
        {
            // onMessageReceived.Invoke(new ChatStreamingMessageInfo("Error: Previous generation is not finished"));
            onMessageStopped.Invoke(new ChatStreamingMessageInfo("Error: Previous generation is not finished"));
            return;
        }

        _isFinished = false;
        LlmManager.Instance.CurrentRunningModel.SendMessageStreaming(History, (x) =>
            {
                TimeStamps[^1] = x.UtcTicks;
                onMessageStart.Invoke();
            },
            onMessageReceived,
            (x) =>
            {
                History[^1].Content = x.Message;
                onMessageStopped.Invoke(x);
                _isFinished = true;
            }, token);
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

    // public event PropertyChangedEventHandler? PropertyChanged;
    //
    // protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    // {
    //     PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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