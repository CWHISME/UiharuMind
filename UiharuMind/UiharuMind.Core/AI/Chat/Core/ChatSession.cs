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
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.Chat;

/// <summary>
/// 表示一个对话
/// </summary>
public class ChatSession //: INotifyPropertyChanged //: IEnumerable<ChatMessage>
    : IUniquieContainerItem
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

    /// <summary>
    /// 记忆
    /// </summary>
    public string MemeryName { get; set; } = "";


    public ChatHistory History { get; set; } = new ChatHistory();

    /// <summary>
    /// 是否不携带历史对话的上下文，如果为true则不携带，每次只有最后一句用户消息
    /// 注：仅工具角色有效，角色扮演 必定携带历史上下文
    /// </summary>
    public bool IsNotTakeHistoryContext { get; set; }

    //自定义参数
    public Dictionary<string, object?> CustomParams { get; set; } = new Dictionary<string, object?>();
    //========

    [JsonIgnore]
    public CharacterData CharacterData => _characterData ??= CharacterManager.Instance.GetCharacterData(CharaterId);

    [JsonIgnore]
    public MemoryData? Memery
    {
        get
        {
            if (_memory != null) return _memory;
            //如果没有指定记忆，则使用角色的默认记忆，同时将对话记忆设置为角色的默认记忆
            if (string.IsNullOrEmpty(MemeryName) ||
                !MemoryManager.Instance.TryGetMemoryData(MemeryName, out _memory))
            {
                _memory = CharacterData.Memery;
                if (_memory != null) MemeryName = CharacterData.MemeryName;
            }

            return _memory;
        }
        set
        {
            if (_memory == value) return;
            _memory = value;
            MemeryName = value?.Name ?? "";
            Save();
        }
    }

    [JsonIgnore] private ModelRunningData? _modelRunningData;

    /// <summary>
    /// 该对话对应的模型
    /// </summary>
    [JsonIgnore]
    public ModelRunningData? ChatModelRunningData
    {
        get => _modelRunningData ?? LlmManager.Instance.CurrentRunningModel;
        set => _modelRunningData = value;
    }

    /// <summary>
    /// 如果该字段为 true，则表示该对话为临时对话，触发存储时将会保存为一个全新的 Session
    /// </summary>
    [JsonIgnore]
    public bool IsDirty { get; set; } = false;

    //以 UTC 格式存储的时间戳
    public List<long> TimeStamps { get; set; } = new List<long>();

    public DateTime FirstTime =>
        TimeStamps.Count > 0 ? new DateTime(TimeStamps[0], DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue;

    public DateTime LastTime =>
        TimeStamps.Count > 0 ? new DateTime(TimeStamps[^1], DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue;

    private CharacterData? _characterData;
    private MemoryData? _memory;

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
        IsNotTakeHistoryContext = characterData.IsNotTakeHistoryContext;
        Description = string.IsNullOrEmpty(characterData.FirstGreeting)
            ? characterData.Description
            : characterData.FirstGreeting;

        //开场白，处理逻辑待定
        if (!string.IsNullOrEmpty(CharacterData.FirstGreeting))
        {
            AddMessage(AuthorRole.Assistant, CharacterData.TryRender(CharacterData.FirstGreeting));
        }
    }

    public void ReInitHistory(ChatHistory history, List<long>? timeStamps = null)
    {
        History = history;
        TimeStamps.Clear();
        if (timeStamps != null)
        {
            foreach (var time in timeStamps)
            {
                TimeStamps.Add(time);
            }
        }

        while (TimeStamps.Count < History.Count)
        {
            TimeStamps.Add(DateTime.UtcNow.Ticks);
        }
    }

    public int Count => History.Count;

    public ChatMessage this[int index] => new ChatMessage()
    {
        Message = History[index],
        Timestamp = index < TimeStamps.Count ? TimeStamps[index] : TimeStamps[^1]
    };

    /// <summary>
    /// 添加一条消息
    /// </summary>
    /// <param name="authorRole"></param>
    /// <param name="message"></param>
    /// <param name="imageBytes"></param>
    public void AddMessage(AuthorRole authorRole, string message, byte[]? imageBytes = null)
    {
        var chatMsg = CreateMessage(authorRole, message, imageBytes);

        //如果AI作为第一条消息，那么特殊处理下(特殊显示)
        if (History.Count == 0 && authorRole == AuthorRole.Assistant)
        {
#pragma warning disable SKEXP0001
            chatMsg.Message.AuthorName = ChatMessage.NarratorName;
#pragma warning restore SKEXP0001
        }

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
    /// <param name="imageBytes"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public ChatMessage CreateMessage(AuthorRole authorRole, string message, byte[]? imageBytes = null,
        long? timestamp = null)
    {
        ChatMessageContentItemCollection items = new ChatMessageContentItemCollection();
        if (imageBytes != null) items.Add(new ImageContent(imageBytes, "image/jpg"));
        if (!string.IsNullOrEmpty(message)) items.Add(new TextContent(message));
        return new ChatMessage()
        {
            Message = new ChatMessageContent(authorRole, items)
            {
#pragma warning disable SKEXP0001
                AuthorName = authorRole == AuthorRole.User
                    ? CharacterManager.Instance.UserCharacterName
                    : authorRole == AuthorRole.System
                        ? "System"
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
                prevMessage.CharacterName == lastMessage.CharacterName)
                return new AsyncEnumerableWithMessage("Error:A same assistant cannot generate message");
        }

        return ChatModelRunningData.InvokeAgentStreamingAsync(this, cancellationToken);
    }

    public async Task<ChatHistory> SafeGetHistory()
    {
        //检测是否额外挂载
        ChatHistory chatHistory = [];
        if (!CharacterData.IsTool)
        {
            StringBuilder sb = StringBuilderPool.Get();
            if (CharacterData.MountCharacters.Count > 0)
            {
                foreach (var mountChar in CharacterData.MountCharacters)
                {
                    var mountCharData = CharacterManager.Instance.GetCharacterData(mountChar);
                    if (string.IsNullOrEmpty(mountCharData.Template)) continue;
                    sb.AppendLine(CharacterData.TryRender(mountCharData.Template));
                    sb.AppendLine();
//                     chatHistory.Add(new ChatMessageContent(AuthorRole.System,
//                         CharacterData.TryRender(mountCharData.Template)));
// //                     {
// // #pragma warning disable SKEXP0001
// //                         AuthorName = mountCharData.CharacterName
// // #pragma warning restore SKEXP0001
// //                     });
                }
            }

            // //开场白，处理逻辑待定¸
            // if (!string.IsNullOrEmpty(CharacterData.FirstGreeting))
            // {
            //     chatHistory.Add(new ChatMessageContent(AuthorRole.System,
            //         CharacterData.TryRender(CharacterData.FirstGreeting)));
            // }
            //角色信息
            var user = CharacterManager.Instance.UserCharacterData;
            sb.Append($"{user.Description}的个人信息： ");
            // sb.Append("User information: ");
            // chatHistory.AddSystemMessage(user.Template.Replace("{{$char}}", user.Description));
            sb.AppendLine(user.Template.Replace("{{$char}}", user.Description));

            //对话模板，处理逻辑待定
            if (!string.IsNullOrEmpty(CharacterData.DialogTemplate))
            {
                sb.AppendLine(CharacterData.TryRender("Dialog Template:"));

                sb.AppendLine(CharacterData.TryRender(CharacterData.DialogTemplate));
                sb.AppendLine();
//                 chatHistory.Add(new ChatMessageContent(AuthorRole.System,
//                     $@"{CharacterData.TryRender(CharacterData.DialogTemplate)}")
//                 {
// #pragma warning disable SKEXP0001
//                     AuthorName = "ExampleRole"
// #pragma warning restore SKEXP0001
//                 });
//                 var dialogList = CharacterData.TryRender(CharacterData.DialogTemplate).Split('\n');
//                 foreach (var dialog in dialogList)
//                 {
//                     if (string.IsNullOrEmpty(dialog) || dialog.Length < 2) continue;
//                     chatHistory.Add(new ChatMessageContent(AuthorRole.System, dialog)
//                     {
// #pragma warning disable SKEXP0001
//                         AuthorName = "ExampleRole"
// #pragma warning restore SKEXP0001
//                     });
//                 }
//                 chatHistory.Add(new ChatMessageContent(AuthorRole.System,
//                     )
//                 {
// #pragma warning disable SKEXP0001
//                     AuthorName = "exampleRole"
// #pragma warning restore SKEXP0001
//                 });
            }

            chatHistory.Add(new ChatMessageContent(AuthorRole.System, sb.ToString()));
        }

        if (Memery != null)
        {
            var longTermMemory = await Memery.GetLongTermMemory(History[^1].Content ?? "");
            if (!string.IsNullOrEmpty(longTermMemory))
                chatHistory.AddSystemMessage("以下是通过文本嵌入模型搜索到的相关信息片段，用户当前的问题极有可能与之相关，请根据片段的相关性(Relevance)参数高低酌情参考：\n" +
                                             longTermMemory);
        }

        //工具角色，且选择不携带历史记录
        if (CharacterData.IsTool && IsNotTakeHistoryContext)
        {
            chatHistory.AddUserMessage(History[^1].Content ?? "");
            return chatHistory;
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
        ChatManager.Instance.Save(this, IsDirty);
        IsDirty = false;
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