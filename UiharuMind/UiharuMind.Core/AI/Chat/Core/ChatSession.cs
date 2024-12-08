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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
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
    public string Name { get; set; } = "Empty";

    /// <summary>
    /// 本条对话描述
    /// </summary>
    public string Description { get; set; } = "Empty";

    /// <summary>
    /// 对应的角色 Id
    /// </summary>
    public string CharaterId { get; set; } = "Empty";

    public ChatHistory History { get; set; } = new ChatHistory();

    [JsonIgnore]
    public CharacterData CharacterData => _characterData ??= CharacterManager.Instance.GetCharacterData(CharaterId);

    //以 UTC 格式存储的时间戳
    public List<long> TimeStamps { get; set; } = new List<long>();

    public DateTime FirstTime => TimeStamps.Count > 0 ? new DateTime(TimeStamps[0], DateTimeKind.Utc) : DateTime.Now;
    public DateTime LastTime => TimeStamps.Count > 0 ? new DateTime(TimeStamps[^1], DateTimeKind.Utc) : DateTime.Now;

    private CharacterData? _characterData;

    // private bool _isFinished = true;

    private readonly SessionEnumerator _enumerator;

    //存储计数
    // private const int CountSavedMaxCounter = 6;
    // private int _countSavedCounter = CountSavedMaxCounter;

    // public event Action OnSessionChanged;

    public ChatSession()
    {
        _enumerator = new SessionEnumerator(this);
    }

    public ChatSession(string sessionName, CharacterData characterData) : this()
    {
        _characterData = characterData;
        CharaterId = characterData.CharacterName;
        Name = sessionName;
        Description = string.IsNullOrEmpty(characterData.FirstGreeting)
            ? characterData.Description
            : characterData.FirstGreeting;
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
        var chatMsg = CreateMessage(authorRole, message);
        History.Add(chatMsg.Message);
        // TimeStamps.Add(chatMsg.Timestamp);
        // if (_countSavedCounter-- <= 0)
        // {
        // Save();
        //     _countSavedCounter = CountSavedMaxCounter;
        // }
        AddMessageInfo(chatMsg.Timestamp);
    }

    /// <summary>
    /// 单纯添加信息对应的信息，不修改 History
    /// 因为 sk agent 会自动修改 History，所以这里只需要添加额外信息
    /// </summary>
    public void AddMessageInfo(long timestamp)
    {
        TimeStamps.Add(timestamp);
        Save();
        // OnSessionChanged?.Invoke();
    }

    /// <summary>
    /// 创建一条消息
    /// </summary>
    /// <param name="authorRole"></param>
    /// <param name="message"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public ChatMessage CreateMessage(AuthorRole authorRole, string message, long? timestamp = null)
    {
        return new ChatMessage()
        {
            Message = new ChatMessageContent(authorRole, message)
            {
#pragma warning disable SKEXP0001
                AuthorName = authorRole == AuthorRole.User
                    ? CharacterManager.Instance.UserCharacterName
                    : CharacterData.CharacterName
#pragma warning restore SKEXP0001
            },
            Timestamp = timestamp ?? DateTime.UtcNow.Ticks
        };
    }

    public void RemoveMessageAt(int index)
    {
        History.RemoveAt(index);
        TimeStamps.RemoveAt(index);
        Save();
    }

    public IAsyncEnumerable<string> GenerateCompletionStreaming(CancellationToken cancellationToken = default)
    {
        var lastMessage = this[^1];
        if (lastMessage.Character == ECharacter.Assistant && Count > 1)
        {
            //最后两条如是同一个 assistant 的话，不生成
            var prevMessage = this[^2];
            if (prevMessage.Character == ECharacter.Assistant &&
                prevMessage.CharactorName == lastMessage.CharactorName)
                return new AsyncEnumerableWithMessage("Error:A same assistant cannot generate message");
        }

        return LlmManager.Instance.CurrentRunningModel!.InvokeAgentStreamingAsync(this, cancellationToken);
    }

    public ChatHistory SafeGetHistory()
    {
        //检测是否额外挂载
        ChatHistory chatHistory = [];
        if (!CharacterData.IsTool)
        {
            if (CharacterData.MountCharacters.Count > 0)
            {
                foreach (var mountChar in CharacterData.MountCharacters)
                {
                    var mountCharData = CharacterManager.Instance.GetCharacterData(mountChar);
                    chatHistory.Add(new ChatMessageContent(AuthorRole.System,
                        CharacterData.TryRender(mountCharData.Template)));
//                     {
// #pragma warning disable SKEXP0001
//                         AuthorName = mountCharData.CharacterName
// #pragma warning restore SKEXP0001
//                     });
                }
            }

            //对话模板，处理逻辑待定
            if (!string.IsNullOrEmpty(CharacterData.DialogTemplate))
            {
                var dialogList = CharacterData.TryRender(CharacterData.DialogTemplate).Split('\n');
                foreach (var dialog in dialogList)
                {
                    chatHistory.Add(new ChatMessageContent(AuthorRole.System, dialog)
                    {
#pragma warning disable SKEXP0001
                        AuthorName = "exampleRole"
#pragma warning restore SKEXP0001
                    });
                }
//                 chatHistory.Add(new ChatMessageContent(AuthorRole.System,
//                     )
//                 {
// #pragma warning disable SKEXP0001
//                     AuthorName = "exampleRole"
// #pragma warning restore SKEXP0001
//                 });
            }

            //开场白，处理逻辑待定
            if (!string.IsNullOrEmpty(CharacterData.FirstGreeting))
            {
                chatHistory.Add(new ChatMessageContent(AuthorRole.System,
                    CharacterData.TryRender(CharacterData.FirstGreeting)));
            }

            //角色信息
            var user = CharacterManager.Instance.UserCharacterData;
            chatHistory.AddSystemMessage(user.Template.Replace("{{$char}}", user.Description));
        }

        chatHistory.AddRange(History);

        return chatHistory;
    }

    // public IAsyncEnumerable<string> GenerateCompletionAsync(Action<ChatMessage> onStartCallback,
    //     Action<string> onStepCallback,
    //     Action<string> onCompletionCallback, CancellationToken token)

    /// <summary>
    /// 执行生成
    /// 如果当前最后一条消息不是 AI 回复，则生成 AI 回复
    /// 若当前最后一条消息是 AI 回复，则删除最后一条消息，并重新生成 AI 回复
    /// </summary>
    // public void GenerateCompletionStreaming(Action onMessageStart, Action<ChatStreamingMessageInfo> onMessageReceived,
    //     Action<ChatStreamingMessageInfo> onMessageStopped,
    //     CancellationToken token)
    // {
    //     if (History.Count == 0)
    //     {
    //         // Log.Warning("No message in chat session");
    //         // return new AsyncEnumerableWithMessage("Error: No message in chat session");
    //         // onMessageReceived.Invoke(new ChatStreamingMessageInfo("Error: No message in chat session"));
    //         onMessageStopped.Invoke(new ChatStreamingMessageInfo("Error: No message in chat session"));
    //         return;
    //     }
    //
    //     if (LlmManager.Instance.CurrentRunningModel == null || !LlmManager.Instance.CurrentRunningModel.IsRunning)
    //     {
    //         // return new AsyncEnumerableWithMessage("Error: No running model");
    //         // onMessageReceived.Invoke(new ChatStreamingMessageInfo("Error: No running model"));
    //         onMessageStopped.Invoke(new ChatStreamingMessageInfo("Error: No running model"));
    //         return;
    //     }
    //
    //     if (!_isFinished)
    //     {
    //         // onMessageReceived.Invoke(new ChatStreamingMessageInfo("Error: Previous generation is not finished"));
    //         onMessageStopped.Invoke(new ChatStreamingMessageInfo("Error: Previous generation is not finished"));
    //         return;
    //     }
    //
    //     _isFinished = false;
    //     LlmManager.Instance.CurrentRunningModel.SendMessageStreaming(History, (x) =>
    //         {
    //             // TimeStamps[^1] = x.UtcTicks;
    //             onMessageStart.Invoke();
    //         },
    //         onMessageReceived,
    //         (x) =>
    //         {
    //             // History[^1].Content = x.Message;
    //             AddMessage(AuthorRole.Assistant, x.Message);
    //             onMessageStopped.Invoke(x);
    //             _isFinished = true;
    //         }, token);
    // }
    public void Save()
    {
        ChatManager.SaveChat(this);
    }

    public void Clear()
    {
        History.Clear();
        TimeStamps.Clear();
        // _isFinished = true;
    }

    public IEnumerator<ChatMessage> GetEnumerator()
    {
        _enumerator.Reset();
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