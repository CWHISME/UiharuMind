using System.Collections;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Core.Chat;

/// <summary>
/// 表示一个对话。持久化数据使用项目自有消息模型，不依赖具体 AI SDK。
/// </summary>
public class ChatSession : IUniquieContainerItem
{
    public int FormatVersion { get; set; } = 2;
    public string Name { get; set; } = "Empty";
    public string Description { get; set; } = "Empty";
    public string CharaterId { get; set; } = "Empty";
    public string MemeryName { get; set; } = "";
    public List<ChatMessageData> History { get; set; } = [];
    /// <summary>
    /// 是否不携带历史对话的上下文，如果为true则不携带，每次只有最后一句用户消息
    /// 注：仅工具角色有效，角色扮演 必定携带历史上下文
    /// </summary>
    public bool IsNotTakeHistoryContext { get; set; }
    //自定义参数
    public Dictionary<string, object?> CustomParams { get; set; } = [];

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
    [JsonIgnore] public bool IsDirty { get; set; }

    public DateTime FirstTime => History.Count > 0
        ? new DateTime(History[0].Timestamp, DateTimeKind.Utc).ToLocalTime()
        : DateTime.MinValue;

    public DateTime LastTime => History.Count > 0
        ? new DateTime(History[^1].Timestamp, DateTimeKind.Utc).ToLocalTime()
        : DateTime.MinValue;

    private CharacterData? _characterData;
    private MemoryData? _memory;
    private readonly SessionEnumerator _enumerator;

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
        if (!string.IsNullOrEmpty(characterData.FirstGreeting))
            AddMessage(ECharacter.Assistant, characterData.TryRender(characterData.FirstGreeting));
    }

    public void ReInitHistory(IEnumerable<ChatMessageData> history)
    {
        History = history.ToList();
    }

    public int Count => History.Count;

    public ChatMessage this[int index] => new() { Message = History[index] };

    public void AddMessage(ECharacter authorRole, string message, byte[]? imageBytes = null)
    {
        ChatMessageData data = CreateMessage(authorRole, message, imageBytes).Message;
        //如果AI作为第一条消息，那么特殊处理下(特殊显示)
        if (History.Count == 0 && authorRole == ECharacter.Assistant)
            data.AuthorName = ChatMessage.NarratorName;

        History.Add(data);
        Save();
    }

    public ChatMessage CreateMessage(ECharacter authorRole, string message, byte[]? imageBytes = null,
        long? timestamp = null)
    {
        return new ChatMessage
        {
            Message = new ChatMessageData
            {
                Role = authorRole,
                AuthorName = authorRole switch
                {
                    ECharacter.User => CharacterManager.Instance.UserCharacterName,
                    ECharacter.System => "System",
                    _ => CharacterData.CharacterName
                },
                Content = message,
                ImageBytes = imageBytes,
                Timestamp = timestamp ?? DateTime.UtcNow.Ticks
            }
        };
    }

    public void AddGeneratedAssistantMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        History.Add(CreateMessage(ECharacter.Assistant, content).Message);
        Save();
    }

    public void RemoveMessageAt(int index)
    {
        History.RemoveAt(index);
        Save();
    }

    public IAsyncEnumerable<string> GenerateCompletionStreaming(CancellationToken cancellationToken = default)
    {
        if (Count == 0) return new AsyncEnumerableWithMessage("Error: No message");

        ChatMessage lastMessage = this[^1];
        if (lastMessage.Character == ECharacter.Assistant && Count > 1)
        {
            ChatMessage previous = this[^2];
            if (previous.Character == ECharacter.Assistant &&
                previous.CharacterName == lastMessage.CharacterName)
                return new AsyncEnumerableWithMessage("Error:A same assistant cannot generate message");
        }

        return ChatModelRunningData.InvokeAgentStreamingAsync(this, cancellationToken);
    }

    public async Task<List<AIChatMessage>> BuildRequestMessagesAsync()
    {
        List<AIChatMessage> messages = [];

        if (!CharacterData.IsTool)
        {
            StringBuilder sb = StringBuilderPool.Get();
            foreach (string mountCharacter in CharacterData.MountCharacters)
            {
                CharacterData mounted = CharacterManager.Instance.GetCharacterData(mountCharacter);
                if (string.IsNullOrEmpty(mounted.Template)) continue;
                sb.AppendLine(CharacterData.TryRender(mounted.Template));
                sb.AppendLine();
            }

            CharacterData user = CharacterManager.Instance.UserCharacterData;
            sb.Append($"{user.Description}的个人信息： ");
            sb.AppendLine(user.Template.Replace("{{$char}}", user.Description));

            if (!string.IsNullOrEmpty(CharacterData.DialogTemplate))
            {
                sb.AppendLine("Dialog Template:");
                sb.AppendLine(CharacterData.TryRender(CharacterData.DialogTemplate));
            }

            messages.Add(new AIChatMessage(ChatRole.System, sb.ToString()));
            StringBuilderPool.Release(sb);
        }

        if (Memery != null && History.Count > 0)
        {
            string longTermMemory = await Memery.GetLongTermMemory(History[^1].Content);
            if (!string.IsNullOrEmpty(longTermMemory))
            {
                messages.Add(new AIChatMessage(ChatRole.System,
                    "以下是通过文本嵌入模型搜索到的相关信息片段，用户当前的问题极有可能与之相关，请根据片段的相关性(Relevance)参数高低酌情参考：\n" +
                    longTermMemory));
            }
        }

        if (CharacterData.IsTool && IsNotTakeHistoryContext)
        {
            messages.Add(History[^1].ToAIMessage());
            return messages;
        }

        messages.AddRange(History.Select(x => x.ToAIMessage()));
        return messages;
    }

    public void Save()
    {
        ChatManager.Instance.Save(this, IsDirty);
        IsDirty = false;
    }

    public void Clear()
    {
        History.Clear();
        Save();
    }

    public IEnumerator<ChatMessage> GetEnumerator()
    {
        _enumerator.Reset();
        return _enumerator;
    }

    private sealed class SessionEnumerator(ChatSession session) : IEnumerator<ChatMessage>
    {
        private int _index = -1;
        private ChatMessage _current;

        public ChatMessage Current => _current;
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (++_index >= session.Count) return false;
            _current = session[_index];
            return true;
        }

        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}
